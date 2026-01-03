namespace TreeAgent.Web.Features.PullRequests;

/// <summary>
/// Represents GitHub PR data. This is a read model - not persisted to EF Core.
/// The source of truth for PR data is GitHub.
/// Used by both GitHubService and PullRequestWorkflowService.
/// </summary>
public class PullRequestInfo
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public required PullRequestStatus Status { get; set; }
    public string? BranchName { get; set; }
    public string? HtmlUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Group extracted from branch name (e.g., "core" from "core/feature/pr-time-dimension")
    /// </summary>
    public string? Group => ExtractGroup(BranchName);

    /// <summary>
    /// Type extracted from branch name (e.g., "feature" from "core/feature/pr-time-dimension")
    /// </summary>
    public string? Type => ExtractType(BranchName);

    /// <summary>
    /// Whether CI checks are passing.
    /// </summary>
    public bool? ChecksPassing { get; set; }

    /// <summary>
    /// Whether the PR has been approved by reviewers.
    /// </summary>
    public bool? IsApproved { get; set; }

    /// <summary>
    /// Whether the PR has unresolved review comments.
    /// </summary>
    public bool HasUnresolvedComments { get; set; }

    /// <summary>
    /// Validates that the PR state is consistent.
    /// </summary>
    public bool IsValid()
    {
        // Merged PRs must have a MergedAt timestamp
        if (Status == PullRequestStatus.Merged && MergedAt == null)
            return false;

        // Open PRs should not have a MergedAt timestamp
        if (PullRequestStatusExtensions.IsOpen(Status) && MergedAt != null)
            return false;

        return true;
    }

    private static string? ExtractGroup(string? branchName)
    {
        if (string.IsNullOrEmpty(branchName))
            return null;

        var parts = branchName.Split('/');
        return parts.Length >= 2 ? parts[0] : null;
    }

    private static string? ExtractType(string? branchName)
    {
        if (string.IsNullOrEmpty(branchName))
            return null;

        var parts = branchName.Split('/');
        return parts.Length >= 2 ? parts[1] : null;
    }
}
