namespace Homespun.Features.PullRequests.Data.Entities;

/// <summary>
/// Status of locally tracked pull requests. Only open PRs are tracked in the database.
/// Merged/Cancelled PRs are removed from local tracking (retrieve from GitHub if needed).
/// </summary>
public enum OpenPullRequestStatus
{
    /// <summary>Work is in progress - agent actively working or awaiting agent start</summary>
    InDevelopment,

    /// <summary>Ready for code review on GitHub</summary>
    ReadyForReview
}
