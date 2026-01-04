using Homespun.Features.OpenCode.Models;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Manages OpenCode server instances - spawning, tracking, and stopping servers.
/// </summary>
public interface IOpenCodeServerManager : IDisposable
{
    /// <summary>
    /// Starts a new OpenCode server for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID this server is for</param>
    /// <param name="worktreePath">The worktree directory to run the server in</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The started server instance</returns>
    Task<OpenCodeServer> StartServerAsync(string pullRequestId, string worktreePath, CancellationToken ct = default);

    /// <summary>
    /// Stops a running server for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="ct">Cancellation token</param>
    Task StopServerAsync(string pullRequestId, CancellationToken ct = default);

    /// <summary>
    /// Gets the running server for a pull request, if any.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <returns>The server instance or null if not running</returns>
    OpenCodeServer? GetServerForPullRequest(string pullRequestId);

    /// <summary>
    /// Gets all currently running servers.
    /// </summary>
    IReadOnlyList<OpenCodeServer> GetRunningServers();

    /// <summary>
    /// Checks if a server is healthy by calling its health endpoint.
    /// </summary>
    /// <param name="server">The server to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(OpenCodeServer server, CancellationToken ct = default);

    /// <summary>
    /// Allocates the next available port for a new server.
    /// </summary>
    int AllocatePort();

    /// <summary>
    /// Releases a port back to the pool.
    /// </summary>
    /// <param name="port">The port to release</param>
    void ReleasePort(int port);
}
