using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing Claude Code sessions using the ClaudeAgentSdk.
/// </summary>
public class ClaudeSessionService : IClaudeSessionService, IAsyncDisposable
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly SessionOptionsFactory _optionsFactory;
    private readonly ILogger<ClaudeSessionService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly ConcurrentDictionary<string, ClaudeAgentOptions> _sessionOptions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionCts = new();

    public ClaudeSessionService(
        IClaudeSessionStore sessionStore,
        SessionOptionsFactory optionsFactory,
        ILogger<ClaudeSessionService> logger,
        IHubContext<ClaudeCodeHub> hubContext)
    {
        _sessionStore = sessionStore;
        _optionsFactory = optionsFactory;
        _logger = logger;
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public async Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Model = model,
            Mode = mode,
            Status = ClaudeSessionStatus.Starting,
            CreatedAt = DateTime.UtcNow,
            SystemPrompt = systemPrompt
        };

        _sessionStore.Add(session);
        _logger.LogInformation("Created session {SessionId} for entity {EntityId} in mode {Mode}",
            sessionId, entityId, mode);

        // Create cancellation token source for this session
        var cts = new CancellationTokenSource();
        _sessionCts[sessionId] = cts;

        // Create and store the SDK options (we'll create clients per-query for streaming support)
        var options = _optionsFactory.Create(mode, workingDirectory, model, systemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions; // Allow all tools without prompting
        options.IncludePartialMessages = true; // Enable streaming with --print mode
        _sessionOptions[sessionId] = options;

        session.Status = ClaudeSessionStatus.Running;
        _logger.LogInformation("Session {SessionId} initialized and ready", sessionId);

        // Notify clients about the new session
        await _hubContext.BroadcastSessionStarted(session);

        return session;
    }

    /// <inheritdoc />
    public Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, PermissionMode.BypassPermissions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == ClaudeSessionStatus.Stopped || session.Status == ClaudeSessionStatus.Error)
        {
            throw new InvalidOperationException($"Session {sessionId} is not active (status: {session.Status})");
        }

        if (!_sessionOptions.TryGetValue(sessionId, out var baseOptions))
        {
            throw new InvalidOperationException($"No options found for session {sessionId}");
        }

        _logger.LogInformation("Sending message to session {SessionId} with permission mode {PermissionMode}", sessionId, permissionMode);

        // Add user message to session
        var userMessage = new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = message }]
        };
        session.Messages.Add(userMessage);
        session.Status = ClaudeSessionStatus.Processing;

        // Notify clients about the user message
        await _hubContext.BroadcastMessageReceived(sessionId, userMessage);

        try
        {
            // Get the combined cancellation token
            var cts = _sessionCts.GetValueOrDefault(sessionId);
            using var linkedCts = cts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Create options for this query, using Resume if we have a conversation ID from previous query
            var queryOptions = new ClaudeAgentOptions
            {
                AllowedTools = baseOptions.AllowedTools,
                SystemPrompt = baseOptions.SystemPrompt,
                McpServers = baseOptions.McpServers,
                PermissionMode = permissionMode,
                MaxTurns = baseOptions.MaxTurns,
                DisallowedTools = baseOptions.DisallowedTools,
                Model = baseOptions.Model,
                Cwd = baseOptions.Cwd,
                Settings = baseOptions.Settings,
                AddDirs = baseOptions.AddDirs,
                Env = baseOptions.Env,
                ExtraArgs = baseOptions.ExtraArgs,
                IncludePartialMessages = true, // Enable streaming
                SettingSources = baseOptions.SettingSources,
                // Resume from previous conversation if we have a ConversationId
                Resume = session.ConversationId
            };

            // Create a new client for this query with the message as prompt
            // This uses --print mode which supports --include-partial-messages for streaming
            await using var client = new ClaudeSdkClient(queryOptions);
            await client.ConnectAsync(message, linkedCts.Token);

            var assistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };

            await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(linkedCts.Token))
            {
                _logger.LogDebug("Received SDK message type: {MessageType}", msg.GetType().Name);
                await ProcessSdkMessageAsync(sessionId, session, assistantMessage, msg, linkedCts.Token);

                // Stop processing after receiving the result message
                if (msg is ResultMessage)
                    break;
            }

            // Add completed assistant message
            if (assistantMessage.Content.Count > 0)
            {
                session.Messages.Add(assistantMessage);
            }

            session.Status = ClaudeSessionStatus.Running;
            _logger.LogInformation("Message processing completed for session {SessionId}", sessionId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled for session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Stopped;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message in session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = ex.Message;
            throw;
        }
    }

    private async Task ProcessSdkMessageAsync(
        string sessionId,
        ClaudeSession session,
        ClaudeMessage assistantMessage,
        Message sdkMessage,
        CancellationToken cancellationToken)
    {
        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
                // Handle partial streaming updates for real-time display
                await ProcessStreamEventAsync(sessionId, session, assistantMessage, streamEvent, cancellationToken);
                break;

            case AssistantMessage assistantMsg:
                // Content already processed by streaming events (content_block_start/delta/stop)
                // Just ensure all blocks are finalized - don't add duplicates or broadcast again
                foreach (var block in assistantMessage.Content)
                {
                    block.IsStreaming = false;
                }
                break;

            case ResultMessage resultMsg:
                session.TotalCostUsd = (decimal)(resultMsg.TotalCostUsd ?? 0);
                session.TotalDurationMs = resultMsg.DurationMs;
                // Store the Claude CLI session ID for use with --resume in subsequent messages
                session.ConversationId = resultMsg.SessionId;
                _logger.LogDebug("Stored ConversationId {ConversationId} for session resumption", resultMsg.SessionId);
                await _hubContext.BroadcastSessionResultReceived(sessionId, session.TotalCostUsd, resultMsg.DurationMs);
                break;

            case SystemMessage systemMsg:
                _logger.LogDebug("System message received: {Subtype}", systemMsg.Subtype);
                break;
        }
    }

    private async Task ProcessStreamEventAsync(
        string sessionId,
        ClaudeSession session,
        ClaudeMessage assistantMessage,
        StreamEvent streamEvent,
        CancellationToken cancellationToken)
    {
        if (streamEvent.Event == null || !streamEvent.Event.TryGetValue("type", out var typeObj))
            return;

        var eventType = typeObj is JsonElement typeElement ? typeElement.GetString() : typeObj?.ToString();
        _logger.LogDebug("Processing stream event type: {EventType} for session {SessionId}", eventType, sessionId);

        switch (eventType)
        {
            case "content_block_start":
                await HandleContentBlockStart(sessionId, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            case "content_block_delta":
                await HandleContentBlockDelta(sessionId, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            case "content_block_stop":
                await HandleContentBlockStop(sessionId, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            default:
                _logger.LogDebug("Unhandled stream event type: {EventType}", eventType);
                break;
        }
    }

    private async Task HandleContentBlockStart(
        string sessionId,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetValue("content_block", out var blockObj))
            return;

        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        var blockJson = blockObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(blockObj);
        var blockData = JsonSerializer.Deserialize<Dictionary<string, object>>(blockJson);
        if (blockData == null) return;

        var blockType = GetStringValue(blockData, "type");
        ClaudeMessageContent? content = blockType switch
        {
            "text" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = "",
                IsStreaming = true,
                Index = index
            },
            "thinking" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Thinking,
                Text = "",
                IsStreaming = true,
                Index = index
            },
            "tool_use" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.ToolUse,
                ToolName = GetStringValue(blockData, "name") ?? "unknown",
                ToolInput = "",
                IsStreaming = true,
                Index = index
            },
            _ => null
        };

        if (content != null)
        {
            assistantMessage.Content.Add(content);
            _logger.LogDebug("Broadcasting content_block_start for index {Index}, type {Type}", index, blockType);
            await _hubContext.BroadcastStreamingContentStarted(sessionId, content, index);
        }
    }

    private async Task HandleContentBlockDelta(
        string sessionId,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetValue("delta", out var deltaObj))
            return;

        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        var deltaJson = deltaObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(deltaObj);
        var deltaData = JsonSerializer.Deserialize<Dictionary<string, object>>(deltaJson);
        if (deltaData == null) return;

        var deltaType = GetStringValue(deltaData, "type");

        // Find the streaming content block by index, or fall back to last streaming block
        var streamingBlock = index >= 0
            ? assistantMessage.Content.FirstOrDefault(c => c.IsStreaming && c.Index == index)
            : assistantMessage.Content.LastOrDefault(c => c.IsStreaming);

        if (streamingBlock == null) return;

        switch (deltaType)
        {
            case "text_delta":
                var textDelta = GetStringValue(deltaData, "text") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + textDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, textDelta, index);
                break;

            case "thinking_delta":
                var thinkingDelta = GetStringValue(deltaData, "thinking") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + thinkingDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, thinkingDelta, index);
                break;

            case "input_json_delta":
                var inputDelta = GetStringValue(deltaData, "partial_json") ?? "";
                streamingBlock.ToolInput = (streamingBlock.ToolInput ?? "") + inputDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, inputDelta, index);
                break;
        }
    }

    private async Task HandleContentBlockStop(
        string sessionId,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        // Find the streaming content block by index, or fall back to last streaming block
        var streamingBlock = index >= 0
            ? assistantMessage.Content.FirstOrDefault(c => c.IsStreaming && c.Index == index)
            : assistantMessage.Content.LastOrDefault(c => c.IsStreaming);

        if (streamingBlock != null)
        {
            streamingBlock.IsStreaming = false;
            _logger.LogDebug("Broadcasting content_block_stop for index {Index}", index);
            await _hubContext.BroadcastStreamingContentStopped(sessionId, streamingBlock, index);
        }
    }

    private static string? GetStringValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        return value is JsonElement element ? element.GetString() : value?.ToString();
    }

    private static int? GetIntValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : null;
        }
        return value is int intValue ? intValue : null;
    }

    private static ClaudeMessageContent? ConvertContentBlock(object block)
    {
        return block switch
        {
            TextBlock textBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = textBlock.Text
            },
            ThinkingBlock thinkingBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Thinking,
                Text = thinkingBlock.Thinking
            },
            ToolUseBlock toolUseBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.ToolUse,
                ToolName = toolUseBlock.Name,
                ToolInput = toolUseBlock.Input?.ToString()
            },
            ToolResultBlock toolResultBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.ToolResult,
                Text = toolResultBlock.Content?.ToString()
            },
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Attempted to stop non-existent session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation("Stopping session {SessionId}", sessionId);

        // Cancel any ongoing operations
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        // Remove stored options
        _sessionOptions.TryRemove(sessionId, out _);

        // Update session status and remove from store
        session.Status = ClaudeSessionStatus.Stopped;
        _sessionStore.Remove(sessionId);

        // Notify clients
        await _hubContext.BroadcastSessionStopped(sessionId);
    }

    /// <inheritdoc />
    public ClaudeSession? GetSession(string sessionId)
    {
        return _sessionStore.GetById(sessionId);
    }

    /// <inheritdoc />
    public ClaudeSession? GetSessionByEntityId(string entityId)
    {
        return _sessionStore.GetAll().FirstOrDefault(s => s.EntityId == entityId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId)
    {
        return _sessionStore.GetAll()
            .Where(s => s.ProjectId == projectId)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        return _sessionStore.GetAll().ToList();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing ClaudeSessionService");

        // Cancel all sessions
        foreach (var cts in _sessionCts.Values)
        {
            try
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
            catch { }
        }
        _sessionCts.Clear();

        // Clear stored options
        _sessionOptions.Clear();
    }
}
