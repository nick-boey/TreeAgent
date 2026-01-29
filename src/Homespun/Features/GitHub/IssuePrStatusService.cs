using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for retrieving PR status information for issues.
/// </summary>
public class IssuePrStatusService(
    IDataStore dataStore,
    PullRequestWorkflowService workflowService,
    ILogger<IssuePrStatusService> logger)
    : IIssuePrStatusService
{
    /// <inheritdoc />
    public async Task<IssuePullRequestStatus?> GetPullRequestStatusForIssueAsync(string projectId, string issueId)
    {
        // Find tracked PRs linked to this issue
        var linkedPr = dataStore.GetPullRequestsByProject(projectId)
            .FirstOrDefault(pr => pr.BeadsIssueId == issueId && pr.GitHubPRNumber.HasValue);

        if (linkedPr == null)
        {
            logger.LogDebug("No linked PR found for issue {IssueId} in project {ProjectId}", issueId, projectId);
            return null;
        }

        var project = dataStore.GetProject(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogWarning("Cannot get PR status: project {ProjectId} not found or missing GitHub config", projectId);
            return null;
        }

        try
        {
            // Get open PRs with status from GitHub
            var openPrsWithStatus = await workflowService.GetOpenPullRequestsWithStatusAsync(projectId);
            var prWithStatus = openPrsWithStatus.FirstOrDefault(p => p.PullRequest.Number == linkedPr.GitHubPRNumber);

            if (prWithStatus != null)
            {
                return new IssuePullRequestStatus
                {
                    PrNumber = prWithStatus.PullRequest.Number,
                    PrUrl = prWithStatus.PullRequest.HtmlUrl ?? $"https://github.com/{project.GitHubOwner}/{project.GitHubRepo}/pull/{prWithStatus.PullRequest.Number}",
                    BranchName = prWithStatus.PullRequest.BranchName,
                    Status = prWithStatus.Status,
                    ChecksPassing = prWithStatus.PullRequest.ChecksPassing,
                    IsApproved = prWithStatus.PullRequest.IsApproved,
                    ApprovalCount = prWithStatus.PullRequest.ApprovalCount,
                    ChangesRequestedCount = prWithStatus.PullRequest.ChangesRequestedCount
                };
            }

            // PR might be closed/merged - return basic info from local tracking
            logger.LogDebug("PR #{PrNumber} not found in open PRs, may be closed/merged", linkedPr.GitHubPRNumber);
            return new IssuePullRequestStatus
            {
                PrNumber = linkedPr.GitHubPRNumber!.Value,
                PrUrl = $"https://github.com/{project.GitHubOwner}/{project.GitHubRepo}/pull/{linkedPr.GitHubPRNumber}",
                BranchName = linkedPr.BranchName,
                Status = PullRequestStatus.Merged, // Assume merged if not in open list
                ChecksPassing = null,
                IsApproved = null,
                ApprovalCount = 0,
                ChangesRequestedCount = 0
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get PR status for issue {IssueId}", issueId);

            // Return basic info from local tracking on error
            return new IssuePullRequestStatus
            {
                PrNumber = linkedPr.GitHubPRNumber!.Value,
                PrUrl = $"https://github.com/{project.GitHubOwner}/{project.GitHubRepo}/pull/{linkedPr.GitHubPRNumber}",
                BranchName = linkedPr.BranchName,
                Status = PullRequestStatus.InProgress,
                ChecksPassing = null,
                IsApproved = null,
                ApprovalCount = 0,
                ChangesRequestedCount = 0
            };
        }
    }
}
