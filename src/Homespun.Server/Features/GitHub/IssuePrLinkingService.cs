using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for linking Fleece issues to pull requests.
/// Handles the link from PR.FleeceIssueId to the issue.
/// Uses IProjectFleeceService for direct file access.
/// </summary>
public class IssuePrLinkingService(
    IDataStore dataStore,
    IProjectFleeceService fleeceService,
    ILogger<IssuePrLinkingService> logger)
    : IIssuePrLinkingService
{
    /// <summary>
    /// Links a pull request to a Fleece issue by setting the FleeceIssueId.
    /// Note: Fleece.Core doesn't support a LinkedPR property on issues, so only the PR side is updated.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="issueId">The Fleece issue ID to link.</param>
    /// <param name="prNumber">The GitHub PR number (unused, kept for API compatibility).</param>
    /// <returns>True if linking succeeded, false otherwise.</returns>
    public async Task<bool> LinkPullRequestToIssueAsync(
        string projectId,
        string pullRequestId,
        string issueId,
        int prNumber)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot link PR to issue: project {ProjectId} not found", projectId);
            return false;
        }

        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot link PR to issue: pull request {PullRequestId} not found", pullRequestId);
            return false;
        }

        // Check if already linked to avoid duplicate operations
        if (!string.IsNullOrEmpty(pullRequest.FleeceIssueId))
        {
            logger.LogDebug("PR {PullRequestId} already linked to issue {IssueId}", pullRequestId, pullRequest.FleeceIssueId);
            return true;
        }

        // Update the pull request with the issue ID
        pullRequest.FleeceIssueId = issueId;
        pullRequest.UpdatedAt = DateTime.UtcNow;
        await dataStore.UpdatePullRequestAsync(pullRequest);

        logger.LogInformation("Linked PR {PullRequestId} to issue {IssueId}", pullRequestId, issueId);

        return true;
    }

    /// <summary>
    /// Attempts to link a pull request to a Fleece issue by extracting the issue ID
    /// from the branch name.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <returns>The linked issue ID if successful, null otherwise.</returns>
    public async Task<string?> TryLinkByBranchNameAsync(string projectId, string pullRequestId)
    {
        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot link by branch: pull request {PullRequestId} not found", pullRequestId);
            return null;
        }

        // If already linked, return the existing issue ID
        if (!string.IsNullOrEmpty(pullRequest.FleeceIssueId))
        {
            logger.LogDebug("PR {PullRequestId} already linked to issue {IssueId}", pullRequestId, pullRequest.FleeceIssueId);
            return pullRequest.FleeceIssueId;
        }

        // Can't link without a PR number (needed for the LinkedPR)
        if (!pullRequest.GitHubPRNumber.HasValue)
        {
            logger.LogDebug("Cannot link PR {PullRequestId} by branch: no GitHub PR number", pullRequestId);
            return null;
        }

        // Extract issue ID from branch name
        var issueId = BranchNameParser.ExtractIssueId(pullRequest.BranchName);
        if (string.IsNullOrEmpty(issueId))
        {
            logger.LogDebug("No issue ID found in branch name {BranchName}", pullRequest.BranchName);
            return null;
        }

        // Link the PR to the issue
        var linked = await LinkPullRequestToIssueAsync(projectId, pullRequestId, issueId, pullRequest.GitHubPRNumber.Value);
        return linked ? issueId : null;
    }

    /// <summary>
    /// Closes the Fleece issue linked to a pull request.
    /// Used when a PR is merged or closed.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="reason">The reason for closing (unused in Fleece, kept for API compatibility).</param>
    /// <returns>True if the issue was closed, false if no linked issue or close failed.</returns>
    public async Task<bool> CloseLinkedIssueAsync(string projectId, string pullRequestId, string? reason = null)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot close linked issue: project {ProjectId} not found", projectId);
            return false;
        }

        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot close linked issue: pull request {PullRequestId} not found", pullRequestId);
            return false;
        }

        if (string.IsNullOrEmpty(pullRequest.FleeceIssueId))
        {
            logger.LogDebug("PR {PullRequestId} has no linked issue to close", pullRequestId);
            return false;
        }

        // Close the issue by setting status to Closed — PR-driven, not a user undo step.
        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            pullRequest.FleeceIssueId,
            status: IssueStatus.Closed,
            recordUndo: false);

        if (updated != null)
        {
            logger.LogInformation("Closed issue {IssueId} linked to PR {PullRequestId}", pullRequest.FleeceIssueId, pullRequestId);
            return true;
        }

        logger.LogWarning("Failed to close issue {IssueId} linked to PR {PullRequestId}", pullRequest.FleeceIssueId, pullRequestId);
        return false;
    }

    /// <summary>
    /// Updates a Fleece issue status based on the associated PR status.
    /// </summary>
    public async Task<bool> UpdateIssueStatusFromPRAsync(
        string projectId,
        string issueId,
        PullRequestStatus prStatus,
        int prNumber)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot update issue status: project {ProjectId} not found", projectId);
            return false;
        }

        // Get the current issue to check its status
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            logger.LogWarning("Cannot update issue status: issue {IssueId} not found", issueId);
            return false;
        }

        // Map PR status to issue status
        var targetIssueStatus = MapPrStatusToIssueStatus(prStatus);

        // Idempotency check - skip if already in the correct status
        if (issue.Status == targetIssueStatus)
        {
            logger.LogDebug("Issue {IssueId} already in status {Status}, skipping update", issueId, targetIssueStatus);
            return false;
        }

        // Update the issue status — PR-driven, not a user undo step.
        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            status: targetIssueStatus,
            recordUndo: false);

        if (updated != null)
        {
            logger.LogInformation("Updated issue {IssueId} to status {Status} based on PR #{PrNumber}", issueId, targetIssueStatus, prNumber);
            return true;
        }

        logger.LogWarning("Failed to update issue {IssueId} to status {Status}", issueId, targetIssueStatus);
        return false;
    }

    /// <summary>
    /// Maps a PR status to the corresponding issue status.
    /// </summary>
    private static IssueStatus MapPrStatusToIssueStatus(PullRequestStatus prStatus)
    {
        return prStatus switch
        {
            PullRequestStatus.Merged => IssueStatus.Complete,
            PullRequestStatus.Closed => IssueStatus.Closed,
            // All open PR states map to Review
            _ => IssueStatus.Review
        };
    }
}
