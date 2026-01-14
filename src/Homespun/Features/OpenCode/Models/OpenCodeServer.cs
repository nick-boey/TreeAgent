using System.Diagnostics;

namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Represents a running OpenCode server instance associated with a PullRequest or FutureChange.
/// </summary>
public class OpenCodeServer
{
    /// <summary>
    /// External hostname for generating URLs accessible from outside the container.
    /// Set at application startup from configuration.
    /// </summary>
    public static string? ExternalHostname { get; set; }

    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The entity ID this server is associated with. Can be a PullRequest ID or a FutureChange ID (branch name).
    /// </summary>
    public required string EntityId { get; init; }
    public required string WorktreePath { get; init; }
    public required int Port { get; init; }

    /// <summary>
    /// Internal base URL for server-to-server communication (always localhost).
    /// </summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// External base URL for UI links. Uses ExternalHostname if configured, otherwise localhost.
    /// </summary>
    public string ExternalBaseUrl
    {
        get
        {
            var url = !string.IsNullOrEmpty(ExternalHostname)
                ? $"https://{ExternalHostname}:{Port}"
                : BaseUrl;
            // Debug logging - can be removed once issue is resolved
            System.Diagnostics.Debug.WriteLine($"[OpenCodeServer] ExternalBaseUrl computed: ExternalHostname='{ExternalHostname ?? "(null)"}', Port={Port}, Result='{url}'");
            return url;
        }
    }

    /// <summary>
    /// Gets the full web view URL including encoded path and session.
    /// Returns null if no active session.
    /// Uses external hostname if configured.
    /// </summary>
    public string? WebViewUrl => ActiveSessionId != null
        ? $"{ExternalBaseUrl}/{Base64UrlEncode(WorktreePath)}/session/{ActiveSessionId}"
        : null;

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
