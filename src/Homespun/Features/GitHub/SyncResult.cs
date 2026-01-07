namespace Homespun.Features.GitHub;

/// <summary>
/// Result of a sync operation
/// </summary>
public class SyncResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public List<string> Errors { get; set; } = [];
    
    /// <summary>
    /// PRs that were removed from local tracking (merged/closed on GitHub).
    /// Contains (PullRequestId, BeadsIssueId, GitHubPrNumber) for each removed PR.
    /// Used to close linked beads issues.
    /// </summary>
    public List<RemovedPrInfo> RemovedPrs { get; set; } = [];
}

/// <summary>
/// Information about a PR that was removed from local tracking.
/// </summary>
public class RemovedPrInfo
{
    public required string PullRequestId { get; set; }
    public string? BeadsIssueId { get; set; }
    public int? GitHubPrNumber { get; set; }
}