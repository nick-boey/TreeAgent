import {
  query,
  type SDKMessage,
  type Query,
  type PermissionResult,
} from "@anthropic-ai/claude-agent-sdk";
import { SpanKind, trace } from "@opentelemetry/api";
import type {
  SessionInfo,
  AskUserQuestionInput,
  ExitPlanModeInput,
  UserQuestion,
  LastMessageType,
} from "../types/index.js";
import { randomUUID } from "node:crypto";
import {
  info,
  error,
  warn,
  debug,
  sdkDebug,
  isSdkDebugEnabled,
} from "../utils/otel-logger.js";
import {
  buildInventoryFromInit,
  emitInventoryLog,
  resolveToolOrigin,
  type SdkInitMessageLike,
} from "./session-inventory.js";
import {
  captureDebugInfo,
  FileMonitor,
} from "../utils/diagnostics.js";
import { watch, existsSync } from "node:fs";
import { InputQueue } from "./input-queue.js";


export type SdkPermissionMode =
  | "default"
  | "acceptEdits"
  | "plan"
  | "bypassPermissions";

const MODE_MAP: Record<string, SdkPermissionMode> = {
  Plan: "plan",
  Build: "bypassPermissions",
};

export function mapMode(value: string | undefined): SdkPermissionMode {
  if (!value) return "bypassPermissions";
  return MODE_MAP[value] ?? "bypassPermissions";
}

// --- OutputChannel types ---

export interface ControlEvent {
  type: "question_pending" | "plan_pending" | "status_resumed";
  data:
    | { questions: UserQuestion[] }
    | { plan: string }
    | Record<string, never>;
}

export type OutputEvent = SDKMessage | ControlEvent;

export function isControlEvent(event: OutputEvent): event is ControlEvent {
  if (!("type" in event) || !("data" in event)) return false;
  const t = (event as ControlEvent).type;
  return (
    t === "question_pending" ||
    t === "plan_pending" ||
    t === "status_resumed"
  );
}

/**
 * Single-consumer async queue that merges pushes from multiple producers
 * (the SDK query background task and the canUseTool callback).
 *
 * ## Invariants
 *
 * - **Single consumer at a time.** The channel delivers each event to exactly
 *   one consumer. Per-HTTP-request SSE streams consume the channel
 *   sequentially, but never concurrently. The behavior under concurrent
 *   `for await` loops is undefined — whichever consumer installed the most
 *   recent resolver receives the next push.
 * - **Events survive consumer abort.** When a consumer exits its `for await`
 *   (e.g. because `streamSessionEvents` returns on a `result`), the pending
 *   resolver MUST be cleared in the generator's `finally` block so that any
 *   subsequent `push(...)` lands in the internal buffer rather than invoking
 *   a stale promise resolver (which would silently drop the event). The next
 *   consumer then drains from the buffer.
 */
export class OutputChannel {
  private queue: OutputEvent[] = [];
  private resolver: ((result: IteratorResult<OutputEvent>) => void) | null =
    null;
  private done = false;

  push(event: OutputEvent): void {
    if (this.done) return;

    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: event, done: false });
    } else {
      this.queue.push(event);
    }
  }

  complete(): void {
    this.done = true;
    if (this.resolver) {
      const resolve = this.resolver;
      this.resolver = null;
      resolve({ value: undefined as unknown as OutputEvent, done: true });
    }
  }

  async *[Symbol.asyncIterator](): AsyncGenerator<OutputEvent> {
    // If a prior iterator is still suspended on an un-settled promise (e.g.
    // an aborted SSE consumer whose runtime never called .return()), its
    // resolver is still attached to this channel. Complete it with done:true
    // so it unblocks and runs its own finally, then claim the slot for this
    // new iterator. Without this, the next push() would deliver the event to
    // the orphaned resolver and the new consumer would never see it.
    if (this.resolver) {
      const stale = this.resolver;
      this.resolver = null;
      stale({ value: undefined as unknown as OutputEvent, done: true });
    }

    // Track which resolver this iterator installed so finally only clears
    // its own subscription and not a resolver that belongs to a later
    // iterator that supplanted it.
    let myResolver: ((r: IteratorResult<OutputEvent>) => void) | null = null;

    try {
      while (true) {
        if (this.queue.length > 0) {
          yield this.queue.shift()!;
        } else if (this.done) {
          return;
        } else {
          const result = await new Promise<IteratorResult<OutputEvent>>(
            (resolve) => {
              myResolver = resolve;
              this.resolver = resolve;
            },
          );
          myResolver = null;
          if (result.done) return;
          yield result.value;
        }
      }
    } finally {
      // Clear the resolver slot only if it still points at ours — a later
      // iterator may have supplanted it by now, and we must not clobber
      // that. Without this, events produced between two per-turn SSE
      // consumer iterations would be silently dropped.
      if (myResolver && this.resolver === myResolver) {
        this.resolver = null;
      }
    }
  }
}

// --- Pending-interaction types ---

export type PendingKind = "question" | "plan";

/**
 * Payload used by `resolvePending` for each kind.
 * - `question`: the record of answers keyed by question text.
 * - `plan`: the approval decision with optional context control.
 */
export type PendingPayload =
  | { kind: "question"; answers: Record<string, string> }
  | {
      kind: "plan";
      approved: boolean;
      keepContext?: boolean;
      feedback?: string;
    };

/**
 * Per-session, per-kind state tracked while the SDK awaits a user decision.
 * `data` is the interaction-specific captured data (e.g. the questions to
 * answer, or the plan to approve).
 */
interface PendingInteraction<T> {
  data: T;
  resolve: (result: PermissionResult) => void;
  reject: (err: Error) => void;
}

// --- Session types ---

interface WorkerSession {
  id: string;
  query: Query;
  /**
   * Persistent input iterable supplied to `query({ prompt })`. The initial
   * user prompt is pushed here on session create; follow-up user messages
   * are pushed here by `send()`. Closed by `close()` so the session's
   * `query()` lifecycle ends cleanly.
   */
  inputQueue: InputQueue;
  outputChannel: OutputChannel;
  conversationId?: string;
  mode: string;
  model: string;
  permissionMode: SdkPermissionMode;
  status: "idle" | "streaming" | "closed" | "error";
  createdAt: Date;
  lastActivityAt: Date;
  lastMessageType?: LastMessageType;
  lastMessageSubtype?: string;
  systemPrompt?: string;
  workingDirectory?: string;
  /**
   * First `system`/`init` message observed on this session. Used by
   * `canUseTool` to resolve tool origin without re-awaiting the SDK.
   * Not part of any shared DTO — internal to the worker.
   */
  init?: SdkInitMessageLike;
  /**
   * Whether an `inventory event=...` log has already been emitted for the
   * current session lifecycle phase. Guard against the SDK replaying init.
   */
  inventoryEmitted?: boolean;
  /**
   * Cleanup handle for the debug-log file watcher attached in create().
   * Invoked from runQueryForwarder's finally block.
   */
  debugLogCleanup?: () => void;
}

interface SDKUserMessage {
  type: "user";
  session_id: string;
  message: {
    role: "user";
    content: Array<{ type: "text"; text: string }>;
  };
  parent_tool_use_id: string | null;
}

const PLAN_MODE_TOOLS = [
  "Read",
  "Glob",
  "Grep",
  "WebFetch",
  "WebSearch",
  "Task",
  "AskUserQuestion",
  "ExitPlanMode",
  // Required to let the agent invoke discovered Agent Skills. Skills are
  // loaded from `.claude/skills/` under the session cwd and `~/.claude/skills/`
  // via `settingSources: ["user", "project"]`. Without "Skill" in the allowed
  // list, the Skill tool is not exposed to the model — see
  // https://code.claude.com/docs/en/agent-sdk/skills.
  "Skill",
];

function buildCommonOptions(
  model: string,
  systemPrompt?: string,
  workingDirectory?: string,
) {
  const cwd = workingDirectory || process.env.WORKING_DIRECTORY || "/workdir";

  const systemPromptOption = systemPrompt
    ? {
        type: "preset" as const,
        preset: "claude_code" as const,
        append: systemPrompt,
      }
    : { type: "preset" as const, preset: "claude_code" as const };

  const gitAuthorName = process.env.GIT_AUTHOR_NAME || "Homespun Bot";
  const gitAuthorEmail = process.env.GIT_AUTHOR_EMAIL || "homespun@localhost";
  const githubToken =
    process.env.GITHUB_TOKEN || process.env.GitHub__Token || "";

  return {
    model,
    cwd,
    includePartialMessages: false,
    settingSources: ["user" as const, "project" as const],
    betas: [],
    systemPrompt: systemPromptOption,
    mcpServers: {
      playwright: {
        type: "stdio" as const,
        command: "npx",
        args: [
          "@playwright/mcp@latest",
          "--headless",
          "--browser",
          "chromium",
          "--no-sandbox",
          "--isolated",
        ],
        env: {
          PLAYWRIGHT_BROWSERS_PATH:
            process.env.PLAYWRIGHT_BROWSERS_PATH || "/opt/playwright-browsers",
        },
      },
    },
    env: {
      ...process.env,
      GIT_AUTHOR_NAME: gitAuthorName,
      GIT_AUTHOR_EMAIL: gitAuthorEmail,
      GIT_COMMITTER_NAME: process.env.GIT_COMMITTER_NAME || gitAuthorName,
      GIT_COMMITTER_EMAIL: process.env.GIT_COMMITTER_EMAIL || gitAuthorEmail,
      GITHUB_TOKEN: githubToken,
      GH_TOKEN: githubToken,
    },
  };
}

/**
 * Strip credentials from an object before passing it to `sdkDebug`. Mutates
 * the returned shallow copy — the original `sessionOptions` is not modified.
 */
function redactSessionOptionsForLog(
  options: Record<string, unknown>,
): Record<string, unknown> {
  const copy: Record<string, unknown> = { ...options };
  if (copy.env && typeof copy.env === "object") {
    const env = { ...(copy.env as Record<string, unknown>) };
    for (const key of [
      "GITHUB_TOKEN",
      "GH_TOKEN",
      "GitHub__Token",
      "CLAUDE_CODE_OAUTH_TOKEN",
      "ANTHROPIC_API_KEY",
    ]) {
      if (key in env) env[key] = "[REDACTED]";
    }
    copy.env = env;
  }
  // Don't spill the canUseTool callback's source.
  if (typeof copy.canUseTool === "function") {
    copy.canUseTool = "[Function]";
  }
  return copy;
}

/**
 * Attaches a filesystem watcher on the Claude SDK debug log and streams new
 * lines to the worker's info log. Safe to call once per session; the returned
 * cleanup fn is invoked when the session ends.
 */
function attachDebugLogStreaming(sessionId: string): { cleanup: () => void } {
  const debugLogPath = "/home/homespun/.claude/debug/claude_sdk_debug.log";
  const monitor = new FileMonitor(debugLogPath);
  let watcher: ReturnType<typeof watch> | undefined;

  if (existsSync(debugLogPath)) {
    watcher = watch(
      debugLogPath,
      { persistent: false },
      async (eventType) => {
        if (eventType === "change") {
          const newLines = await monitor.readNewLines();
          newLines.forEach((line) => {
            info(`[SDK Debug] ${line} (sessionId: ${sessionId})`);
          });
        }
      },
    );
  }

  return {
    cleanup: () => {
      watcher?.close();
    },
  };
}

// --- Tool handler registry ---

/**
 * Shared context passed to every tool handler. Exposes the manager's
 * internal pending-state primitives without leaking them on the public API.
 */
interface HandlerContext {
  sessionId: string;
  registerPending: <T>(
    kind: PendingKind,
    data: T,
    resolve: (result: PermissionResult) => void,
    reject: (err: Error) => void,
  ) => void;
  logStatusChange: (session: WorkerSession, next: WorkerSession["status"], reason: string) => void;
}

type ToolHandler = (
  input: Record<string, unknown>,
  session: WorkerSession,
  ctx: HandlerContext,
) => Promise<PermissionResult>;

const askUserQuestionHandler: ToolHandler = async (input, session, ctx) => {
  const questionInput = input as unknown as AskUserQuestionInput;

  const answersPromise = new Promise<Record<string, string>>(
    (resolve, reject) => {
      info(
        `Session entering pending question state (sessionId: ${ctx.sessionId}, questionCount: ${questionInput.questions.length})`,
      );
      ctx.registerPending(
        "question",
        { questions: questionInput.questions },
        (result) => {
          // The unified resolver builds a PermissionResult for us, but the
          // question handler needs just the answers object to complete.
          // We unwrap updatedInput.answers back into a plain record.
          const answers =
            (result as { behavior: "allow"; updatedInput: { answers: Record<string, string> } })
              .updatedInput.answers;
          resolve(answers);
        },
        reject,
      );
    },
  );

  session.outputChannel.push({
    type: "question_pending",
    data: { questions: questionInput.questions },
  });

  info(
    `emitted question_pending, waiting for answers on session '${ctx.sessionId}'`,
  );

  const answers = await answersPromise;

  info(`received answers for session '${ctx.sessionId}'`);

  ctx.logStatusChange(session, "streaming", "resuming_after_question");

  return {
    behavior: "allow",
    updatedInput: {
      questions: questionInput.questions,
      answers,
    },
  };
};

const exitPlanModeHandler: ToolHandler = async (input, session, ctx) => {
  const planInput = input as unknown as ExitPlanModeInput;
  const planContent = planInput.plan || "";

  const approvalPromise = new Promise<PermissionResult>((resolve, reject) => {
    info(
      `Session entering pending plan approval state (sessionId: ${ctx.sessionId})`,
    );
    ctx.registerPending("plan", { plan: planContent }, resolve, reject);
  });

  session.outputChannel.push({
    type: "plan_pending",
    data: { plan: planContent },
  });

  info(
    `emitted plan_pending, waiting for approval on session '${ctx.sessionId}'`,
  );

  const result = await approvalPromise;

  info(
    `received plan approval decision for session '${ctx.sessionId}': behavior='${result.behavior}'`,
  );

  if (result.behavior === "allow") {
    ctx.logStatusChange(session, "streaming", "resuming_after_plan_approval");
  }

  return result;
};

const TOOL_HANDLERS: Record<string, ToolHandler> = {
  AskUserQuestion: askUserQuestionHandler,
  ExitPlanMode: exitPlanModeHandler,
};

export class SessionManager {
  private sessions = new Map<string, WorkerSession>();
  /**
   * Unified pending-interaction map: sessionId → (kind → interaction).
   * One-to-one between session+kind and outstanding promise.
   */
  private pending = new Map<
    string,
    Map<PendingKind, PendingInteraction<unknown>>
  >();

  /**
   * Helper method to log status changes with consistent format.
   */
  private logStatusChange(
    session: WorkerSession,
    newStatus: WorkerSession["status"],
    reason: string,
  ): void {
    const previousStatus = session.status;
    session.status = newStatus;
    info(
      `Session status changed: ${previousStatus} → ${newStatus} (sessionId: ${session.id}, reason: ${reason})`,
    );
  }

  private registerPending<T>(
    sessionId: string,
    kind: PendingKind,
    data: T,
    resolve: (result: PermissionResult) => void,
    reject: (err: Error) => void,
  ): void {
    let bySession = this.pending.get(sessionId);
    if (!bySession) {
      bySession = new Map();
      this.pending.set(sessionId, bySession);
    }
    bySession.set(kind, {
      data,
      resolve,
      reject,
    } as PendingInteraction<unknown>);
  }

  private popPending(
    sessionId: string,
    kind: PendingKind,
  ): PendingInteraction<unknown> | undefined {
    const bySession = this.pending.get(sessionId);
    if (!bySession) return undefined;
    const entry = bySession.get(kind);
    if (!entry) return undefined;
    bySession.delete(kind);
    if (bySession.size === 0) this.pending.delete(sessionId);
    return entry;
  }

  async create(opts: {
    prompt: string;
    model: string;
    mode: string;
    systemPrompt?: string;
    workingDirectory?: string;
    resumeSessionId?: string;
  }): Promise<WorkerSession> {
    const id = randomUUID();
    const isPlan = opts.mode.toLowerCase() === "plan";
    const startTime = Date.now();

    info(
      `Creating session ${id} - mode='${opts.mode}', isPlan=${isPlan}, model='${opts.model}', workingDirectory='${opts.workingDirectory || process.env.WORKING_DIRECTORY || "/workdir"}', systemPromptLength=${opts.systemPrompt?.length || 0}, resumeSessionId='${opts.resumeSessionId}'`,
    );

    const common = buildCommonOptions(
      opts.model,
      opts.systemPrompt,
      opts.workingDirectory,
    );

    const canUseTool = this.createCanUseToolCallback(id);

    const sessionOptions: Record<string, unknown> = {
      ...common,
      canUseTool,
    };

    if (isPlan) {
      sessionOptions.permissionMode = "plan";
      sessionOptions.allowedTools = PLAN_MODE_TOOLS;
    } else {
      sessionOptions.permissionMode = "bypassPermissions";
      sessionOptions.allowDangerouslySkipPermissions = true;
      // Build mode bypasses permissions so allowedTools is not used as a
      // deny-list. Reuse PLAN_MODE_TOOLS so Build has at least the same
      // exposure as Plan (notably "Skill" — which must be listed explicitly
      // to be exposed to the model at all). The canUseTool callback handles
      // everything else. See https://code.claude.com/docs/en/agent-sdk/skills.
      sessionOptions.allowedTools = PLAN_MODE_TOOLS;
    }

    if (opts.resumeSessionId) {
      sessionOptions.resume = opts.resumeSessionId;
    }

    info(
      `Session configuration: permissionMode='${sessionOptions.permissionMode}', allowDangerouslySkipPermissions=${sessionOptions.allowDangerouslySkipPermissions}, sessionId='${id}', allowedTools=${JSON.stringify(sessionOptions.allowedTools)}, hasCanUseTool=true, hasResume=${!!opts.resumeSessionId}`,
    );

    // Build the initial prompt as the first entry in a persistent InputQueue.
    // The queue's iterator never returns until close() is called, which keeps
    // the SDK's internal streamInput suspended between turns and prevents
    // transport.endInput() from closing stdin to the CLI. Follow-up messages
    // are delivered by pushing into this same queue rather than by invoking
    // q.streamInput() — see scripts/spike-idle-tolerance.ts for the contract
    // and the `fix-worker-streaminput-multi-turn` OpenSpec change for the why.
    const initialPrompt: SDKUserMessage = {
      type: "user",
      session_id: "",
      message: {
        role: "user",
        content: [{ type: "text", text: opts.prompt }],
      },
      parent_tool_use_id: null,
    };

    const inputQueue = new InputQueue();

    // Capture point 1: full session options just before query({...}). Gated
    // on DEBUG_AGENT_SDK. Credentials are redacted from options.env.
    if (isSdkDebugEnabled()) {
      warn(
        `DEBUG_AGENT_SDK is enabled — high log volume expected (sessionId: ${id})`,
      );
      sdkDebug("tx", redactSessionOptionsForLog(sessionOptions));
    }

    // Capture point 2: initial user message pushed into the input queue.
    if (isSdkDebugEnabled()) {
      sdkDebug("tx", initialPrompt);
    }
    inputQueue.push(initialPrompt);

    const q = query({
      prompt: inputQueue,
      options: sessionOptions as Parameters<typeof query>[0]["options"],
    });

    const outputChannel = new OutputChannel();

    const workerSession: WorkerSession = {
      id,
      query: q,
      inputQueue,
      outputChannel,
      conversationId: opts.resumeSessionId,
      mode: opts.mode,
      model: opts.model,
      permissionMode: isPlan ? "plan" : "bypassPermissions",
      status: "idle",
      createdAt: new Date(),
      lastActivityAt: new Date(),
      systemPrompt: opts.systemPrompt,
      workingDirectory: opts.workingDirectory,
    };

    this.sessions.set(id, workerSession);
    info(`session created, workerSessionId='${id}'`);

    this.logStatusChange(workerSession, "streaming", "session_created");

    const debugHandle = attachDebugLogStreaming(id);
    workerSession.debugLogCleanup = debugHandle.cleanup;

    this.runQueryForwarder(
      workerSession,
      q,
      outputChannel,
      startTime,
      opts,
      sessionOptions,
    );

    return workerSession;
  }

  /**
   * Creates the canUseTool callback for a session. Delegates to the tool
   * handler registry; unknown tools are allowed with their input unchanged.
   */
  private createCanUseToolCallback(sessionId: string) {
    return async (
      toolName: string,
      input: Record<string, unknown>,
    ): Promise<PermissionResult> => {
      const session = this.sessions.get(sessionId);
      const origin = resolveToolOrigin(toolName, session?.init);
      info(
        `canUseTool - tool='${toolName}', sessionId='${sessionId}' origin=${origin}`,
      );

      const handler = TOOL_HANDLERS[toolName];
      if (handler && session) {
        const ctx: HandlerContext = {
          sessionId,
          registerPending: (kind, data, resolve, reject) =>
            this.registerPending(sessionId, kind, data, resolve, reject),
          logStatusChange: (s, next, reason) => this.logStatusChange(s, next, reason),
        };
        return handler(input, session, ctx);
      }

      return {
        behavior: "allow",
        updatedInput: input,
      };
    };
  }

  /**
   * Runs the background query forwarder that reads SDK messages and pushes
   * them to the output channel. There is a single forwarder per session
   * lifetime. Follow-up user messages are delivered by pushing into the
   * session's persistent `InputQueue` (the same iterable supplied as the
   * initial `prompt` to `query()`), never via `q.streamInput(...)` — see
   * the `fix-worker-streaminput-multi-turn` OpenSpec change for the SDK
   * contract that motivates this design.
   */
  private runQueryForwarder(
    session: WorkerSession,
    q: Query,
    outputChannel: OutputChannel,
    startTime: number,
    opts: {
      mode: string;
      model: string;
      systemPrompt?: string;
      workingDirectory?: string;
    },
    sessionOptions: Record<string, unknown>,
  ): void {
    // Per-message `homespun.sdk.rx` spans wrap each iteration of the SDK
    // Query forwarder. Motivation: the Hono SERVER span for POST /api/sessions
    // ends as soon as the handler returns (~ms), and any long-lived span
    // around the full stream would never end for an idle-but-open session,
    // so `BatchSpanProcessor` would never export it and Aspire's waterfall
    // could not nest the logs. A short-lived span per yielded SDK message
    // gives each `sdkDebug("rx", ...)` log a span whose time window covers
    // it and which is exported promptly.
    const sessionTracer = trace.getTracer("homespun.worker.session");
    (async () => {
      try {
        info(`Query processing started (sessionId: ${session.id})`);
        let messageCount = 0;
        session.inventoryEmitted = false;
        for await (const msg of q) {
          await sessionTracer.startActiveSpan("homespun.sdk.rx", {
            kind: SpanKind.INTERNAL,
            attributes: {
              "homespun.session.id": session.id,
              "homespun.sdk.message.type": msg.type,
              "homespun.sdk.message.subtype":
                (msg as { subtype?: string }).subtype ?? "",
            },
          }, async (span) => {
            try {
              if (isSdkDebugEnabled()) {
                sdkDebug("rx", msg);
              }
              if (
                !session.inventoryEmitted &&
                msg.type === "system" &&
                (msg as { subtype?: string }).subtype === "init"
              ) {
                session.inventoryEmitted = true;
                const initLike = msg as unknown as SdkInitMessageLike;
                session.init = initLike;
                try {
                  const record = await buildInventoryFromInit(
                    initLike,
                    sessionOptions,
                    "create",
                    session.id,
                  );
                  emitInventoryLog(record);
                } catch (err) {
                  warn(
                    `inventory emission failed for session '${session.id}': ${
                      err instanceof Error ? err.message : String(err)
                    }`,
                  );
                }
              }

              outputChannel.push(msg);
              messageCount++;
            } finally {
              span.end();
            }
          });
        }
        const duration = Date.now() - startTime;
        info(
          `Query processing completed (sessionId: ${session.id}, messageCount: ${messageCount}, duration: ${duration}ms)`,
        );
        this.logStatusChange(session, "idle", "query_completed");
      } catch (err) {
        const duration = Date.now() - startTime;
        const errorMessage = err instanceof Error ? err.message : String(err);

        const errorDetails = {
          message: errorMessage,
          stack: err instanceof Error ? err.stack : undefined,
          type: err?.constructor?.name || "Unknown",
          sessionId: session.id,
          sessionOptions: {
            mode: opts.mode,
            model: opts.model,
            permissionMode: sessionOptions.permissionMode,
            workingDirectory: sessionOptions.cwd,
          },
          timestamp: new Date().toISOString(),
          duration,
        };

        error(
          `query forwarder error for session '${session.id}' - ${JSON.stringify(errorDetails)}`,
        );
        this.logStatusChange(session, "error", "query_error");

        const debugInfo = await captureDebugInfo(this.sessions.size);

        outputChannel.push({
          type: "result",
          subtype: "error_during_execution",
          session_id: session.id,
          is_error: true,
          duration_ms: duration,
          duration_api_ms: 0,
          num_turns: 0,
          total_cost_usd: 0,
          usage: {
            input_tokens: 0,
            output_tokens: 0,
            cache_creation_input_tokens: 0,
            cache_read_input_tokens: 0,
          },
          result: errorDetails.message,
          errors: [errorDetails.message],
          debug: {
            stack: errorDetails.stack,
            errorType: errorDetails.type,
            lastStderr: debugInfo.lastStderr.slice(-10),
            diagnostics: {
              memory: debugInfo.diagnostics.memory,
              uptime: debugInfo.diagnostics.uptime,
              sessionCount: debugInfo.sessionCount,
            },
            sessionOptions: errorDetails.sessionOptions,
          },
        } as unknown as SDKMessage);
      } finally {
        outputChannel.complete();
        session.debugLogCleanup?.();
        session.debugLogCleanup = undefined;
      }
    })();
  }

  /**
   * Resolves a pending interaction for a session and kind. The payload
   * is unpacked per kind:
   * - `question`: `{ answers }` becomes a PermissionResult with answers bound
   *   into `updatedInput`.
   * - `plan`: `{ approved, keepContext?, feedback? }` is mapped to an
   *   allow/deny PermissionResult matching the legacy ExitPlanMode semantics.
   *
   * Returns true if a matching pending interaction was resolved, false if
   * none was outstanding.
   */
  resolvePending(
    sessionId: string,
    kind: PendingKind,
    payload: Record<string, unknown>,
  ): boolean {
    const entry = this.popPending(sessionId, kind);
    if (!entry) {
      info(
        `resolvePending - no pending ${kind} for session '${sessionId}'`,
      );
      return false;
    }

    const ws = this.sessions.get(sessionId);

    if (kind === "question") {
      const answers = (payload.answers ?? {}) as Record<string, string>;
      ws?.outputChannel.push({ type: "status_resumed", data: {} });
      const data = entry.data as { questions: UserQuestion[] };
      const result: PermissionResult = {
        behavior: "allow",
        updatedInput: {
          questions: data.questions,
          answers,
        },
      };
      entry.resolve(result);
      info(
        `Session exiting pending question state (sessionId: ${sessionId}, resolved: true)`,
      );
      return true;
    }

    // kind === "plan"
    const approved = Boolean(payload.approved);
    const keepContext = payload.keepContext as boolean | undefined;
    const feedback = payload.feedback as string | undefined;
    const planData = entry.data as { plan: string };

    let result: PermissionResult;
    if (approved && keepContext) {
      result = {
        behavior: "allow",
        updatedInput: { plan: planData.plan },
      };
    } else if (approved && !keepContext) {
      result = {
        behavior: "deny",
        message:
          "Plan approved. Interrupting to start fresh implementation session.",
      };
    } else {
      result = {
        behavior: "deny",
        message: feedback
          ? `User rejected the plan: ${feedback}`
          : "User rejected the plan. Please revise.",
      };
    }

    if (!(approved && !keepContext)) {
      ws?.outputChannel.push({ type: "status_resumed", data: {} });
    }

    entry.resolve(result);
    info(
      `Session exiting pending plan approval state (sessionId: ${sessionId}, approved: ${approved})`,
    );
    return true;
  }

  /**
   * Returns true when a pending interaction of the given kind is outstanding
   * for the session.
   */
  hasPending(sessionId: string, kind: PendingKind): boolean {
    return this.pending.get(sessionId)?.has(kind) ?? false;
  }

  /**
   * Returns the data captured when the pending interaction was registered
   * (the questions for `question`, the plan string for `plan`), or undefined
   * if no interaction of that kind is outstanding.
   */
  getPendingData<T>(sessionId: string, kind: PendingKind): T | undefined {
    return this.pending.get(sessionId)?.get(kind)?.data as T | undefined;
  }

  /**
   * Sets the mode for a session without sending a message. Updates the
   * permission mode and pushes it through to the live SDK query.
   */
  async setMode(sessionId: string, mode: "Plan" | "Build"): Promise<boolean> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      info(`setMode - session '${sessionId}' not found`);
      return false;
    }

    const newPermissionMode = mapMode(mode);

    if (ws.mode === mode && ws.permissionMode === newPermissionMode) {
      info(`setMode - no change (mode='${mode}', sessionId='${sessionId}')`);
      return true;
    }

    ws.mode = mode;
    ws.permissionMode = newPermissionMode;
    info(
      `setMode - mode updated to '${mode}', permissionMode='${newPermissionMode}' (sessionId='${sessionId}')`,
    );

    if (ws.query.setPermissionMode) {
      if (isSdkDebugEnabled()) {
        sdkDebug("tx", { op: "setPermissionMode", mode: newPermissionMode });
      }
      await ws.query.setPermissionMode(newPermissionMode);
      info(
        `setMode - setPermissionMode('${newPermissionMode}') applied (sessionId='${sessionId}')`,
      );
    }

    ws.lastActivityAt = new Date();
    return true;
  }

  /**
   * Sets the model for a session without sending a message. Applies to the
   * live SDK query when available so the next turn uses the new model.
   */
  async setModel(sessionId: string, model: string): Promise<boolean> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      info(`setModel - session '${sessionId}' not found`);
      return false;
    }

    if (ws.model === model) {
      info(`setModel - no change (model='${model}', sessionId='${sessionId}')`);
      return true;
    }

    ws.model = model;
    info(`setModel - model updated to '${model}' (sessionId='${sessionId}')`);

    if (ws.query.setModel) {
      if (isSdkDebugEnabled()) {
        sdkDebug("tx", { op: "setModel", model });
      }
      await ws.query.setModel(model);
      info(`setModel - setModel('${model}') applied (sessionId='${sessionId}')`);
    }

    ws.lastActivityAt = new Date();
    return true;
  }

  async send(
    sessionId: string,
    message: string,
    model?: string,
    mode?: string,
  ): Promise<WorkerSession> {
    info(
      `send() - sessionId='${sessionId}', messageLength=${message?.length}, model=${model || "default"}, mode=${mode || "unchanged"}`,
    );
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    if (model) {
      ws.model = model;
      info(`model updated to '${model}'`);
      if (ws.query.setModel) {
        if (isSdkDebugEnabled()) {
          sdkDebug("tx", { op: "setModel", model });
        }
        await ws.query.setModel(model);
      }
    }

    if (mode) {
      ws.permissionMode = mapMode(mode);
      ws.mode = ws.permissionMode === "plan" ? "Plan" : "Build";
      info(
        `mode updated to '${ws.mode}', permissionMode='${ws.permissionMode}'`,
      );
      if (ws.query.setPermissionMode) {
        if (isSdkDebugEnabled()) {
          sdkDebug("tx", {
            op: "setPermissionMode",
            mode: ws.permissionMode,
          });
        }
        await ws.query.setPermissionMode(ws.permissionMode);
        info(`setPermissionMode('${ws.permissionMode}') applied`);
      }
    }

    ws.lastActivityAt = new Date();

    const userMessage: SDKUserMessage = {
      type: "user",
      session_id: ws.conversationId ?? "",
      message: {
        role: "user",
        content: [{ type: "text", text: message }],
      },
      parent_tool_use_id: null,
    };

    // Push into the persistent input queue. This avoids q.streamInput(...)
    // which always calls transport.endInput() at the tail and would close
    // stdin to the CLI subprocess after the message is written.
    if (isSdkDebugEnabled()) {
      sdkDebug("tx", userMessage);
    }
    ws.inputQueue.push(userMessage);

    this.logStatusChange(ws, "streaming", "message_sent");

    return ws;
  }

  async *stream(sessionId: string): AsyncGenerator<OutputEvent> {
    const ws = this.sessions.get(sessionId);
    if (!ws) {
      throw new Error(`Session ${sessionId} not found`);
    }

    try {
      for await (const event of ws.outputChannel) {
        if (isControlEvent(event)) {
          info(`stream() - control event: type='${event.type}'`);
          if (event.type === "question_pending") {
            const questionCount =
              (event.data as { questions?: UserQuestion[] }).questions?.length ||
              0;
            debug(`Control event details: ${questionCount} questions pending`);
          } else if (event.type === "plan_pending") {
            debug(`Control event details: plan approval pending`);
          }
          ws.lastMessageType = event.type;
          ws.lastMessageSubtype = undefined;
          ws.lastActivityAt = new Date();
          yield event;
          continue;
        }

        const msg = event;
        if (msg.session_id) {
          ws.conversationId = msg.session_id;
        }

        ws.lastMessageType = msg.type as LastMessageType;
        ws.lastMessageSubtype = (msg as { subtype?: string }).subtype;
        ws.lastActivityAt = new Date();

        if (
          msg.type === "system" &&
          (msg as { subtype?: string }).subtype === "init"
        ) {
          const initMsg = msg as {
            permissionMode?: string;
            model?: string;
            tools?: string[];
          };
          const hasSkill = Array.isArray(initMsg.tools) && initMsg.tools.includes("Skill");
          info(
            `SDK init: permissionMode='${initMsg.permissionMode}', model='${initMsg.model}', toolCount=${initMsg.tools?.length ?? 0}, hasSkillTool=${hasSkill}`,
          );
        }
        if (msg.type === "result") {
          const r = msg as { subtype?: string; is_error?: boolean };
          info(`result: subtype='${r.subtype}', is_error=${r.is_error}`);
        }
        yield event;
      }
    } finally {
      // Don't overwrite error status — it indicates a genuine failure.
      if (ws.status !== "error") {
        this.logStatusChange(ws, "idle", "stream_complete");
      }
    }
  }

  async close(sessionId: string): Promise<void> {
    const ws = this.sessions.get(sessionId);
    if (!ws) return;

    const bySession = this.pending.get(sessionId);
    if (bySession) {
      for (const [kind, entry] of bySession) {
        const label = kind === "question" ? "question" : "plan approval";
        warn(`Session closed with pending ${label} (sessionId: ${sessionId})`);
        entry.reject(new Error("Session closed"));
      }
      this.pending.delete(sessionId);
    }

    // Close the input queue first so the SDK's internal consumer sees
    // { done: true } and finishes its streamInput loop before we tear down
    // the Query itself. Closing the query first can surface a spurious
    // write-error if a push is racing.
    ws.inputQueue.close();
    ws.outputChannel.complete();
    ws.query.close?.();
    this.logStatusChange(ws, "closed", "session_closed");
    this.sessions.delete(sessionId);
  }

  get(sessionId: string): WorkerSession | undefined {
    return this.sessions.get(sessionId);
  }

  list(): SessionInfo[] {
    return Array.from(this.sessions.values()).map((ws) => ({
      sessionId: ws.id,
      conversationId: ws.conversationId,
      mode: ws.mode,
      model: ws.model,
      permissionMode: ws.permissionMode,
      status: ws.status,
      createdAt: ws.createdAt.toISOString(),
      lastActivityAt: ws.lastActivityAt.toISOString(),
      lastMessageType: ws.lastMessageType,
      lastMessageSubtype: ws.lastMessageSubtype,
    }));
  }

  async closeAll(): Promise<void> {
    for (const [id] of this.sessions) {
      await this.close(id);
    }
  }

  /**
   * Clears context by closing an existing session and creating a new one.
   * Returns the new session. Message replay after the swap comes exclusively
   * from the server-side cache.
   */
  async clearContextAndCreate(
    currentSessionId: string,
    opts: {
      prompt: string;
      model: string;
      mode: string;
      systemPrompt?: string;
      workingDirectory?: string;
    },
  ): Promise<{ newSession: WorkerSession; oldSessionId: string }> {
    info(
      `clearContextAndCreate - closing session '${currentSessionId}' and creating new session`,
    );

    const existingSession = this.sessions.get(currentSessionId);
    if (existingSession) {
      await this.close(currentSessionId);
    } else {
      info(
        `clearContextAndCreate - no existing session found for '${currentSessionId}'`,
      );
    }

    const newSession = await this.create({
      prompt: opts.prompt,
      model: opts.model,
      mode: opts.mode,
      systemPrompt: opts.systemPrompt,
      workingDirectory: opts.workingDirectory,
    });

    info(
      `clearContextAndCreate - new session created: ${newSession.id} (previous: ${currentSessionId})`,
    );

    return {
      newSession,
      oldSessionId: currentSessionId,
    };
  }
}
