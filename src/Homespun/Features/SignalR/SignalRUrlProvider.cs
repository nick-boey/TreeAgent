using Microsoft.Extensions.Options;

namespace Homespun.Features.SignalR;

/// <summary>
/// Provides URLs for SignalR hub connections.
/// Uses configured internal base URL (for Docker) or falls back to localhost.
/// </summary>
public class SignalRUrlProvider : ISignalRUrlProvider
{
    private readonly string _baseUrl;

    public SignalRUrlProvider(IOptions<SignalROptions> options)
    {
        // Use configured internal URL, or fall back to localhost:5000 for development
        var internalBaseUrl = options.Value.InternalBaseUrl;
        _baseUrl = string.IsNullOrEmpty(internalBaseUrl)
            ? "http://localhost:5000"
            : internalBaseUrl.TrimEnd('/');
    }

    /// <inheritdoc/>
    public string GetHubUrl(string hubPath)
    {
        // Ensure hub path starts with /
        if (!hubPath.StartsWith('/'))
        {
            hubPath = "/" + hubPath;
        }

        return _baseUrl + hubPath;
    }
}
