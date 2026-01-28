using Homespun.Features.GitHub;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGitHubService that operates on in-memory data.
/// Note: OpenPullRequestStatus only tracks open PRs (InDevelopment, ReadyForReview,
/// HasReviewComments, Approved). Merged/Closed PRs are removed from local tracking.
/// </summary>
public class MockGitHubService : IGitHubService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockGitHubService> _logger;
    private int _nextPrNumber = 100;

    public MockGitHubService(IDataStore dataStore, ILogger<MockGitHubService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public Task<string?> GetDefaultBranchAsync(string owner, string repo)
    {
        _logger.LogDebug("[Mock] GetDefaultBranch for {Owner}/{Repo}", owner, repo);
        return Task.FromResult<string?>("main");
    }

    public Task<bool> IsConfiguredAsync(string projectId)
    {
        _logger.LogDebug("[Mock] IsConfigured for project {ProjectId}", projectId);
        return Task.FromResult(true);
    }

    public Task<List<PullRequestInfo>> GetOpenPullRequestsAsync(string projectId)
    {
        _logger.LogDebug("[Mock] GetOpenPullRequests for project {ProjectId}", projectId);

        // All PRs in our data store are considered open (the enum only has open states)
        var prs = _dataStore.GetPullRequestsByProject(projectId)
            .Select(ConvertToPullRequestInfo)
            .ToList();

        return Task.FromResult(prs);
    }

    public Task<List<PullRequestInfo>> GetClosedPullRequestsAsync(string projectId)
    {
        _logger.LogDebug("[Mock] GetClosedPullRequests for project {ProjectId}", projectId);

        // In the real system, closed PRs are fetched from GitHub, not local storage
        // For mock, we return empty since OpenPullRequestStatus doesn't have closed states
        return Task.FromResult(new List<PullRequestInfo>());
    }

    public Task<PullRequestInfo?> GetPullRequestAsync(string projectId, int prNumber)
    {
        _logger.LogDebug("[Mock] GetPullRequest {PrNumber} for project {ProjectId}", prNumber, projectId);

        var pr = _dataStore.GetPullRequestsByProject(projectId)
            .FirstOrDefault(pr => pr.GitHubPRNumber == prNumber);

        return Task.FromResult(pr != null ? ConvertToPullRequestInfo(pr) : null);
    }

    public async Task<PullRequestInfo?> CreatePullRequestAsync(string projectId, string featureId)
    {
        _logger.LogDebug("[Mock] CreatePullRequest for feature {FeatureId} in project {ProjectId}", featureId, projectId);

        var pr = _dataStore.GetPullRequest(featureId);
        if (pr == null)
        {
            _logger.LogWarning("[Mock] Pull request {FeatureId} not found", featureId);
            return null;
        }

        // Assign a PR number if not already assigned
        if (pr.GitHubPRNumber == null)
        {
            pr.GitHubPRNumber = _nextPrNumber++;
            pr.Status = OpenPullRequestStatus.ReadyForReview;
            await _dataStore.UpdatePullRequestAsync(pr);
        }

        return ConvertToPullRequestInfo(pr);
    }

    public Task<bool> PushBranchAsync(string projectId, string branchName)
    {
        _logger.LogDebug("[Mock] PushBranch {BranchName} for project {ProjectId}", branchName, projectId);
        return Task.FromResult(true);
    }

    public Task<SyncResult> SyncPullRequestsAsync(string projectId)
    {
        _logger.LogDebug("[Mock] SyncPullRequests for project {ProjectId}", projectId);

        // In mock mode, data is already in sync since it's all in-memory
        return Task.FromResult(new SyncResult
        {
            Imported = 0,
            Updated = 0,
            Removed = 0
        });
    }

    public async Task<bool> LinkPullRequestAsync(string featureId, int prNumber)
    {
        _logger.LogDebug("[Mock] LinkPullRequest {PrNumber} to feature {FeatureId}", prNumber, featureId);

        var pr = _dataStore.GetPullRequest(featureId);
        if (pr == null)
        {
            return false;
        }

        pr.GitHubPRNumber = prNumber;
        await _dataStore.UpdatePullRequestAsync(pr);
        return true;
    }

    public Task<ReviewSummary> GetPullRequestReviewsAsync(string projectId, int prNumber)
    {
        _logger.LogDebug("[Mock] GetPullRequestReviews for PR {PrNumber} in project {ProjectId}", prNumber, projectId);

        // Return an empty review summary
        return Task.FromResult(new ReviewSummary
        {
            TotalReviews = 0,
            Approvals = 0,
            ChangesRequested = 0,
            Comments = 0,
            Reviews = []
        });
    }

    public async Task<bool> MergePullRequestAsync(string projectId, int prNumber, string? commitMessage = null)
    {
        _logger.LogDebug("[Mock] MergePullRequest {PrNumber} in project {ProjectId}", prNumber, projectId);

        var pr = _dataStore.GetPullRequestsByProject(projectId)
            .FirstOrDefault(pr => pr.GitHubPRNumber == prNumber);

        if (pr == null)
        {
            return false;
        }

        // In mock mode, we remove the PR from local storage to simulate merge
        // (real merged PRs are removed from local tracking)
        await _dataStore.RemovePullRequestAsync(pr.Id);
        return true;
    }

    private static PullRequestInfo ConvertToPullRequestInfo(PullRequest pr)
    {
        return new PullRequestInfo
        {
            Number = pr.GitHubPRNumber ?? 0,
            Title = pr.Title ?? "Untitled",
            Body = pr.Description,
            Status = ConvertStatus(pr.Status),
            BranchName = pr.BranchName,
            HtmlUrl = pr.GitHubPRNumber != null
                ? $"https://github.com/mock-org/mock-repo/pull/{pr.GitHubPRNumber}"
                : null,
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
            MergedAt = null, // Open PRs are not merged
            ChecksPassing = true,
            IsApproved = pr.Status == OpenPullRequestStatus.Approved
        };
    }

    private static PullRequestStatus ConvertStatus(OpenPullRequestStatus status)
    {
        return status switch
        {
            OpenPullRequestStatus.InDevelopment => PullRequestStatus.InProgress,
            OpenPullRequestStatus.ReadyForReview => PullRequestStatus.ReadyForReview,
            OpenPullRequestStatus.HasReviewComments => PullRequestStatus.InProgress,
            OpenPullRequestStatus.Approved => PullRequestStatus.ReadyForMerging,
            _ => PullRequestStatus.InProgress
        };
    }
}
