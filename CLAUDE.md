# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

Homespun is a web application for managing development features and AI agents. It provides:

- Project and feature management with hierarchical tree visualization
- Git clone integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration

The frontend is built with React + Vite + TypeScript + TailwindCSS, with an ASP.NET server backend.

## Development Practices

### Test Driven Development (TDD)

**TDD is mandatory for all planning and implementation.** When developing new features or fixing bugs:

1. **Write tests first** - Before implementing any code, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle for every change:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Plan with tests in mind** - When planning features, identify test cases as part of the design
4. **Test naming** - Use descriptive test names that explain the scenario and expected outcome (e.g., `it('returns error when project not found')`)
5. **Test coverage** - Aim for comprehensive coverage of business logic and component behavior

### Pre-PR Checklist

**Before creating a pull request, always run the following checks:**

```bash
dotnet test

cd src/Homespun.Web

# Run linting (must pass with no errors)
npm run lint:fix

# Format code
npm run format:check

# Generate API client from OpenAPI spec
npm run generate:api:fetch

# Run type checking (must pass with no errors)
npm run typecheck

# Run tests
npm test

# Run e2e tests
npm test:e2e

# Build Storybook (catches story drift at author time)
npm run build-storybook
```

These checks are run in CI and will cause the PR to fail if not passing. Always verify locally first.

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture where code is organized by feature rather than technical layer. Features are organized at the top level, each containing all related components, hooks, stores, and API calls.

```
src/
├── Homespun.Server/             # ASP.NET backend (API, SignalR hubs, services)
│   ├── Features/                # Feature slices (all business logic)
│   │   ├── AgentOrchestration/  # Agent lifecycle management
│   │   ├── ClaudeCode/          # Claude Code SDK session management
│   │   ├── Commands/            # Shell command execution
│   │   ├── Fleece/              # Fleece issue tracking integration
│   │   ├── Git/                 # Git clone operations
│   │   ├── GitHub/              # GitHub API integration (Octokit)
│   │   ├── Notifications/       # Toast notifications via SignalR
│   │   ├── Projects/            # Project management
│   │   ├── PullRequests/        # PR workflow and data entities
│   │   └── SignalR/             # SignalR hub implementations
│   └── Program.cs               # Application entry point
├── Homespun.Web/                # React frontend
│   ├── src/
│   │   ├── features/            # Feature slices (top-level organization)
│   │   │   ├── projects/        # Project management
│   │   │   │   ├── components/  # Feature-specific components
│   │   │   │   ├── hooks/       # Feature-specific hooks
│   │   │   │   └── index.ts     # Public exports
│   │   │   ├── issues/          # Issue tracking
│   │   │   ├── agents/          # Agent sessions and chat
│   │   │   └── branches/        # Branch and PR management
│   │   ├── components/          # Shared components
│   │   │   └── ui/              # shadcn/ui components
│   │   ├── api/                 # OpenAPI generated client
│   │   ├── hooks/               # Shared hooks
│   │   ├── lib/                 # Utility functions
│   │   ├── stores/              # Zustand stores
│   │   ├── routes/              # TanStack Router routes
│   │   └── test/                # Test setup and utilities
│   └── package.json
├── Homespun.Shared/             # Shared library (DTOs, contracts, hub interfaces)
├── Homespun.ClaudeAgentSdk/     # Claude Code SDK C# wrapper
└── Homespun.Worker/             # TypeScript agent worker (Hono + Claude Agent SDK)

tests/
├── Homespun.Tests/              # Backend unit tests (NUnit + Moq)
├── Homespun.Api.Tests/          # API integration tests (WebApplicationFactory)
```

### Feature Slices

- **Fleece**: Integration with Fleece issue tracking — event-sourced storage in `.fleece/`. The library writes a projected snapshot at `.fleece/issues.jsonl` (lean records, no per-field `*LastUpdate`/`*ModifiedBy` metadata), per-session append-only event logs at `.fleece/changes/change_{guid}.jsonl`, and a `.fleece/tombstones.jsonl` for hard-deletes. Reads load the snapshot and replay all change files (DAG ordered by per-file `meta.follows` pointer). Writes append events to the active change file. `.fleece/.active-change` and `.fleece/.replay-cache` are per-clone local state and gitignored. The pre-commit hook installed by `fleece install` auto-stages `.fleece/changes/` on every commit and additionally `.fleece/issues.jsonl` + `.fleece/tombstones.jsonl` on the default branch. The `fleece project` command compacts events into the snapshot — runs on the default branch only, either via the server's sync flow (after a successful merge) or out-of-band.
  - **Version Sync Required**: When updating the `Fleece.Core` NuGet package version in `Homespun.Server.csproj` and `Homespun.Shared.csproj`, you must also update the `Fleece.Cli` version in `Dockerfile.base` to match. Current version: **3.1.0**.
  - **Client-side layout**: Graph layout runs in the browser, not on the server. `GET /api/projects/{projectId}/issues` returns the visible-set (`open ∪ ancestors-of-open ∪ explicitInclude ∪ openPrLinkedIds when flagged`) — a 30-line BFS in `IssueAncestorTraversalService`. Decorations come from sibling endpoints fetched in parallel: `/linked-prs`, `/agent-statuses`, `/openspec-states?issues=…`, `/orphan-changes`, `/pull-requests/merged`. The TS port of `Fleece.Core`'s `IIssueLayoutService` lives under `src/Homespun.Web/src/features/issues/services/layout/` and is validated against the C# reference via golden fixtures (`tests/Homespun.Web.LayoutFixtures/`); regenerate with `UPDATE_FIXTURES=1` when bumping `Fleece.Core`. SignalR broadcasts a single unified `IssueChanged` event (`BroadcastIssueChanged(projectId, kind, issueId?, IssueResponse?)`); the client merge is idempotent (`applyIssueChanged`) so the local POST response and the SignalR echo can both apply without coordination. See [`docs/graph-layout-client-side.md`](docs/graph-layout-client-side.md) for the full architecture.
- **ClaudeCode**: Claude Code SDK session management — supports Plan (read-only) and Build (full access) modes. The worker is the only component that speaks the native SDK format; the server ingests A2A events from the worker via `ISessionEventIngestor`, appends each event to the per-session JSONL log through `A2AEventStore`, translates once via `A2AToAGUITranslator`, and broadcasts a single `SessionEventEnvelope` per AG-UI event over SignalR's `ReceiveSessionEvent`. Refresh replays the same envelopes through `GET /api/sessions/{id}/events?since=N&mode=incremental|full` so live and refresh produce identical streams. The default replay mode is configurable via `SessionEvents:ReplayMode`; `Full` is the kill switch if incremental replay ever produces a gap. **Model catalogue:** `GET /api/models` is the single source of truth for available Claude models — backed by `IModelCatalogService` (live path fetches the Anthropic Models API with a 24h in-process cache + `FallbackModels` on SDK failure). Mock-mode catalog wiring is gated on `MockMode:UseLiveClaudeSessions`: `dev-mock` (false) short-circuits to the fallback list via `MockModelCatalogService`; `dev-live` / `dev-windows` / `dev-container` (true) use the live `ModelCatalogService` so the endpoint returns the real Anthropic catalogue. `FallbackModels` contains the three short tier aliases (`opus`, `sonnet`, `haiku`) — the Claude Agent SDK accepts these directly. Agent-creating controllers pass `request.Model ?? project.DefaultModel` through `ResolveModelIdAsync` so short tier aliases ("opus"/"sonnet"/"haiku") and legacy stored ids normalize to live-catalogue ids before a session starts. The frontend consumes the endpoint via `useAvailableModels` (TanStack Query, `staleTime: 24h`).
- **Commands**: Shell command execution abstraction
- **Git**: Git clone creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities

### Dev prerequisites

- .NET 10 SDK with the Aspire workload
- Node 20+ (npm on PATH — required by the AppHost's Vite wiring)
- Docker Desktop (for Seq + Docker-mode agent execution)
- One-time secret bootstrap: run `scripts/set-user-secrets.sh` (macOS/Linux) or
  `scripts/set-user-secrets.ps1` (Windows) after copying `.env.example` → `.env`.
  The script migrates `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` into the
  `Homespun.AppHost` user-secrets store so Aspire can inject them at runtime.

### Running the Application

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.**

Local dev runs through the Aspire AppHost. Pick a launch profile:

| Profile | Use case |
|---|---|
| `dev-mock` | Fastest inner loop. Server (`AddProject`) + Vite + Seq with the mock service graph. No worker, agents return canned A2A events. |
| `dev-live` | Same stack plus real Claude SDK sessions. Docker-mode agent execution spawns a sibling worker container per session via DooD. |
| `dev-windows` | Same stack plus a single pre-running worker container. Agent execution routes every session to that worker (SingleContainer mode) — also usable on Apple Silicon now that the worker image is built locally instead of pulled from GHCR. |
| `dev-container` | Prod-parity check: server/web/worker built from their Dockerfiles. Not a daily driver — inner loop is rebuild-per-change. |
| `prod` | Container topology against `~/.homespun-container/data` with `ASPNETCORE_ENVIRONMENT=Production` and no mock seeding. Seq still starts. Local debugging tool for prod-shaped runs; not a deployment gate (Komodo + `docker-compose.yml` remains the deploy path). |

```bash
dotnet run --project src/Homespun.AppHost --launch-profile dev-mock
dotnet run --project src/Homespun.AppHost --launch-profile dev-live
dotnet run --project src/Homespun.AppHost --launch-profile dev-windows
dotnet run --project src/Homespun.AppHost --launch-profile dev-container
dotnet run --project src/Homespun.AppHost --launch-profile prod
```

Use `dotnet run` for these — `aspire run` does not accept `--launch-profile`
and silently falls back to the first profile in `launchSettings.json`.

In every dev profile that needs a worker (`dev-live`, `dev-windows`,
`dev-container`), the AppHost builds the image from
`src/Homespun.Worker/Dockerfile` via `AddDockerfile` and tags it `worker:dev`.
First boot takes an extra 30–60s for the Docker build; subsequent boots hit
the layer cache. No dev profile pulls from GHCR — GHCR is reserved for the
prod deploy path (`docker-compose.yml` + Komodo).

Server is reachable at `http://localhost:5101` (port pinned for Playwright + existing tests).
Seq's UI + OTLP ingest both land on `5341` in every dev profile
(`http://localhost:5341`). The Aspire dashboard prints its own URL at startup.

### Accessing Application Logs

Every tier exports OTLP to two sinks in parallel via
`Homespun.ServiceDefaults`:

1. **Aspire dashboard** — per-signal `AddOtlpExporter` calls driven by
   `OTEL_EXPORTER_OTLP_ENDPOINT`. `aspire otel logs server` surfaces entries
   for the current session. Server `Program.cs` calls `ClearProviders()`
   *before* `AddServiceDefaults()` so the OTLP logger provider survives.
2. **Seq** — `AddSeqEndpoint("seq")` reads the `seq` connection string the
   AppHost injects via `WithReference(seq)`. Logs + traces land here; metrics
   stay on the Aspire leg only (Seq does not accept metrics).

Seq UI is at `http://localhost:5341` in dev. In prod the `datalust/seq:2024.3`
service in `docker-compose.yml` ingests via `http://seq:5341/ingest/otlp`;
set `SEQ_API_KEY` in the Komodo env to gate ingestion.

**Container-mode profiles (`dev-container`, `prod`)** can't reach the Aspire
dashboard's default gRPC OTLP endpoint — it serves the ASP.NET Core dev HTTPS
cert which containers don't trust. The AppHost works around this by injecting
the dashboard's HTTP/protobuf OTLP URL (`DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`,
rewritten `localhost` → `host.docker.internal`) onto every container resource,
and by setting `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` +
`ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` so the dashboard accepts unauthenticated
HTTP OTLP locally. Host-mode profiles (`dev-live`, `dev-windows`, `dev-mock`)
are unaffected — Aspire's default HTTPS-gRPC injection still works for them.

**Full-body debug logging.** The `HOMESPUN_DEBUG_FULL_MESSAGES` umbrella
flag opts the entire stack (worker + server + web) into emitting full A2A,
AG-UI, and envelope bodies as OTel log events alongside the `[SDK rx]` /
`[SDK tx]` SDK-boundary logs. Default: ON in every launch profile — opt
out explicitly with `HOMESPUN_DEBUG_FULL_MESSAGES=false` on the AppHost
process env. When on, the AppHost fans the flag out and implies
`DEBUG_AGENT_SDK=true` + `CONTENT_PREVIEW_CHARS=-1` on the worker,
`SessionEventContent__ContentPreviewChars=-1` on the server, and
`VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true` on the web build (tree-shaken
when unset). Replay-path log entries carry `homespun.replay=true` so they
can be filtered out in Seq via `homespun.replay is null`. The umbrella
flag is read at process start; toggling requires a restart.

**Verify connectivity:**
```bash
curl -s 'http://localhost:5341/api/events/signal?count=1'
```

For detailed log analysis, use the `/logs` skill.

**Span / tracer reference:** every span emitted by any tier is catalogued in
[`docs/traces/dictionary.md`](docs/traces/dictionary.md). Add / rename /
remove a span → update that file in the same PR. A drift check in each
tier's test suite refuses to merge otherwise; see
[`docs/traces/README.md`](docs/traces/README.md) for the workflow.

**OTLP proxy:** the worker and web client never hit Seq or the Aspire
dashboard directly. Both export through `POST /api/otlp/v1/logs` and
`POST /api/otlp/v1/traces` on the server, which parses the protobuf body,
runs `OtlpScrubber` (content-preview gating + secret-substring redaction),
and fans out to Seq + the Aspire dashboard in parallel via `OtlpFanout`.
Upstream sink failures are logged at Warning and swallowed — the proxy
always returns 202 so OTLP client SDKs never retry-amplify sink outages.
Full contract in [`docs/observability/otlp-proxy.md`](docs/observability/otlp-proxy.md).

### Worker OTLP path

The worker (`src/Homespun.Worker`) exports traces + logs via
`@opentelemetry/sdk-node` from `src/instrumentation.ts`, which is the very
first import of `src/index.ts` so `@opentelemetry/auto-instrumentations-node`
patches Hono/http/undici before any other module binds them. The SDK
targets `${OTLP_PROXY_URL}/traces` and `${OTLP_PROXY_URL}/logs`; the server's
`DockerAgentExecutionService` injects `OTLP_PROXY_URL` (plus
`OTEL_SERVICE_NAME=homespun.worker`, `HOMESPUN_SESSION_ID`,
`HOMESPUN_ISSUE_ID`, `HOMESPUN_PROJECT_NAME`) into every spawned container.
`docker stop --time 3` (not `docker kill`) is used on shutdown so the
worker receives SIGTERM and flushes the OTel batch processors.

**Do NOT enable** the following auto-instrumentations on the worker (all
disabled in `instrumentation.ts` via
`getNodeAutoInstrumentations({...})`):

- `@opentelemetry/instrumentation-fs` — every Claude Agent SDK tool call,
  OpenSpec snapshot, and Fleece read generates hundreds of `fs.readFile`
  spans per second; it drowns Seq and destroys the signal-to-noise ratio.
  Relevant I/O surfaces as explicit spans on service methods instead.
- `@opentelemetry/instrumentation-net`, `@opentelemetry/instrumentation-dns`
  — low-level noise with no correlation value.

### Client OTel propagation

The web client (`src/Homespun.Web`) bootstraps `WebTracerProvider` +
`LoggerProvider` in `src/instrumentation.ts`, loaded as the first import of
`main.tsx`. Fetch and XHR requests to `/api/*` are auto-instrumented, which
injects a W3C `traceparent` header so server spans parent the browser
span automatically. `/api/otlp/v1/*` is ignored to avoid telemetry feedback
loops.

**SignalR is special** because WebSocket transports do not expose per-frame
headers. Propagation uses two symmetric mechanisms:

- **Client → server**: every hub method invocation is wrapped in
  `traceInvoke` (`src/lib/signalr/trace.ts`). The helper starts a client
  span and prepends a traceparent string as arg 0 on the wire. The server's
  `TraceparentHubFilter` (`Homespun.Features.Observability`) pulls arg 0
  off every invocation, parses it as `ActivityContext`, and starts a
  server-kind activity on the `Homespun.Signalr` `ActivitySource` parented
  to the client context. Arg 1 (when a string) is tagged as
  `homespun.session.id`.
- **Server → client**: `SessionEventEnvelope.Traceparent` carries the
  current server activity's W3C traceparent on every
  `ReceiveSessionEvent` broadcast. The client's handler wraps the callback
  in `withExtractedContext(envelope, …)` so downstream work runs under the
  server's parent context.

The native `Microsoft.AspNetCore.SignalR.Server` `ActivitySource` is
**not** registered on the tracer provider — the filter owns the span so
there is no double emission.

Context manager choice: the web SDK uses `StackContextManager` (the
default), not `ZoneContextManager`. Zone.js is not a runtime dependency.
If you add async chains that need context propagation (promise
continuations crossing I/O boundaries), wrap the boundary in
`context.with(context.active(), fn)` at the call site — the default
manager does not follow awaits across event-loop turns on its own.

## React Frontend Development

### Technology Stack

The React frontend uses:

- **React 19** with TypeScript
- **Vite** for build tooling
- **TailwindCSS v4** for styling
- **TanStack Router** for file-based routing
- **TanStack Query** for server state management
- **Zustand** for client state management
- **shadcn/ui** with Base UI for components (New York style, Zinc base color)
- **prompt-kit** for AI chat interface components

### OpenAPI Client

The API client is auto-generated from the server's OpenAPI spec. The generated code is in `src/api/generated/`.

Regenerate after server API changes:

```bash
npm run generate:api:fetch
```

Use the typed API client:

```typescript
import { getProjects, createProject } from '@/api'
```

## Testing

### Backend Tests (.NET)

```bash
# Run all backend tests
dotnet test

# Run specific test project
dotnet test tests/Homespun.Tests
dotnet test tests/Homespun.Api.Tests

# Run with verbose output
dotnet test --verbosity normal
```

### Frontend Tests (Vitest)

The React frontend uses **Vitest** with **React Testing Library** for testing.

```bash
cd src/Homespun.Web

# Run tests once
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm run test:coverage
```

#### Test Structure

Tests are co-located with source files using the `.test.ts` or `.test.tsx` extension:

```
src/
├── components/ui/
│   ├── button.tsx
│   └── button.test.tsx
├── lib/
│   ├── utils.ts
│   └── utils.test.ts
└── features/projects/
    ├── ProjectCard.tsx
    └── ProjectCard.test.tsx
```

#### Writing Tests

Refer to existing tests and use a similar style.

## Testing Infrastructure

The project uses a comprehensive three-tier testing strategy:

### Unit Tests (Homespun.Tests)

**Framework:** NUnit + Moq

Unit tests cover service logic in isolation using Moq for dependency mocking.

### API Integration Tests (Homespun.Api.Tests)

**Framework:** NUnit + WebApplicationFactory

API tests verify HTTP endpoints using an in-memory test server with `HomespunWebApplicationFactory`.

### React Frontend E2E Tests (Homespun.Web/e2e)

**Framework:** Playwright Test

E2E tests for the React frontend are located in `src/Homespun.Web/e2e/`. These tests mirror the C# E2E tests but use the JavaScript Playwright API.

**E2E Test Configuration:**

- Tests automatically start the mock server via `webServer` config

### Inspection with Playwright MCP and Mock Mode

To start a server running in Mock Mode to investigate with the Playwright MCP:

```bash
dotnet run --project src/Homespun.AppHost --launch-profile dev-mock
```

Server, Vite, and Seq are started by the AppHost. Server logs, traces, and
metrics stream to the Aspire dashboard via OTLP (URL printed at startup).
Logs + traces are also visible in the Seq UI at `http://localhost:5341`.
The server is reachable at `http://localhost:5101` (pinned port), and the
Vite dev server at `http://localhost:5173`. Use the Playwright MCP tools
against those URLs.

### Critical Shell Management Rules

**NEVER use `KillShell` on a shell running `dotnet run --project src/Homespun.AppHost`
or any long-lived `dotnet` process.** Killing these shells can terminate your
entire session and crash the agent.

When cleaning up after UI testing:

- Close the browser using `mcp__playwright__browser_close` - this is safe
- Leave the AppHost process running - it will be cleaned up when the session ends
- Do NOT attempt to kill background shells that are running server processes

If you need to restart the AppHost:

1. Use `pkill -f "Homespun.AppHost"` to stop the dotnet process directly
2. Start a new AppHost run with
   `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock &`
