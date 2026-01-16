using Homespun.Features.Beads.Services;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for linking beads issues to pull requests.
/// Handles the bidirectional link: PR.BeadsIssueId and issue label hsp:pr-{number}.
/// Uses direct SQLite access via IBeadsDatabaseService for performance.
/// </summary>
public class IssuePrLinkingService(
    IDataStore dataStore,
    IBeadsDatabaseService beadsDatabaseService,
    ILogger<IssuePrLinkingService> logger)
    : IIssuePrLinkingService
{
    /// <summary>
    /// Links a pull request to a beads issue by setting the BeadsIssueId
    /// and adding the hsp:pr-{prNumber} label to the issue.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="issueId">The beads issue ID to link.</param>
    /// <param name="prNumber">The GitHub PR number for the label.</param>
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
        if (!string.IsNullOrEmpty(pullRequest.BeadsIssueId))
        {
            logger.LogDebug("PR {PullRequestId} already linked to issue {IssueId}", pullRequestId, pullRequest.BeadsIssueId);
            return true;
        }

        // Update the pull request with the issue ID
        pullRequest.BeadsIssueId = issueId;
        pullRequest.UpdatedAt = DateTime.UtcNow;
        await dataStore.UpdatePullRequestAsync(pullRequest);

        logger.LogInformation("Linked PR {PullRequestId} to beads issue {IssueId}", pullRequestId, issueId);

        // Add the label to the beads issue (updates cache immediately, queues DB write)
        var label = BranchNameParser.GetPrLabel(prNumber);
        var labelAdded = await beadsDatabaseService.AddLabelAsync(project.LocalPath, issueId, label);

        if (!labelAdded)
        {
            logger.LogWarning("Failed to add label {Label} to issue {IssueId}, but PR link was created locally", label, issueId);
        }
        else
        {
            logger.LogInformation("Added label {Label} to beads issue {IssueId}", label, issueId);
        }

        return true;
    }

    /// <summary>
    /// Attempts to link a pull request to a beads issue by extracting the issue ID
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
        if (!string.IsNullOrEmpty(pullRequest.BeadsIssueId))
        {
            logger.LogDebug("PR {PullRequestId} already linked to issue {IssueId}", pullRequestId, pullRequest.BeadsIssueId);
            return pullRequest.BeadsIssueId;
        }

        // Can't link without a PR number (needed for the label)
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
    /// Closes the beads issue linked to a pull request.
    /// Used when a PR is merged or closed.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="reason">The reason for closing (e.g., "PR #123 merged").</param>
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

        if (string.IsNullOrEmpty(pullRequest.BeadsIssueId))
        {
            logger.LogDebug("PR {PullRequestId} has no linked issue to close", pullRequestId);
            return false;
        }

        var closed = await beadsDatabaseService.CloseIssueAsync(project.LocalPath, pullRequest.BeadsIssueId, reason);

        if (closed)
        {
            logger.LogInformation("Closed beads issue {IssueId} linked to PR {PullRequestId}", pullRequest.BeadsIssueId, pullRequestId);
        }
        else
        {
            logger.LogWarning("Failed to close beads issue {IssueId} linked to PR {PullRequestId}", pullRequest.BeadsIssueId, pullRequestId);
        }

        return closed;
    }
}
