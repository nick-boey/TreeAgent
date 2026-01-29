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
    private readonly IClaudeSessionDiscovery _sessionDiscovery;
    private readonly ISessionMetadataStore _metadataStore;
    private readonly IToolResultParser _toolResultParser;
    private readonly ConcurrentDictionary<string, ClaudeAgentOptions> _sessionOptions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionCts = new();

    /// <summary>
    /// Maps session ID -> (tool use ID -> tool name) for linking tool results to their tool uses.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionToolUses = new();

    public ClaudeSessionService(
        IClaudeSessionStore sessionStore,
        SessionOptionsFactory optionsFactory,
        ILogger<ClaudeSessionService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IClaudeSessionDiscovery sessionDiscovery,
        ISessionMetadataStore metadataStore,
        IToolResultParser toolResultParser)
    {
        _sessionStore = sessionStore;
        _optionsFactory = optionsFactory;
        _logger = logger;
        _hubContext = hubContext;
        _sessionDiscovery = sessionDiscovery;
        _metadataStore = metadataStore;
        _toolResultParser = toolResultParser;
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

        session.Status = ClaudeSessionStatus.WaitingForInput;
        _logger.LogInformation("Session {SessionId} initialized and ready", sessionId);

        // Save metadata for future resumption
        var metadata = new SessionMetadata(
            SessionId: session.ConversationId ?? sessionId, // Will be updated when we get the real ConversationId
            EntityId: entityId,
            ProjectId: projectId,
            WorkingDirectory: workingDirectory,
            Mode: mode,
            Model: model,
            SystemPrompt: systemPrompt,
            CreatedAt: session.CreatedAt
        );
        await _metadataStore.SaveAsync(metadata, cancellationToken);

        // Notify clients about the new session
        await _hubContext.BroadcastSessionStarted(session);

        return session;
    }

    /// <inheritdoc />
    public async Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resuming session {ClaudeSessionId} for entity {EntityId}", sessionId, entityId);

        // Try to get our saved metadata for this session
        var metadata = await _metadataStore.GetBySessionIdAsync(sessionId, cancellationToken);

        // Create a new ClaudeSession using the discovered session ID as ConversationId
        var newSessionId = Guid.NewGuid().ToString();
        var session = new ClaudeSession
        {
            Id = newSessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            ConversationId = sessionId, // The Claude CLI session ID for --resume
            Model = metadata?.Model ?? "sonnet",
            Mode = metadata?.Mode ?? SessionMode.Build,
            SystemPrompt = metadata?.SystemPrompt,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Messages = []
        };

        // Create SDK options with Resume flag
        var options = _optionsFactory.Create(
            session.Mode,
            workingDirectory,
            session.Model,
            session.SystemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions;
        options.IncludePartialMessages = true;
        options.Resume = sessionId; // THIS IS THE KEY - tells Claude CLI to resume

        _sessionOptions[newSessionId] = options;
        _sessionCts[newSessionId] = new CancellationTokenSource();
        _sessionStore.Add(session);

        _logger.LogInformation("Resumed session {NewSessionId} with ConversationId {ConversationId}",
            newSessionId, sessionId);

        await _hubContext.BroadcastSessionStarted(session);
        return session;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering resumable sessions for entity {EntityId} at {WorkingDirectory}",
            entityId, workingDirectory);

        // Discover sessions from Claude's storage
        var discoveredSessions = await _sessionDiscovery.DiscoverSessionsAsync(workingDirectory, cancellationToken);

        var resumableSessions = new List<ResumableSession>();
        foreach (var discovered in discoveredSessions)
        {
            // Try to get our metadata for this session
            var metadata = await _metadataStore.GetBySessionIdAsync(discovered.SessionId, cancellationToken);

            // Get message count if possible
            var messageCount = await _sessionDiscovery.GetMessageCountAsync(
                discovered.SessionId, workingDirectory, cancellationToken);

            resumableSessions.Add(new ResumableSession(
                SessionId: discovered.SessionId,
                LastActivityAt: discovered.LastModified,
                Mode: metadata?.Mode,
                Model: metadata?.Model,
                MessageCount: messageCount
            ));
        }

        _logger.LogDebug("Found {Count} resumable sessions for entity {EntityId}",
            resumableSessions.Count, entityId);

        return resumableSessions;
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
        session.Status = ClaudeSessionStatus.Running;

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

            session.Status = ClaudeSessionStatus.WaitingForInput;
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
                var previousConversationId = session.ConversationId;
                session.ConversationId = resultMsg.SessionId;
                _logger.LogDebug("Stored ConversationId {ConversationId} for session resumption", resultMsg.SessionId);

                // Update metadata with the actual Claude session ID if it changed
                if (resultMsg.SessionId != null && resultMsg.SessionId != previousConversationId)
                {
                    var metadata = new SessionMetadata(
                        SessionId: resultMsg.SessionId,
                        EntityId: session.EntityId,
                        ProjectId: session.ProjectId,
                        WorkingDirectory: session.WorkingDirectory,
                        Mode: session.Mode,
                        Model: session.Model,
                        SystemPrompt: session.SystemPrompt,
                        CreatedAt: session.CreatedAt
                    );
                    await _metadataStore.SaveAsync(metadata, cancellationToken);
                }

                await _hubContext.BroadcastSessionResultReceived(sessionId, session.TotalCostUsd, resultMsg.DurationMs);
                break;

            case SystemMessage systemMsg:
                _logger.LogDebug("System message received: {Subtype}", systemMsg.Subtype);
                break;

            case UserMessage userMsg:
                // Handle user messages containing tool results
                await ProcessUserMessageAsync(sessionId, session, userMsg, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Processes a UserMessage from the SDK, which may contain tool results.
    /// In Claude's API protocol, tool results are sent back as user messages.
    /// </summary>
    private async Task ProcessUserMessageAsync(
        string sessionId,
        ClaudeSession session,
        UserMessage userMsg,
        CancellationToken cancellationToken)
    {
        // Check if content contains tool results (content is a list of content blocks)
        if (userMsg.Content is not List<object> contentBlocks)
        {
            _logger.LogDebug("UserMessage content is not a list of blocks, skipping tool result processing");
            return;
        }

        var toolResultContents = new List<ClaudeMessageContent>();

        foreach (var block in contentBlocks)
        {
            if (block is ToolResultBlock toolResultBlock)
            {
                var content = ConvertToolResultBlock(sessionId, toolResultBlock);
                toolResultContents.Add(content);
                _logger.LogDebug("Processed tool result for tool use ID: {ToolUseId}, tool: {ToolName}",
                    toolResultBlock.ToolUseId, content.ToolName);
            }
        }

        if (toolResultContents.Count > 0)
        {
            var toolResultMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.User,
                Content = toolResultContents
            };
            session.Messages.Add(toolResultMessage);

            // Broadcast to clients for real-time UI updates
            await _hubContext.BroadcastMessageReceived(sessionId, toolResultMessage);
            _logger.LogDebug("Broadcasted {Count} tool result(s) for session {SessionId}",
                toolResultContents.Count, sessionId);
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
            "tool_use" => CreateToolUseContent(sessionId, blockData, index),
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

    /// <summary>
    /// Creates a tool use content block and tracks the tool use ID -> name mapping.
    /// </summary>
    private ClaudeMessageContent CreateToolUseContent(string sessionId, Dictionary<string, object> blockData, int index)
    {
        var toolUseId = GetStringValue(blockData, "id") ?? "";
        var toolName = GetStringValue(blockData, "name") ?? "unknown";

        // Track the tool use ID -> name mapping for this session
        if (!string.IsNullOrEmpty(toolUseId))
        {
            var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
            sessionToolUses[toolUseId] = toolName;
            _logger.LogDebug("Tracked tool use: {ToolUseId} -> {ToolName}", toolUseId, toolName);
        }

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = toolName,
            ToolUseId = toolUseId,
            ToolInput = "",
            IsStreaming = true,
            Index = index
        };
    }

    /// <summary>
    /// Looks up the tool name for a given tool use ID.
    /// </summary>
    private string GetToolNameForUseId(string sessionId, string toolUseId)
    {
        if (_sessionToolUses.TryGetValue(sessionId, out var toolUses) &&
            toolUses.TryGetValue(toolUseId, out var toolName))
        {
            return toolName;
        }
        return "unknown";
    }

    private ClaudeMessageContent? ConvertContentBlock(string sessionId, object block)
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
            ToolUseBlock toolUseBlock => ConvertToolUseBlock(sessionId, toolUseBlock),
            ToolResultBlock toolResultBlock => ConvertToolResultBlock(sessionId, toolResultBlock),
            _ => null
        };
    }

    private ClaudeMessageContent ConvertToolUseBlock(string sessionId, ToolUseBlock toolUseBlock)
    {
        // Track the tool use ID -> name mapping
        if (!string.IsNullOrEmpty(toolUseBlock.Id))
        {
            var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
            sessionToolUses[toolUseBlock.Id] = toolUseBlock.Name;
        }

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = toolUseBlock.Name,
            ToolUseId = toolUseBlock.Id,
            ToolInput = toolUseBlock.Input != null ? JsonSerializer.Serialize(toolUseBlock.Input) : null
        };
    }

    private ClaudeMessageContent ConvertToolResultBlock(string sessionId, ToolResultBlock toolResultBlock)
    {
        // Look up the tool name from the tool use ID
        var toolName = GetToolNameForUseId(sessionId, toolResultBlock.ToolUseId);
        var isError = toolResultBlock.IsError ?? false;
        var contentString = toolResultBlock.Content?.ToString() ?? "";

        // Parse the result for rich display
        var parsedResult = _toolResultParser.Parse(toolName, toolResultBlock.Content, isError);

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolResult,
            ToolName = toolName,
            ToolUseId = toolResultBlock.ToolUseId,
            ToolSuccess = !isError,
            Text = contentString,
            ParsedToolResult = parsedResult
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

        // Remove stored options and tool use tracking
        _sessionOptions.TryRemove(sessionId, out _);
        _sessionToolUses.TryRemove(sessionId, out _);

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

        // Clear stored options and tool use tracking
        _sessionOptions.Clear();
        _sessionToolUses.Clear();
    }
}
