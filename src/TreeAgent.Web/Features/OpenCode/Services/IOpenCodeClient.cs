using TreeAgent.Web.Features.OpenCode.Models;

namespace TreeAgent.Web.Features.OpenCode.Services;

/// <summary>
/// HTTP client for communicating with OpenCode server instances.
/// </summary>
public interface IOpenCodeClient
{
    /// <summary>
    /// Gets the health status of an OpenCode server.
    /// </summary>
    Task<HealthResponse> GetHealthAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Lists all sessions on a server.
    /// </summary>
    Task<List<OpenCodeSession>> ListSessionsAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Gets a specific session.
    /// </summary>
    Task<OpenCodeSession> GetSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new session.
    /// </summary>
    Task<OpenCodeSession> CreateSessionAsync(string baseUrl, string? title = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a session.
    /// </summary>
    Task<bool> DeleteSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Aborts a running session.
    /// </summary>
    Task<bool> AbortSessionAsync(string baseUrl, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets messages for a session.
    /// </summary>
    Task<List<OpenCodeMessage>> GetMessagesAsync(string baseUrl, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to a session and waits for the response.
    /// </summary>
    Task<OpenCodeMessage> SendPromptAsync(string baseUrl, string sessionId, PromptRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to a session without waiting for the response.
    /// </summary>
    Task SendPromptAsyncNoWait(string baseUrl, string sessionId, PromptRequest request, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to server events via SSE.
    /// </summary>
    IAsyncEnumerable<OpenCodeEvent> SubscribeToEventsAsync(string baseUrl, CancellationToken ct = default);
}
