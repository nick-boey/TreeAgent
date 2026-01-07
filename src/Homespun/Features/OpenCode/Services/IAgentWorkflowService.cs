using Homespun.Features.Beads.Data;
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
    /// Starts an agent for a beads issue.
    /// Creates the branch and worktree, then starts the agent.
    /// The issue must have an hsp: label (e.g., hsp:frontend/-/update-page) for branch naming.
    /// The issue transitions to InProgress status until the agent creates a GitHub PR,
    /// at which point the issue is closed and a tracked PR is created.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID (e.g., "bd-a3f8")</param>
    /// <param name="agentMode">The mode to start the agent in (Planning or Building)</param>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    /// <exception cref="InvalidOperationException">Thrown if the issue does not have an hsp: label</exception>
    Task<AgentStatus> StartAgentForBeadsIssueAsync(string projectId, string issueId, AgentMode agentMode = AgentMode.Building, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Stops the agent for an entity (PR, FutureChange, or Beads Issue).
    /// Also handles agent completion by checking for GitHub PR and transitioning appropriately.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID, change ID, or beads issue ID)</param>
    /// <param name="ct">Cancellation token</param>
    Task StopAgentAsync(string entityId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent status for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent status or null if no agent is running</returns>
    Task<AgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent status for a beads issue.
    /// </summary>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent status or null if no agent is running</returns>
    Task<AgentStatus?> GetAgentStatusForBeadsIssueAsync(string issueId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the agent's active session.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID, change ID, or beads issue ID)</param>
    /// <param name="prompt">The prompt text</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The response message</returns>
    Task<OpenCodeMessage> SendPromptAsync(string entityId, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Handles agent completion for a beads issue.
    /// Checks GitHub for a PR on the branch and either creates a tracked PR (closing the issue)
    /// or transitions to AwaitingPR status.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="ct">Cancellation token</param>
    Task HandleAgentCompletionForBeadsIssueAsync(string projectId, string issueId, CancellationToken ct = default);
}

/// <summary>
/// Represents the current status of an agent for a pull request or future change.
/// </summary>
public class AgentStatus
{
    /// <summary>
    /// The entity ID this agent is for (PR ID or change ID/branch name).
    /// </summary>
    public required string EntityId { get; init; }
    public required OpenCodeServer Server { get; init; }
    public OpenCodeSession? ActiveSession { get; set; }
    public List<OpenCodeSession> Sessions { get; set; } = [];
}
