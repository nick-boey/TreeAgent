using Homespun.Features.PullRequests;

namespace Homespun.Features.GitHub;

/// <summary>
/// Data transfer object containing PR status information for an issue.
/// </summary>
public class IssuePullRequestStatus
{
    /// <summary>
    /// The GitHub PR number.
    /// </summary>
    public int PrNumber { get; set; }

    /// <summary>
    /// URL to the PR on GitHub.
    /// </summary>
    public required string PrUrl { get; set; }

    /// <summary>
    /// The branch name associated with the PR.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Current status of the PR (InProgress, ReadyForReview, ChecksFailing, etc.).
    /// </summary>
    public PullRequestStatus Status { get; set; }

    /// <summary>
    /// Whether CI checks are passing. Null if no checks configured.
    /// </summary>
    public bool? ChecksPassing { get; set; }

    /// <summary>
    /// Whether the PR has been approved by reviewers.
    /// </summary>
    public bool? IsApproved { get; set; }

    /// <summary>
    /// Number of approvals on this PR.
    /// </summary>
    public int ApprovalCount { get; set; }

    /// <summary>
    /// Number of pending review requests (changes requested).
    /// </summary>
    public int ChangesRequestedCount { get; set; }

    /// <summary>
    /// Whether the PR is ready to merge (approved, checks passing, no conflicts).
    /// </summary>
    public bool IsMergeable => Status == PullRequestStatus.ReadyForMerging;

    /// <summary>
    /// Whether checks are currently running (not yet passed or failed).
    /// </summary>
    public bool ChecksRunning => ChecksPassing == null && Status == PullRequestStatus.InProgress;

    /// <summary>
    /// Whether checks are failing.
    /// </summary>
    public bool ChecksFailing => Status == PullRequestStatus.ChecksFailing || ChecksPassing == false;

    /// <summary>
    /// Whether there are merge conflicts.
    /// </summary>
    public bool HasConflicts => Status == PullRequestStatus.Conflict;
}
