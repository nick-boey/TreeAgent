namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// Information about a running agent for SignalR broadcast.
/// </summary>
public class RunningAgentInfo
{
    /// <summary>
    /// The entity ID this agent is for.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The harness type managing this agent.
    /// </summary>
    public required string HarnessType { get; init; }

    /// <summary>
    /// Port number (if applicable).
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// Base URL for API access.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Working directory path.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// When the agent was started.
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// Active session ID.
    /// </summary>
    public string? ActiveSessionId { get; init; }

    /// <summary>
    /// URL to open the agent's web view.
    /// </summary>
    public string? WebViewUrl { get; init; }

    /// <summary>
    /// Current agent status.
    /// </summary>
    public AgentInstanceStatus Status { get; init; }

    /// <summary>
    /// Creates a RunningAgentInfo from an AgentInstance.
    /// </summary>
    public static RunningAgentInfo FromAgentInstance(AgentInstance agent)
    {
        return new RunningAgentInfo
        {
            EntityId = agent.EntityId,
            HarnessType = agent.HarnessType,
            Port = agent.Metadata.TryGetValue("port", out var port) ? (int?)port : null,
            BaseUrl = agent.ApiBaseUrl,
            WorkingDirectory = agent.WorkingDirectory,
            StartedAt = agent.StartedAt,
            ActiveSessionId = agent.ActiveSessionId,
            WebViewUrl = agent.WebViewUrl,
            Status = agent.Status
        };
    }
}
