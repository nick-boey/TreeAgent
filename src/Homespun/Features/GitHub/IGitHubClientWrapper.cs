using Octokit;

namespace Homespun.Features.GitHub;

/// <summary>
/// Wrapper interface for Octokit's GitHubClient to enable testing
/// </summary>
public interface IGitHubClientWrapper
{
    Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repo, PullRequestRequest request);
    Task<PullRequest> GetPullRequestAsync(string owner, string repo, int number);
    Task<PullRequest> CreatePullRequestAsync(string owner, string repo, NewPullRequest newPullRequest);
    Task<IReadOnlyList<PullRequestReview>> GetPullRequestReviewsAsync(string owner, string repo, int number);
    Task<CombinedCommitStatus> GetCombinedCommitStatusAsync(string owner, string repo, string reference);
}