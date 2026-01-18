using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, ClaudeSdkClient> _clients = new();
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

        // Create and connect the SDK client
        try
        {
            var options = _optionsFactory.Create(mode, workingDirectory, model, systemPrompt);
            options.PermissionMode = PermissionMode.AcceptEdits; // Allow file operations
            options.IncludePartialMessages = true; // Enable streaming

            var client = new ClaudeSdkClient(options);
            _clients[sessionId] = client;

            await client.ConnectAsync();

            session.Status = ClaudeSessionStatus.Running;
            _logger.LogInformation("Session {SessionId} connected and running", sessionId);

            // Notify clients about the new session
            await _hubContext.BroadcastSessionStarted(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = ex.Message;

            // Clean up
            _clients.TryRemove(sessionId, out _);
            _sessionCts.TryRemove(sessionId, out var removedCts);
            removedCts?.Dispose();
        }

        return session;
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
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

        if (!_clients.TryGetValue(sessionId, out var client))
        {
            throw new InvalidOperationException($"No client found for session {sessionId}");
        }

        _logger.LogInformation("Sending message to session {SessionId}", sessionId);

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

            // Send query and process streaming response
            await client.QueryAsync(message);

            var assistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };

            await foreach (var msg in client.ReceiveResponseAsync().WithCancellation(linkedCts.Token))
            {
                await ProcessSdkMessageAsync(sessionId, session, assistantMessage, msg, linkedCts.Token);
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
            case AssistantMessage assistantMsg:
                foreach (var block in assistantMsg.Content)
                {
                    var content = ConvertContentBlock(block);
                    if (content != null)
                    {
                        assistantMessage.Content.Add(content);
                        await _hubContext.BroadcastContentBlockReceived(sessionId, content);
                    }
                }
                break;

            case ResultMessage resultMsg:
                session.TotalCostUsd = (decimal)(resultMsg.TotalCostUsd ?? 0);
                session.TotalDurationMs = resultMsg.DurationMs;
                await _hubContext.BroadcastSessionResultReceived(sessionId, session.TotalCostUsd, resultMsg.DurationMs);
                break;

            case SystemMessage systemMsg:
                _logger.LogDebug("System message received: {Subtype}", systemMsg.Subtype);
                break;
        }
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

        // Dispose the client
        if (_clients.TryRemove(sessionId, out var client))
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing client for session {SessionId}", sessionId);
            }
        }

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

        // Dispose all clients
        foreach (var client in _clients.Values)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch { }
        }
        _clients.Clear();
    }
}
