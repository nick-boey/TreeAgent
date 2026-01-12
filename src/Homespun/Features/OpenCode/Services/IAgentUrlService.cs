namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for generating agent URLs that work in both local and container environments.
/// </summary>
public interface IAgentUrlService
{
    /// <summary>
    /// Gets the externally-accessible base URL for an agent server.
    /// In local mode: http://127.0.0.1:{port}
    /// In container mode: http://{hostname}/agent/{port}
    /// </summary>
    string GetExternalBaseUrl(int port);

    /// <summary>
    /// Gets the full web view URL for an agent session.
    /// </summary>
    string? GetWebViewUrl(int port, string worktreePath, string? sessionId);

    /// <summary>
    /// Gets the internal base URL (always localhost) for internal API calls.
    /// </summary>
    string GetInternalBaseUrl(int port);

    /// <summary>
    /// Whether container mode is enabled.
    /// </summary>
    bool IsContainerMode { get; }
}
