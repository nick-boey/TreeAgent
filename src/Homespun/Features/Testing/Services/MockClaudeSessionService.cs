using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IClaudeSessionService that simulates Claude Code sessions.
/// </summary>
public class MockClaudeSessionService : IClaudeSessionService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<MockClaudeSessionService> _logger;

    public MockClaudeSessionService(
        IClaudeSessionStore sessionStore,
        ILogger<MockClaudeSessionService> logger)
    {
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StartSession for entity {EntityId} in project {ProjectId}, mode: {Mode}",
            entityId, projectId, mode);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = mode,
            Model = model,
            SystemPrompt = systemPrompt,
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        // Add an initial assistant message
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = $"[Mock Session] Ready to help with your {mode.ToString().ToLower()} task. " +
                           "This is a mock session - no actual Claude API calls will be made."
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        await SendMessageAsync(sessionId, message, PermissionMode.Default, cancellationToken);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        PermissionMode permissionMode,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SendMessage to session {SessionId}: {Message}", sessionId, message);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        // Add user message
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = message
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        session.Status = ClaudeSessionStatus.Running;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);

        // Simulate processing delay
        await Task.Delay(500, cancellationToken);

        // Add mock response
        var responseText = GenerateMockResponse(message, session.Mode);
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Thinking,
                    Thinking = "Analyzing the request and preparing a mock response..."
                },
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = responseText
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        session.TotalCostUsd += 0.01m; // Mock cost
        session.TotalDurationMs += 500;
        _sessionStore.Update(session);
    }

    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopSession {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.Status = ClaudeSessionStatus.Stopped;
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public ClaudeSession? GetSession(string sessionId)
    {
        return _sessionStore.GetById(sessionId);
    }

    public ClaudeSession? GetSessionByEntityId(string entityId)
    {
        return _sessionStore.GetByEntityId(entityId);
    }

    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId)
    {
        return _sessionStore.GetByProjectId(projectId);
    }

    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        return _sessionStore.GetAll();
    }

    public Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ResumeSession {SessionId} for entity {EntityId}", sessionId, entityId);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = SessionMode.Build,
            Model = "opus",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        // Add an initial message indicating this is a resumed session
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = $"[Mock Resumed Session] Resumed from session {sessionId}. " +
                           "This is a mock session - no actual Claude API calls will be made."
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetResumableSessions for entity {EntityId}", entityId);

        // Return a mock resumable session
        var sessions = new List<ResumableSession>
        {
            new ResumableSession(
                SessionId: Guid.NewGuid().ToString(),
                LastActivityAt: DateTime.UtcNow.AddHours(-1),
                Mode: SessionMode.Build,
                Model: "opus",
                MessageCount: 5
            )
        };

        return Task.FromResult<IReadOnlyList<ResumableSession>>(sessions);
    }

    private static string GenerateMockResponse(string userMessage, SessionMode mode)
    {
        var truncatedMessage = userMessage.Length > 50
            ? userMessage[..50] + "..."
            : userMessage;

        return mode switch
        {
            SessionMode.Plan => $"""
                [Mock Plan Response]

                I've analyzed your request: "{truncatedMessage}"

                Here's a mock implementation plan:

                1. **Phase 1: Analysis** - Review the current codebase structure
                2. **Phase 2: Design** - Create a design for the requested changes
                3. **Phase 3: Implementation** - Implement the changes
                4. **Phase 4: Testing** - Verify the implementation works correctly

                This is a mock response - in a real session, I would provide detailed analysis and recommendations.
                """,

            SessionMode.Build => $"""
                [Mock Build Response]

                I've processed your request: "{truncatedMessage}"

                In mock mode, I would typically:
                - Read relevant files
                - Make code changes using Edit/Write tools
                - Run tests to verify changes

                Mock session complete. No actual code changes were made.
                """,

            _ => $"[Mock Response] Processed: {truncatedMessage}"
        };
    }
}
