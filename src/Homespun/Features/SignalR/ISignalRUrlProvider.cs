namespace Homespun.Features.SignalR;

/// <summary>
/// Provides URLs for SignalR hub connections.
/// </summary>
public interface ISignalRUrlProvider
{
    /// <summary>
    /// Gets the full URL for a SignalR hub path.
    /// </summary>
    /// <param name="hubPath">The hub path (e.g., "/hubs/claudecode")</param>
    /// <returns>The full URL to the hub</returns>
    string GetHubUrl(string hubPath);
}
