namespace Homespun.Features.Agents;

/// <summary>
/// Global configuration options for agents.
/// </summary>
public class AgentOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Default harness type to use when not specified.
    /// </summary>
    public string DefaultHarnessType { get; set; } = "opencode";

    /// <summary>
    /// External hostname for agent web UI URLs.
    /// If set, this hostname is used instead of localhost for generating external URLs.
    /// </summary>
    public string? ExternalHostname { get; set; }
}
