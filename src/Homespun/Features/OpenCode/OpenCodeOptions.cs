namespace Homespun.Features.OpenCode;

/// <summary>
/// Configuration options for the OpenCode integration.
/// </summary>
public class OpenCodeOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "OpenCode";

    /// <summary>
    /// Path to the OpenCode executable. Can be just "opencode" if it's in PATH.
    /// </summary>
    public string ExecutablePath { get; set; } = "opencode";

    /// <summary>
    /// Base port for OpenCode servers. Each server gets an incremented port.
    /// </summary>
    public int BasePort { get; set; } = 4096;

    /// <summary>
    /// Maximum number of concurrent OpenCode servers allowed.
    /// </summary>
    public int MaxConcurrentServers { get; set; } = 10;

    /// <summary>
    /// Timeout in milliseconds for waiting for a server to start.
    /// </summary>
    public int ServerStartTimeoutMs { get; set; } = 15000;

    /// <summary>
    /// Interval in milliseconds for health check polling.
    /// </summary>
    public int HealthCheckIntervalMs { get; set; } = 30000;

    /// <summary>
    /// Default model to use when not specified by project or session.
    /// Format: "provider/model" (e.g., "anthropic/claude-opus-4-5")
    /// </summary>
    public string DefaultModel { get; set; } = "anthropic/claude-opus-4-5";

    /// <summary>
    /// External hostname for agent URLs when running in container mode with Tailscale.
    /// When set, agent web view links will use this hostname instead of localhost.
    /// Format: "hostname.tailnet.ts.net" (without http:// prefix)
    /// Can be set via HSP_EXTERNAL_HOSTNAME environment variable.
    /// </summary>
    public string? ExternalHostname { get; set; }
}
