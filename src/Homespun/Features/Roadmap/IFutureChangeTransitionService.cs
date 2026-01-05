namespace Homespun.Features.Roadmap;

/// <summary>
/// Service for managing status transitions for future changes in the roadmap.
/// Coordinates between roadmap updates, agent workflow, and SignalR notifications.
/// </summary>
public interface IFutureChangeTransitionService
{
    /// <summary>
    /// Transitions a change to InProgress status when an agent starts working on it.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <returns>True if the transition was successful</returns>
    Task<TransitionResult> TransitionToInProgressAsync(string projectId, string changeId);

    /// <summary>
    /// Transitions a change to AwaitingPR status when the agent finishes without creating a PR.
    /// This indicates the change is waiting for a PR to be created manually or by resuming the agent.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <returns>True if the transition was successful</returns>
    Task<TransitionResult> TransitionToAwaitingPRAsync(string projectId, string changeId);

    /// <summary>
    /// Transitions a change to Complete status when the PR is merged.
    /// Also removes the change from other changes' parent lists.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <returns>True if the transition was successful</returns>
    Task<TransitionResult> TransitionToCompleteAsync(string projectId, string changeId);

    /// <summary>
    /// Handles an agent failure by reverting the change to Pending status.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <param name="error">The error message describing the failure</param>
    /// <returns>True if the transition was successful</returns>
    Task<TransitionResult> HandleAgentFailureAsync(string projectId, string changeId, string error);

    /// <summary>
    /// Gets the current status of a change.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The roadmap change ID</param>
    /// <returns>The current status or null if the change doesn't exist</returns>
    Task<FutureChangeStatus?> GetStatusAsync(string projectId, string changeId);
}

/// <summary>
/// Result of a status transition operation.
/// </summary>
public class TransitionResult
{
    public bool Success { get; init; }
    public FutureChangeStatus? PreviousStatus { get; init; }
    public FutureChangeStatus? NewStatus { get; init; }
    public string? Error { get; init; }
    public int? PrNumber { get; init; }

    public static TransitionResult Ok(FutureChangeStatus previousStatus, FutureChangeStatus newStatus, int? prNumber = null)
        => new() { Success = true, PreviousStatus = previousStatus, NewStatus = newStatus, PrNumber = prNumber };

    public static TransitionResult Fail(string error, FutureChangeStatus? currentStatus = null)
        => new() { Success = false, Error = error, PreviousStatus = currentStatus };
}
