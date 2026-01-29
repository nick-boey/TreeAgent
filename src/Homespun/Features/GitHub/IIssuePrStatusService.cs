namespace Homespun.Features.GitHub;

/// <summary>
/// Service for retrieving PR status information for issues.
/// </summary>
public interface IIssuePrStatusService
{
    /// <summary>
    /// Gets the PR status for an issue by looking up linked PRs.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="issueId">The issue ID to find PR status for.</param>
    /// <returns>PR status if a linked PR exists, null otherwise.</returns>
    Task<IssuePullRequestStatus?> GetPullRequestStatusForIssueAsync(string projectId, string issueId);
}
