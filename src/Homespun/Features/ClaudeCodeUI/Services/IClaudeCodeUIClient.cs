using Homespun.Features.ClaudeCodeUI.Models;

namespace Homespun.Features.ClaudeCodeUI.Services;

/// <summary>
/// HTTP client for communicating with Claude Code UI server instances.
/// </summary>
public interface IClaudeCodeUIClient
{
    /// <summary>
    /// Checks if the server is healthy.
    /// </summary>
    Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the agent API and returns the response via SSE streaming.
    /// </summary>
    Task<ClaudeCodeUIResponse> SendPromptAsync(
        string baseUrl,
        ClaudeCodeUIPromptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt without waiting for the full response.
    /// </summary>
    Task SendPromptNoWaitAsync(
        string baseUrl,
        ClaudeCodeUIPromptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all projects/conversations.
    /// </summary>
    Task<List<ClaudeCodeUIProject>> GetProjectsAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Gets sessions for a project.
    /// </summary>
    Task<List<ClaudeCodeUISession>> GetSessionsAsync(
        string baseUrl,
        string projectPath,
        CancellationToken ct = default);

    /// <summary>
    /// Subscribes to real-time events from the server via SSE.
    /// </summary>
    IAsyncEnumerable<ClaudeCodeUIEvent> SubscribeToEventsAsync(
        string baseUrl,
        string? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures a default user exists for platform mode authentication.
    /// Registers a user if the database is empty.
    /// </summary>
    Task<bool> EnsureDefaultUserAsync(string baseUrl, CancellationToken ct = default);
}
