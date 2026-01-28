using Fleece.Core.Models;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IIssuePrLinkingService.
/// </summary>
public class MockIssuePrLinkingService : IIssuePrLinkingService
{
    private readonly IDataStore _dataStore;
    private readonly MockFleeceService _fleeceService;
    private readonly ILogger<MockIssuePrLinkingService> _logger;

    public MockIssuePrLinkingService(
        IDataStore dataStore,
        MockFleeceService fleeceService,
        ILogger<MockIssuePrLinkingService> logger)
    {
        _dataStore = dataStore;
        _fleeceService = fleeceService;
        _logger = logger;
    }

    public async Task<bool> LinkPullRequestToIssueAsync(
        string projectId,
        string pullRequestId,
        string issueId,
        int prNumber)
    {
        _logger.LogDebug("[Mock] LinkPullRequestToIssue PR:{PullRequestId} to Issue:{IssueId} with GitHub PR #{PrNumber}",
            pullRequestId, issueId, prNumber);

        var pr = _dataStore.GetPullRequest(pullRequestId);
        if (pr == null)
        {
            _logger.LogWarning("[Mock] Pull request {PullRequestId} not found", pullRequestId);
            return false;
        }

        pr.BeadsIssueId = issueId;
        pr.GitHubPRNumber = prNumber;
        await _dataStore.UpdatePullRequestAsync(pr);

        return true;
    }

    public Task<string?> TryLinkByBranchNameAsync(string projectId, string pullRequestId)
    {
        _logger.LogDebug("[Mock] TryLinkByBranchName PR:{PullRequestId} in project {ProjectId}",
            pullRequestId, projectId);

        var pr = _dataStore.GetPullRequest(pullRequestId);
        if (pr == null)
        {
            return Task.FromResult<string?>(null);
        }

        // Try to extract issue ID from branch name
        // Branch format might be: issues/task/some-description+IssueId
        var branchName = pr.BranchName;
        if (string.IsNullOrEmpty(branchName))
        {
            return Task.FromResult<string?>(null);
        }

        // Check for issue ID pattern in branch name
        var plusIndex = branchName.LastIndexOf('+');
        if (plusIndex > 0 && plusIndex < branchName.Length - 1)
        {
            var potentialIssueId = branchName[(plusIndex + 1)..];
            _logger.LogDebug("[Mock] Extracted potential issue ID from branch: {IssueId}", potentialIssueId);
            return Task.FromResult<string?>(potentialIssueId);
        }

        return Task.FromResult<string?>(null);
    }

    public async Task<bool> CloseLinkedIssueAsync(string projectId, string pullRequestId, string? reason = null)
    {
        _logger.LogDebug("[Mock] CloseLinkedIssue for PR:{PullRequestId} in project {ProjectId}, reason: {Reason}",
            pullRequestId, projectId, reason);

        var pr = _dataStore.GetPullRequest(pullRequestId);
        if (pr == null || string.IsNullOrEmpty(pr.BeadsIssueId))
        {
            _logger.LogDebug("[Mock] No linked issue to close");
            return false;
        }

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            return false;
        }

        var issue = await _fleeceService.GetIssueAsync(project.LocalPath, pr.BeadsIssueId);
        if (issue == null)
        {
            return false;
        }

        // Mark the issue as complete
        await _fleeceService.UpdateIssueAsync(
            project.LocalPath,
            pr.BeadsIssueId,
            status: IssueStatus.Complete);

        return true;
    }
}
