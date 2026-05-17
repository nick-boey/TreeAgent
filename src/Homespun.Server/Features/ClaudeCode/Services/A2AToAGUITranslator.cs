using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Logging;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Pure A2A → AG-UI translator.
///
/// <para>
/// See <c>docs/session-events.md</c> and <c>openspec/changes/a2a-native-messaging/design.md</c>
/// for the event mapping table.
/// </para>
///
/// <para>
/// The translator never throws on input; unknown variants fall back to an AG-UI
/// <see cref="CustomEvent"/> with <c>name = "raw"</c> and the original A2A payload captured
/// under <c>original</c> so clients can render or log it without loss.
/// </para>
/// </summary>
public sealed class A2AToAGUITranslator : IA2AToAGUITranslator
{
    private readonly IPendingToolCallRegistry _pendingToolCalls;
    private readonly ILogger<A2AToAGUITranslator>? _logger;
    private readonly IOptionsMonitor<SessionDebugLoggingOptions>? _debugOptions;

    public A2AToAGUITranslator(
        IPendingToolCallRegistry pendingToolCalls,
        ILogger<A2AToAGUITranslator>? logger = null,
        IOptionsMonitor<SessionDebugLoggingOptions>? debugOptions = null)
    {
        _pendingToolCalls = pendingToolCalls;
        _logger = logger;
        _debugOptions = debugOptions;
    }

    /// <inheritdoc />
    public IEnumerable<AGUIBaseEvent> Translate(ParsedA2AEvent a2a, TranslationContext ctx)
    {
        var events = a2a switch
        {
            ParsedAgentTask parsed => TranslateTask(parsed.Task, ctx),
            ParsedAgentMessage parsed => TranslateMessage(parsed.Message, ctx),
            ParsedTaskStatusUpdateEvent parsed => TranslateStatusUpdate(parsed.StatusUpdate, ctx),
            ParsedTaskArtifactUpdateEvent parsed => TranslateArtifactUpdate(parsed.ArtifactUpdate),
            _ => YieldRaw(a2a),
        };

        if (_debugOptions?.CurrentValue.FullMessages != true || _logger is null)
        {
            return events;
        }

        return LogAndYield(events, ctx);
    }

    private IEnumerable<AGUIBaseEvent> LogAndYield(IEnumerable<AGUIBaseEvent> events, TranslationContext ctx)
    {
        foreach (var evt in events)
        {
            _logger!.AGUITranslate(evt.Type, ctx.SessionId, JsonSerializer.Serialize(evt));
            yield return evt;
        }
    }

    // ---------------- Task ----------------

    private static IEnumerable<AGUIBaseEvent> TranslateTask(AgentTask task, TranslationContext ctx)
    {
        // A newly-submitted Task marks the start of a run.
        if (task.Status.State is TaskState.Submitted or TaskState.Working)
        {
            yield return AGUIEventFactory.CreateRunStarted(ctx.SessionId, ctx.RunId);
        }
        else
        {
            // Unusual: Task arrived in a non-start state. Preserve as raw.
            yield return RawCustom(new { task.Id, task.ContextId, state = task.Status.State.ToString() });
        }
    }

    // ---------------- Message ----------------

    private static IEnumerable<AGUIBaseEvent> TranslateMessage(AgentMessage message, TranslationContext ctx)
    {
        var sdkMessageType = message.Metadata.GetMetadataString("sdkMessageType");
        // Fallback messageId derives from the stored A2A event id so live and replay
        // produce identical AG-UI envelopes when the worker omits messageId.
        var messageId = message.MessageId ?? FallbackMessageId(ctx);

        // System messages carry subtype in the data part (init, hook_started, hook_response, ...).
        if (sdkMessageType == "system")
        {
            foreach (var evt in TranslateSystemMessage(message, messageId))
            {
                yield return evt;
            }
            yield break;
        }

        // User-role messages in the assistant flow are typically tool_result carriers;
        // standalone user text is echoed as a Custom user.message so multi-tab clients see it.
        var isAgent = message.Role == MessageRole.Agent || sdkMessageType == "assistant";
        var isUser = message.Role == MessageRole.User || sdkMessageType == "user";

        if (isAgent)
        {
            foreach (var evt in TranslateAgentMessageParts(message, messageId, ctx))
            {
                yield return evt;
            }
        }
        else if (isUser)
        {
            foreach (var evt in TranslateUserMessageParts(message, messageId))
            {
                yield return evt;
            }
        }
        else
        {
            yield return RawCustom(new
            {
                messageId,
                role = message.Role.ToString(),
                sdkMessageType,
            });
        }
    }

    private static IEnumerable<AGUIBaseEvent> TranslateAgentMessageParts(AgentMessage message, string messageId, TranslationContext ctx)
    {
        var parts = message.Parts ?? [];
        var textBlockIndex = 0;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            switch (part)
            {
                case TextPart textPart:
                    {
                        var blockId = $"{messageId}-text-{textBlockIndex++}";
                        yield return AGUIEventFactory.CreateTextMessageStart(blockId, "assistant");
                        yield return AGUIEventFactory.CreateTextMessageContent(blockId, textPart.Text ?? string.Empty);
                        yield return AGUIEventFactory.CreateTextMessageEnd(blockId);
                        break;
                    }

                case DataPart dataPart:
                    {
                        var kind = dataPart.Metadata.GetMetadataString("kind");
                        switch (kind)
                        {
                            case "thinking":
                                yield return AGUIEventFactory.CreateCustomEvent(
                                    AGUICustomEventName.Thinking,
                                    new { text = dataPart.GetDataString("thinking") ?? string.Empty, parentMessageId = messageId });
                                break;

                            case "tool_use":
                                {
                                    // Fallback toolCallId derives from event id + part index so a
                                    // worker emission missing toolUseId still translates to the
                                    // same id on live and replay.
                                    var toolCallId = dataPart.GetDataString("toolUseId") ?? FallbackToolCallId(ctx, i);
                                    var toolName = dataPart.GetDataString("toolName") ?? "unknown";
                                    yield return AGUIEventFactory.CreateToolCallStart(toolCallId, toolName, messageId);

                                    var input = dataPart.GetDataElement("input");
                                    if (input.HasValue)
                                    {
                                        yield return AGUIEventFactory.CreateToolCallArgs(toolCallId, input.Value.GetRawText());
                                    }

                                    yield return AGUIEventFactory.CreateToolCallEnd(toolCallId);
                                    break;
                                }

                            default:
                                yield return RawCustom(new { dataKind = kind, raw = dataPart.ToJsonElement() });
                                break;
                        }
                        break;
                    }
            }
        }
    }

    private static IEnumerable<AGUIBaseEvent> TranslateUserMessageParts(AgentMessage message, string messageId)
    {
        var parts = message.Parts ?? [];
        var hasToolResult = false;

        foreach (var part in parts)
        {
            if (part is DataPart dataPart)
            {
                var kind = dataPart.Metadata.GetMetadataString("kind");
                if (kind == "tool_result")
                {
                    hasToolResult = true;
                    var toolUseId = dataPart.GetDataString("toolUseId") ?? string.Empty;
                    var content = dataPart.GetDataElement("content");
                    var contentStr = content.HasValue ? content.Value.GetRawText() : string.Empty;
                    yield return AGUIEventFactory.CreateToolCallResult(toolUseId, contentStr, messageId);
                }
            }
        }

        // If the user-role message had no tool_result blocks, treat it as a user-text echo for
        // multi-tab sync. Concatenate any TextPart content.
        if (!hasToolResult)
        {
            var text = string.Join(string.Empty, parts.OfType<TextPart>().Select(p => p.Text ?? string.Empty));
            yield return AGUIEventFactory.CreateCustomEvent(
                AGUICustomEventName.UserMessage,
                new { text });
        }
    }

    private static IEnumerable<AGUIBaseEvent> TranslateSystemMessage(AgentMessage message, string messageId)
    {
        // Data payload with kind=system is emitted by the worker for system init.
        foreach (var part in message.Parts ?? [])
        {
            if (part is not DataPart dataPart) continue;

            var subtype = dataPart.GetDataString("subtype");
            switch (subtype)
            {
                case "init":
                    yield return AGUIEventFactory.CreateCustomEvent(
                        AGUICustomEventName.SystemInit,
                        new
                        {
                            model = dataPart.GetDataString("model"),
                            tools = dataPart.HasDataProperty("tools") ? dataPart.GetDataElement("tools") : null,
                            permissionMode = dataPart.GetDataString("permissionMode"),
                        });
                    break;

                case "hook_started":
                    yield return AGUIEventFactory.CreateCustomEvent(
                        AGUICustomEventName.HookStarted,
                        new
                        {
                            hookId = dataPart.GetDataString("hookId") ?? messageId,
                            hookName = dataPart.GetDataString("hookName") ?? "unknown",
                            hookEvent = dataPart.GetDataString("hookEvent") ?? "unknown",
                        });
                    break;

                case "hook_response":
                    yield return AGUIEventFactory.CreateCustomEvent(
                        AGUICustomEventName.HookResponse,
                        new
                        {
                            hookId = dataPart.GetDataString("hookId") ?? messageId,
                            hookName = dataPart.GetDataString("hookName") ?? "unknown",
                            output = dataPart.GetDataString("output"),
                            exitCode = dataPart.HasDataProperty("exitCode") ? dataPart.GetDataInt("exitCode") : (int?)null,
                            outcome = dataPart.GetDataString("outcome") ?? "unknown",
                        });
                    break;

                default:
                    yield return RawCustom(new
                    {
                        messageId,
                        subtype,
                        data = dataPart.ToJsonElement(),
                    });
                    break;
            }
        }
    }

    // ---------------- StatusUpdate ----------------

    private IEnumerable<AGUIBaseEvent> TranslateStatusUpdate(
        TaskStatusUpdateEvent statusUpdate, TranslationContext ctx)
    {
        // status.message may carry a control-event hint (workflow_complete, ...) embedded via
        // its metadata.sdkMessageType — check those before the state switch so we can
        // distinguish a "workflow_complete" completed-status from an ordinary run completion.
        //
        // status.resumed is deliberately NOT translated: pending tool-call state is modelled as
        // conversation content (TOOL_CALL_START/ARGS/END without a RESULT) rather than ghost
        // state on the session, so there is nothing for "resumed" to clear. Worker-emitted
        // status_resumed events are dropped by the translator and never reach the client.
        var statusMsgSdkType = statusUpdate.Status.Message?.Metadata.GetMetadataString("sdkMessageType");
        var controlType = statusUpdate.Metadata.GetMetadataString("controlType");

        if (statusMsgSdkType == "workflow_complete")
        {
            yield return BuildWorkflowComplete(statusUpdate);
            yield break;
        }

        if (controlType == "status_resumed" || statusMsgSdkType == "status_resumed")
        {
            // Retired signal — see above. No emission.
            yield break;
        }

        switch (statusUpdate.Status.State)
        {
            case TaskState.Working or TaskState.Submitted:
                // Working/submitted states are implied by the preceding RunStarted — suppress.
                yield break;

            case TaskState.InputRequired:
                foreach (var evt in BuildInputRequired(statusUpdate, ctx))
                {
                    yield return evt;
                }
                yield break;

            case TaskState.Completed:
                {
                    object? result = null;
                    if (statusUpdate.Status.Message is { } msg)
                    {
                        result = ExtractResultPayload(msg);
                    }
                    yield return AGUIEventFactory.CreateRunFinished(ctx.SessionId, ctx.RunId, result);
                    yield break;
                }

            case TaskState.Failed:
                {
                    var errorMessage = "Task failed";
                    string? code = null;
                    if (statusUpdate.Status.Message is { } msg)
                    {
                        errorMessage = ExtractFirstText(msg) ?? errorMessage;
                        foreach (var part in msg.Parts ?? [])
                        {
                            if (part is DataPart dp && dp.Metadata.GetMetadataString("kind") == "error")
                            {
                                code = dp.GetDataString("code");
                                break;
                            }
                        }
                    }
                    yield return AGUIEventFactory.CreateRunError(errorMessage, code);
                    yield break;
                }

            case TaskState.Canceled:
                yield return AGUIEventFactory.CreateRunError("Task was canceled", "canceled");
                yield break;

            default:
                yield return RawCustom(new
                {
                    state = statusUpdate.Status.State.ToString(),
                    statusUpdate.TaskId,
                    statusUpdate.ContextId,
                });
                yield break;
        }
    }

    private static AGUIBaseEvent BuildWorkflowComplete(TaskStatusUpdateEvent statusUpdate)
    {
        string? status = null;
        object? outputs = null;
        object? artifacts = null;

        foreach (var part in statusUpdate.Status.Message?.Parts ?? [])
        {
            if (part is DataPart dp && dp.Metadata.GetMetadataString("kind") == "workflow_complete")
            {
                status = dp.GetDataString("status");
                if (dp.HasDataProperty("outputs")) outputs = dp.GetDataElement("outputs");
                if (dp.HasDataProperty("artifacts")) artifacts = dp.GetDataElement("artifacts");
            }
        }

        return AGUIEventFactory.CreateCustomEvent(
            AGUICustomEventName.WorkflowComplete,
            new
            {
                status = status ?? "unknown",
                outputs,
                artifacts,
            });
    }

    /// <summary>
    /// Canonical tool names for the two interactive tool calls. Stable on the wire and in the
    /// event log — don't rename without a wire-protocol migration.
    /// </summary>
    public const string AskUserQuestionToolName = "ask_user_question";
    public const string ProposePlanToolName = "propose_plan";

    /// <summary>
    /// Translates an A2A <c>StatusUpdate{state: InputRequired}</c> into a
    /// <c>TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_END</c> sequence with a canonical tool
    /// name (<see cref="AskUserQuestionToolName"/> or <see cref="ProposePlanToolName"/>) and
    /// the original payload carried as the tool-call args JSON. The generated
    /// <c>toolCallId</c> is registered with <see cref="IPendingToolCallRegistry"/> so the
    /// hub's answer / approval handler can later append a matching
    /// <c>TOOL_CALL_RESULT</c>.
    /// </summary>
    private IEnumerable<AGUIBaseEvent> BuildInputRequired(
        TaskStatusUpdateEvent statusUpdate, TranslationContext ctx)
    {
        var inputType = statusUpdate.Metadata.GetMetadataString("inputType");

        string toolName;
        object argsObject;

        if (inputType == A2AInputType.PlanApproval)
        {
            toolName = ProposePlanToolName;
            var planContent = ExtractPlanContent(statusUpdate);
            var planFilePath = statusUpdate.Metadata.GetMetadataString("planFilePath");
            // planFilePath is preserved for server-side fallback (ToolInteractionService re-reads
            // it if the worker doesn't carry fresh plan content on resume) and so the Tool UI
            // plan component can surface the source filename to the user.
            argsObject = new
            {
                planContent,
                planFilePath,
            };
        }
        else
        {
            // Default to question (inputType == "question" or absent).
            toolName = AskUserQuestionToolName;
            var question = ExtractPendingQuestion(statusUpdate, ctx);
            if (question is null)
            {
                yield return RawCustom(new
                {
                    kind = "input-required-without-payload",
                    inputType,
                });
                yield break;
            }
            argsObject = question;
        }

        // Deterministic toolCallId derived from the stored event id so a live
        // input-required broadcast and a replay of the same record register and emit
        // the same id — preserving the live==replay invariant.
        var toolCallId = FallbackInputRequiredToolCallId(ctx);
        _pendingToolCalls.Register(ctx.SessionId, toolCallId);

        var argsJson = JsonSerializer.Serialize(argsObject, JsonOpts);

        yield return AGUIEventFactory.CreateToolCallStart(toolCallId, toolName);
        yield return AGUIEventFactory.CreateToolCallArgs(toolCallId, argsJson);
        yield return AGUIEventFactory.CreateToolCallEnd(toolCallId);
    }

    // ---------------- ArtifactUpdate ----------------

    private static IEnumerable<AGUIBaseEvent> TranslateArtifactUpdate(TaskArtifactUpdateEvent artifactUpdate)
    {
        yield return RawCustom(new
        {
            kind = "artifact-update",
            artifactUpdate.TaskId,
            artifactUpdate.ContextId,
            artifact = artifactUpdate.Artifact,
        });
    }

    // ---------------- Helpers ----------------

    private static AGUIBaseEvent RawCustom(object original)
        => AGUIEventFactory.CreateCustomEvent(AGUICustomEventName.Raw, new { original });

    private static IEnumerable<AGUIBaseEvent> YieldRaw(ParsedA2AEvent a2a)
    {
        yield return RawCustom(new { type = a2a.GetType().Name });
    }

    private static string? ExtractFirstText(AgentMessage message)
    {
        foreach (var part in message.Parts ?? [])
        {
            if (part is TextPart textPart && !string.IsNullOrEmpty(textPart.Text))
            {
                return textPart.Text;
            }
        }
        return null;
    }

    private static object? ExtractResultPayload(AgentMessage message)
    {
        // Collect any text + metadata data part into a small object for RunFinished.result.
        string? text = null;
        JsonElement? metadata = null;
        foreach (var part in message.Parts ?? [])
        {
            if (part is TextPart tp && text is null)
            {
                text = tp.Text;
            }
            else if (part is DataPart dp && dp.Metadata.GetMetadataString("kind") == "result_metadata")
            {
                metadata = dp.ToJsonElement();
            }
        }

        if (text is null && metadata is null) return null;
        return new { text, metadata };
    }

    private static PendingQuestion? ExtractPendingQuestion(TaskStatusUpdateEvent statusUpdate, TranslationContext ctx)
    {
        var toolUseId = statusUpdate.Metadata.GetMetadataString("toolUseId");
        var questions = ExtractA2AQuestions(statusUpdate);
        if (questions is null || questions.Count == 0) return null;

        return new PendingQuestion
        {
            // Both Id and the ToolUseId fallback are derived from the event id so the
            // serialized AG-UI args payload is byte-identical between live and replay.
            Id = FallbackQuestionId(ctx),
            ToolUseId = toolUseId ?? FallbackQuestionToolUseId(ctx),
            Questions = questions.Select(q => new UserQuestion
            {
                Question = q.Question,
                Header = q.Header,
                Options = q.Options.Select(o => new QuestionOption
                {
                    Label = o.Label,
                    Description = o.Description,
                }).ToList(),
                MultiSelect = q.MultiSelect,
            }).ToList(),
        };
    }

    private static List<A2AQuestionData>? ExtractA2AQuestions(TaskStatusUpdateEvent statusUpdate)
    {
        if (statusUpdate.Metadata is not null &&
            statusUpdate.Metadata.TryGetValue("questions", out var questionsElement))
        {
            try
            {
                return JsonSerializer.Deserialize<List<A2AQuestionData>>(questionsElement.GetRawText(), JsonOpts);
            }
            catch (JsonException) { /* fall through */ }
        }

        foreach (var part in statusUpdate.Status.Message?.Parts ?? [])
        {
            if (part is DataPart dataPart &&
                dataPart.Metadata.GetMetadataString("kind") == "questions" &&
                dataPart.HasDataProperty("questions"))
            {
                var data = dataPart.GetDataElement("questions");
                if (data is null) continue;
                try
                {
                    return JsonSerializer.Deserialize<List<A2AQuestionData>>(data.Value.GetRawText(), JsonOpts);
                }
                catch (JsonException) { /* fall through */ }
            }
        }

        return null;
    }

    private static string ExtractPlanContent(TaskStatusUpdateEvent statusUpdate)
    {
        if (statusUpdate.Metadata is not null &&
            statusUpdate.Metadata.TryGetValue("plan", out var planElement))
        {
            return planElement.GetString() ?? string.Empty;
        }

        foreach (var part in statusUpdate.Status.Message?.Parts ?? [])
        {
            if (part is DataPart dataPart && dataPart.Metadata.GetMetadataString("kind") == "plan")
            {
                return dataPart.GetDataString("plan") ?? string.Empty;
            }

            if (part is TextPart textPart)
            {
                return textPart.Text ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Derives a deterministic GUID-format string from a stable event seed and a per-use discriminator.
    /// Used as a fallback when the upstream A2A event omits an id field, so live and replay
    /// translations produce identical AG-UI payloads.
    /// </summary>
    private static string DeterministicId(string eventId, string discriminator)
    {
        var input = Encoding.UTF8.GetBytes($"{eventId}:{discriminator}");
        var hash = MD5.HashData(input);
        return new Guid(hash).ToString();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Deterministic fallback ids derived from the stored A2A event id. These keep the
    // live broadcast and the replay path emitting byte-identical AG-UI envelopes when
    // the upstream worker omits an optional id field. EventId is empty only in legacy
    // test fixtures that bypass the ingestor; in that case we fall back to a fresh Guid
    // to preserve the existing "ids must be non-empty" contract.
    private static string FallbackMessageId(TranslationContext ctx)
        => string.IsNullOrEmpty(ctx.EventId) ? Guid.NewGuid().ToString() : ctx.EventId;

    private static string FallbackToolCallId(TranslationContext ctx, int partIndex)
        => string.IsNullOrEmpty(ctx.EventId)
            ? Guid.NewGuid().ToString()
            : $"{ctx.EventId}-tool-{partIndex}";

    private static string FallbackInputRequiredToolCallId(TranslationContext ctx)
        => string.IsNullOrEmpty(ctx.EventId)
            ? Guid.NewGuid().ToString()
            : $"{ctx.EventId}-input";

    private static string FallbackQuestionId(TranslationContext ctx)
        => string.IsNullOrEmpty(ctx.EventId)
            ? Guid.NewGuid().ToString()
            : $"{ctx.EventId}-question";

    private static string FallbackQuestionToolUseId(TranslationContext ctx)
        => string.IsNullOrEmpty(ctx.EventId)
            ? Guid.NewGuid().ToString()
            : $"{ctx.EventId}-tooluse";
}
