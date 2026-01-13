using System.Diagnostics;

namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Represents a running OpenCode server instance associated with a PullRequest or FutureChange.
/// </summary>
public class OpenCodeServer
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The entity ID this server is associated with. Can be a PullRequest ID or a FutureChange ID (branch name).
    /// </summary>
    public required string EntityId { get; init; }
    public required string WorktreePath { get; init; }
    public required int Port { get; init; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// The external base URL (for client access). May differ from BaseUrl in container mode.
    /// Set by OpenCodeServerManager based on IAgentUrlService.
    /// </summary>
    public string? ExternalBaseUrl { get; set; }

    /// <summary>
    /// Gets the full web view URL including encoded path and session.
    /// Uses ExternalBaseUrl if set, otherwise BaseUrl.
    /// In container mode (when ExternalBaseUrl is a relative path), appends ?url= parameter
    /// so OpenCode uses the correct API base URL.
    /// Returns null if no active session.
    /// </summary>
    public string? WebViewUrl
    {
        get
        {
            if (ActiveSessionId == null) return null;

            var baseUrl = ExternalBaseUrl ?? BaseUrl;
            var url = $"{baseUrl}/{Base64UrlEncode(WorktreePath)}/session/{ActiveSessionId}";

            // In container mode, ExternalBaseUrl is a relative path like "/agent/4096"
            // Append ?url= parameter so OpenCode uses the correct API base URL
            if (ExternalBaseUrl?.StartsWith("/") == true)
            {
                url += $"?url={ExternalBaseUrl}";
            }

            return url;
        }
    }

    /// <summary>
    /// Encodes a string as URL-safe Base64 (matching OpenCode's encoding).
    /// Uses - instead of +, _ instead of /, and removes padding =.
    /// </summary>
    public static string Base64UrlEncode(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
    public Process? Process { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public OpenCodeServerStatus Status { get; set; } = OpenCodeServerStatus.Starting;
    public string? ActiveSessionId { get; set; }
    
    /// <summary>
    /// Whether the server was started with --continue to resume an existing session.
    /// </summary>
    public bool ContinueSession { get; init; }
}

public enum OpenCodeServerStatus
{
    Starting,
    Running,
    Stopped,
    Failed
}
