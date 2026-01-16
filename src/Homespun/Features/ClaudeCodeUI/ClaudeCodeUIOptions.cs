namespace Homespun.Features.ClaudeCodeUI;

/// <summary>
/// Configuration options for Claude Code UI harness.
/// </summary>
public class ClaudeCodeUIOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "ClaudeCodeUI";

    /// <summary>
    /// Path to the cloudcli executable.
    /// </summary>
    public string ExecutablePath { get; set; } = "cloudcli";

    /// <summary>
    /// Base port for server allocation.
    /// </summary>
    public int BasePort { get; set; } = 3001;

    /// <summary>
    /// Maximum number of concurrent servers.
    /// </summary>
    public int MaxConcurrentServers { get; set; } = 10;

    /// <summary>
    /// Timeout in milliseconds for server startup.
    /// </summary>
    public int ServerStartTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Health check retry interval in milliseconds.
    /// </summary>
    public int HealthCheckIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Maximum health check retries during startup.
    /// </summary>
    public int MaxHealthCheckRetries { get; set; } = 30;

    /// <summary>
    /// External hostname for generating URLs (optional).
    /// </summary>
    public string? ExternalHostname { get; set; }
}
