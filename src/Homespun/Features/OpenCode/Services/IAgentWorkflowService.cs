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
    /// Creates the branch and worktree (but NOT a PR record), then starts the agent.
    /// The FutureChange remains in the roadmap with InProgress status until the agent
    /// creates a GitHub PR, at which point it's promoted to a tracked PR.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID (branch name)</param>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The agent status including server and session info</returns>
    Task<AgentStatus> StartAgentForFutureChangeAsync(string projectId, string changeId, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Stops the agent for an entity (PR or FutureChange).
    /// Also handles agent completion by checking for GitHub PR and transitioning appropriately.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID or change ID)</param>
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
    /// Gets the current agent status for a FutureChange by its change ID.
    /// </summary>
    /// <param name="changeId">The change ID (branch name)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent status or null if no agent is running</returns>
    Task<AgentStatus?> GetAgentStatusForChangeAsync(string changeId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt to the agent's active session.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID or change ID)</param>
    /// <param name="prompt">The prompt text</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The response message</returns>
    Task<OpenCodeMessage> SendPromptAsync(string entityId, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Handles agent completion for a FutureChange.
    /// Checks GitHub for a PR on the branch and either promotes to tracked PR or transitions to AwaitingPR.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The change ID (branch name)</param>
    /// <param name="ct">Cancellation token</param>
    Task HandleAgentCompletionAsync(string projectId, string changeId, CancellationToken ct = default);
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
