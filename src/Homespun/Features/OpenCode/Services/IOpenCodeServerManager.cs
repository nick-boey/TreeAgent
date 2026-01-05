using Homespun.Features.OpenCode.Models;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Manages OpenCode server instances - spawning, tracking, and stopping servers.
/// </summary>
public interface IOpenCodeServerManager : IDisposable
{
    /// <summary>
    /// Starts a new OpenCode server for an entity (PullRequest or FutureChange).
    /// </summary>
    /// <param name="entityId">The entity ID this server is for (PR ID or change ID/branch name)</param>
    /// <param name="worktreePath">The worktree directory to run the server in</param>
    /// <param name="continueSession">Whether to start with --continue to resume existing session</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The started server instance</returns>
    Task<OpenCodeServer> StartServerAsync(string entityId, string worktreePath, bool continueSession = false, CancellationToken ct = default);

    /// <summary>
    /// Stops a running server for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID or change ID)</param>
    /// <param name="ct">Cancellation token</param>
    Task StopServerAsync(string entityId, CancellationToken ct = default);

    /// <summary>
    /// Gets the running server for an entity, if any.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID or change ID)</param>
    /// <returns>The server instance or null if not running</returns>
    OpenCodeServer? GetServerForEntity(string entityId);

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
}
