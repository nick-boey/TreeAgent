namespace Homespun.Features.SignalR;

/// <summary>
/// Configuration options for SignalR connections.
/// </summary>
public class SignalROptions
{
    public const string SectionName = "SignalR";

    /// <summary>
    /// Internal base URL for SignalR connections.
    /// When running in Docker, this should be set to http://localhost:8080
    /// to allow server-side code to connect to SignalR hubs without needing
    /// to resolve external hostnames.
    /// </summary>
    public string? InternalBaseUrl { get; set; }
}
