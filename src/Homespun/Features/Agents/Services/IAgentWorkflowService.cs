using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Beads.Data;

namespace Homespun.Features.Agents.Services;

/// <summary>
/// High-level orchestration service for agent workflows.
/// Supports multiple harness types for different AI backends.
/// </summary>
public interface IAgentWorkflowService
{
    /// <summary>
    /// Starts an agent for an existing pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="model">Optional model override</param>
    /// <param name="harnessType">Optional harness type (defaults to configured default)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    Task<WorkflowAgentStatus> StartAgentForPullRequestAsync(
        string pullRequestId,
        string? model = null,
        string? harnessType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Starts an agent for a beads issue.
    /// Creates the branch and worktree, then starts the agent.
    /// The issue must have an hsp: label (e.g., hsp:frontend/-/update-page) for branch naming.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID (e.g., "bd-a3f8")</param>
    /// <param name="agentMode">The mode to start the agent in (Planning or Building)</param>
    /// <param name="model">Optional model override</param>
    /// <param name="harnessType">Optional harness type (defaults to configured default)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    Task<WorkflowAgentStatus> StartAgentForBeadsIssueAsync(
        string projectId,
        string issueId,
        AgentMode agentMode = AgentMode.Building,
        string? model = null,
        string? harnessType = null,
        CancellationToken ct = default);

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
    Task<WorkflowAgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent status for a beads issue.
    /// </summary>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent status or null if no agent is running</returns>
    Task<WorkflowAgentStatus?> GetAgentStatusForBeadsIssueAsync(string issueId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the agent's active session.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID, change ID, or beads issue ID)</param>
    /// <param name="prompt">The prompt text</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The response message</returns>
    Task<AgentMessage> SendPromptAsync(string entityId, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Handles agent completion for a beads issue.
    /// Checks GitHub for a PR on the branch and either creates a tracked PR (closing the issue)
    /// or transitions to AwaitingPR status.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="ct">Cancellation token</param>
    Task HandleAgentCompletionForBeadsIssueAsync(string projectId, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Gets all running agents across all harnesses.
    /// </summary>
    IReadOnlyList<AgentInstance> GetAllRunningAgents();

    /// <summary>
    /// Gets the harness type for an entity if an agent is running.
    /// </summary>
    /// <param name="entityId">The entity ID</param>
    /// <returns>The harness type, or null if no agent is running</returns>
    string? GetHarnessTypeForEntity(string entityId);
}

/// <summary>
/// Represents the current status of an agent for a pull request or future change.
/// </summary>
public class WorkflowAgentStatus
{
    /// <summary>
    /// The entity ID this agent is for (PR ID or change ID/branch name).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The agent instance.
    /// </summary>
    public required AgentInstance Agent { get; init; }

    /// <summary>
    /// The harness type managing this agent.
    /// </summary>
    public required string HarnessType { get; init; }
}

/// <summary>
/// Agent mode for beads issue workflows.
/// </summary>
public enum AgentMode
{
    /// <summary>
    /// Planning mode - ask clarifying questions and create plans.
    /// </summary>
    Planning,

    /// <summary>
    /// Building mode - implement changes and create PRs.
    /// </summary>
    Building
}
