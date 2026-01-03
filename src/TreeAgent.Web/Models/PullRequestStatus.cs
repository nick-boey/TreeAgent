namespace TreeAgent.Web.Models;

/// <summary>
/// Status of a pull request in the workflow.
/// Note: This differs from FeatureStatus which is used for features/tasks.
/// </summary>
public enum PullRequestStatus
{
    /// <summary>Agent is actively working on the PR</summary>
    InProgress,

    /// <summary>Agent completed, awaiting user review</summary>
    ReadyForReview,

    /// <summary>CI/CD checks have failed</summary>
    ChecksFailing,

    /// <summary>Rebase failed due to merge conflicts</summary>
    Conflict,

    /// <summary>Approved and ready to merge</summary>
    ReadyForMerging,

    /// <summary>PR has been merged (past)</summary>
    Merged,

    /// <summary>PR was closed without merging (past)</summary>
    Closed
}

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
