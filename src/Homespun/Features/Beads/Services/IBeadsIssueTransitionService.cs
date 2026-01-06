using Homespun.Features.Beads.Data;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for managing status transitions for beads issues.
/// Coordinates between beads updates, agent workflow, and SignalR notifications.
/// </summary>
public interface IBeadsIssueTransitionService
{
    /// <summary>
    /// Transitions an issue to InProgress status when an agent starts working on it.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <returns>The transition result</returns>
    Task<BeadsTransitionResult> TransitionToInProgressAsync(string projectId, string issueId);

    /// <summary>
    /// Transitions an issue to AwaitingPR status when the agent finishes without creating a PR.
    /// This indicates the issue is waiting for a PR to be created manually or by resuming the agent.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <returns>The transition result</returns>
    Task<BeadsTransitionResult> TransitionToAwaitingPRAsync(string projectId, string issueId);

    /// <summary>
    /// Transitions an issue to Complete (Closed) status when the PR is created.
    /// Adds a label linking the issue to the PR.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="prNumber">The GitHub PR number to link</param>
    /// <returns>The transition result</returns>
    Task<BeadsTransitionResult> TransitionToCompleteAsync(string projectId, string issueId, int? prNumber = null);

    /// <summary>
    /// Handles an agent failure by reverting the issue to Open status.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <param name="error">The error message describing the failure</param>
    /// <returns>The transition result</returns>
    Task<BeadsTransitionResult> HandleAgentFailureAsync(string projectId, string issueId, string error);

    /// <summary>
    /// Gets the current status of an issue.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The beads issue ID</param>
    /// <returns>The current status or null if the issue doesn't exist</returns>
    Task<BeadsIssueStatus?> GetStatusAsync(string projectId, string issueId);
}

/// <summary>
/// Result of a beads issue status transition operation.
/// </summary>
public class BeadsTransitionResult
{
    public bool Success { get; init; }
    public BeadsIssueStatus? PreviousStatus { get; init; }
    public BeadsIssueStatus? NewStatus { get; init; }
    public string? Error { get; init; }
    public int? PrNumber { get; init; }

    public static BeadsTransitionResult Ok(BeadsIssueStatus previousStatus, BeadsIssueStatus newStatus, int? prNumber = null)
        => new() { Success = true, PreviousStatus = previousStatus, NewStatus = newStatus, PrNumber = prNumber };

    public static BeadsTransitionResult Fail(string error, BeadsIssueStatus? currentStatus = null)
        => new() { Success = false, Error = error, PreviousStatus = currentStatus };
}
