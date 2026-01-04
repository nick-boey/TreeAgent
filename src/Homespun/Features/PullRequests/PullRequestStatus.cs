namespace Homespun.Features.PullRequests;

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