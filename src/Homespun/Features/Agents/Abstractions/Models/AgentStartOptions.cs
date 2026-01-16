namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// Options for starting an agent.
/// </summary>
public class AgentStartOptions
{
    /// <summary>
    /// Unique identifier linking this agent to an entity (PR ID, issue ID, etc.).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The working directory for the agent.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Optional title for the agent session.
    /// </summary>
    public string? SessionTitle { get; init; }

    /// <summary>
    /// Optional model override (e.g., "anthropic/claude-opus-4-5").
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Whether to continue an existing session if available.
    /// </summary>
    public bool ContinueSession { get; init; }

    /// <summary>
    /// Optional initial prompt to send after starting.
    /// </summary>
    public AgentPrompt? InitialPrompt { get; init; }

    /// <summary>
    /// Harness-specific configuration (key-value pairs).
    /// </summary>
    public Dictionary<string, object> HarnessConfig { get; init; } = [];
}
