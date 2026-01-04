using Homespun.Features.OpenCode.Models;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// High-level orchestration service for agent workflows.
/// </summary>
public interface IAgentWorkflowService
{
    /// <summary>
    /// Starts an agent for an existing pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    Task<AgentStatus> StartAgentForPullRequestAsync(string pullRequestId, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Starts an agent for a future roadmap change.
    /// Creates the branch, worktree, and PR record, then starts the agent.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    Task<AgentStatus> StartAgentForFutureChangeAsync(string projectId, string changeId, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Stops the agent for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="ct">Cancellation token</param>
    Task StopAgentAsync(string pullRequestId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent status for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent status or null if no agent is running</returns>
    Task<AgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the agent's active session.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="prompt">The prompt text</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The response message</returns>
    Task<OpenCodeMessage> SendPromptAsync(string pullRequestId, string prompt, CancellationToken ct = default);
}

/// <summary>
/// Represents the current status of an agent for a pull request.
/// </summary>
public class AgentStatus
{
    public required string PullRequestId { get; init; }
    public required OpenCodeServer Server { get; init; }
    public OpenCodeSession? ActiveSession { get; set; }
    public List<OpenCodeSession> Sessions { get; set; } = [];
}
