using Homespun.Features.OpenCode.Models;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for generating agent URLs that work in both local and container environments.
/// Auto-detects container mode from environment variables.
/// </summary>
public class AgentUrlService : IAgentUrlService
{
    private readonly OpenCodeOptions _options;
    private readonly bool _isContainerMode;
    private readonly string? _externalHostname;

    public AgentUrlService(IOptions<OpenCodeOptions> options)
    {
        _options = options.Value;

        // Auto-detect container mode from environment if not explicitly configured
        _isContainerMode = _options.ContainerMode ||
            Environment.GetEnvironmentVariable("CONTAINER_MODE") == "true" ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TAILSCALE_AUTH_KEY"));

        // Get hostname from config or environment
        _externalHostname = _options.ExternalHostname ??
            Environment.GetEnvironmentVariable("TAILSCALE_HOSTNAME") ??
            "homespun-vm";
    }

    public bool IsContainerMode => _isContainerMode;

    public string GetInternalBaseUrl(int port) => $"http://127.0.0.1:{port}";

    public string GetExternalBaseUrl(int port)
    {
        if (!_isContainerMode)
        {
            return GetInternalBaseUrl(port);
        }

        var portSuffix = _options.ExternalPort == 80 ? "" : $":{_options.ExternalPort}";
        return $"http://{_externalHostname}{portSuffix}{_options.AgentProxyBasePath}/{port}";
    }

    public string? GetWebViewUrl(int port, string worktreePath, string? sessionId)
    {
        if (sessionId == null) return null;

        var baseUrl = GetExternalBaseUrl(port);
        var encodedPath = OpenCodeServer.Base64UrlEncode(worktreePath);
        return $"{baseUrl}/{encodedPath}/session/{sessionId}";
    }
}
