namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// Represents a running agent instance.
/// </summary>
public class AgentInstance
{
    /// <summary>
    /// Internal agent ID (may differ from EntityId for some harnesses).
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// The entity this agent is associated with (PR ID, issue ID, etc.).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Harness type that created this instance.
    /// </summary>
    public required string HarnessType { get; init; }

    /// <summary>
    /// Current status of the agent.
    /// </summary>
    public AgentInstanceStatus Status { get; set; } = AgentInstanceStatus.Starting;

    /// <summary>
    /// URL for the agent's web UI (if available).
    /// </summary>
    public string? WebViewUrl { get; init; }

    /// <summary>
    /// Base URL for API communication (internal use).
    /// </summary>
    public string? ApiBaseUrl { get; init; }

    /// <summary>
    /// Active session ID (if the harness uses sessions).
    /// </summary>
    public string? ActiveSessionId { get; set; }

    /// <summary>
    /// Working directory.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// When the agent was started.
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Harness-specific metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}
