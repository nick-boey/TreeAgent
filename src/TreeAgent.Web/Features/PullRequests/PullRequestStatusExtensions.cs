namespace TreeAgent.Web.Features.PullRequests;

public static class PullRequestStatusExtensions
{
    /// <summary>
    /// Gets the color associated with a PR status for UI display.
    /// </summary>
    public static string GetColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.InProgress => "yellow",
        PullRequestStatus.ReadyForReview => "yellow-flashing",
        PullRequestStatus.ChecksFailing => "red",
        PullRequestStatus.Conflict => "orange",
        PullRequestStatus.ReadyForMerging => "green",
        PullRequestStatus.Merged => "purple",
        PullRequestStatus.Closed => "red",
        _ => "gray"
    };

    /// <summary>
    /// Gets a human-readable description of the PR status.
    /// </summary>
    public static string GetDescription(PullRequestStatus status) => status switch
    {
        PullRequestStatus.InProgress => "Agent is actively working on the PR",
        PullRequestStatus.ReadyForReview => "Agent completed, awaiting user review",
        PullRequestStatus.ChecksFailing => "CI/CD checks have failed",
        PullRequestStatus.Conflict => "Rebase failed due to merge conflicts",
        PullRequestStatus.ReadyForMerging => "Approved and ready to merge",
        PullRequestStatus.Merged => "PR has been merged",
        PullRequestStatus.Closed => "PR was closed without merging",
        _ => "Unknown status"
    };

    /// <summary>
    /// Returns true if this is an "open" (active) PR status.
    /// </summary>
    public static bool IsOpen(PullRequestStatus status) => status switch
    {
        PullRequestStatus.InProgress => true,
        PullRequestStatus.ReadyForReview => true,
        PullRequestStatus.ChecksFailing => true,
        PullRequestStatus.Conflict => true,
        PullRequestStatus.ReadyForMerging => true,
        _ => false
    };

    /// <summary>
    /// Returns true if this is a "closed" (completed) PR status.
    /// </summary>
    public static bool IsClosed(PullRequestStatus status) => status switch
    {
        PullRequestStatus.Merged => true,
        PullRequestStatus.Closed => true,
        _ => false
    };
}