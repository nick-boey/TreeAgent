namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Information about a running OpenCode server, used for SignalR broadcasting.
/// </summary>
public class RunningServerInfo
{
    /// <summary>
    /// The entity ID this server is associated with (PR ID, issue ID, or change ID).
    /// </summary>
    public required string EntityId { get; init; }
    
    /// <summary>
    /// The port the server is running on.
    /// </summary>
    public required int Port { get; init; }
    
    /// <summary>
    /// The base URL of the server (e.g., http://127.0.0.1:4099).
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// The external base URL for client access (may differ from BaseUrl in container mode).
    /// </summary>
    public string? ExternalBaseUrl { get; init; }

    /// <summary>
    /// The worktree path the server is operating in.
    /// </summary>
    public required string WorktreePath { get; init; }
    
    /// <summary>
    /// When the server was started.
    /// </summary>
    public required DateTime StartedAt { get; init; }
    
    /// <summary>
    /// The ID of the currently active session, if any.
    /// </summary>
    public string? ActiveSessionId { get; init; }
    
    /// <summary>
    /// The full web view URL including encoded path and session.
    /// Null if no active session.
    /// </summary>
    public string? WebViewUrl { get; init; }
    
    /// <summary>
    /// All sessions associated with this server.
    /// </summary>
    public List<OpenCodeSession> Sessions { get; init; } = [];
}
