using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing Claude Code sessions.
/// </summary>
public interface IClaudeSessionService
{
    /// <summary>
    /// Starts a new Claude Code session for an entity.
    /// </summary>
    Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session.
    /// </summary>
    Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session with a specific permission mode.
    /// </summary>
    Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an existing session.
    /// </summary>
    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    ClaudeSession? GetSession(string sessionId);

    /// <summary>
    /// Gets a session by entity ID.
    /// </summary>
    ClaudeSession? GetSessionByEntityId(string entityId);

    /// <summary>
    /// Gets all sessions for a project.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetAllSessions();
}
