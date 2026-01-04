using Homespun.Features.PullRequests;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for interacting with GitHub API
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Check if GitHub is configured for the project
    /// </summary>
    Task<bool> IsConfiguredAsync(string projectId);

    /// <summary>
    /// Fetch all open pull requests
    /// </summary>
    Task<List<PullRequestInfo>> GetOpenPullRequestsAsync(string projectId);

    /// <summary>
    /// Fetch all closed/merged pull requests
    /// </summary>
    Task<List<PullRequestInfo>> GetClosedPullRequestsAsync(string projectId);

    /// <summary>
    /// Get a specific pull request by number
    /// </summary>
    Task<PullRequestInfo?> GetPullRequestAsync(string projectId, int prNumber);

    /// <summary>
    /// Create a pull request from a feature branch
    /// </summary>
    Task<PullRequestInfo?> CreatePullRequestAsync(string projectId, string featureId);

    /// <summary>
    /// Push a branch to the remote and create a PR
    /// </summary>
    Task<bool> PushBranchAsync(string projectId, string branchName);

    /// <summary>
    /// Sync pull requests with features - imports PRs as features and updates existing feature statuses
    /// </summary>
    Task<SyncResult> SyncPullRequestsAsync(string projectId);

    /// <summary>
    /// Link a pull request number to a feature
    /// </summary>
    Task<bool> LinkPullRequestAsync(string featureId, int prNumber);
}
