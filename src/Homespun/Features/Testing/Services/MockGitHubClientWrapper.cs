using Homespun.Features.GitHub;
using Octokit;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGitHubClientWrapper that returns simulated Octokit responses.
/// </summary>
public class MockGitHubClientWrapper : IGitHubClientWrapper
{
    private int _nextPrNumber = 100;

    public Task<Repository> GetRepositoryAsync(string owner, string repo)
    {
        // Create a minimal mock repository
        // Using reflection or a stub since Repository constructor is complex
        return Task.FromResult(CreateMockRepository(owner, repo));
    }

    public Task<IReadOnlyList<Octokit.PullRequest>> GetPullRequestsAsync(
        string owner,
        string repo,
        PullRequestRequest request)
    {
        var prs = new List<Octokit.PullRequest>();

        // Return empty list - actual PRs are managed through MockGitHubService
        return Task.FromResult<IReadOnlyList<Octokit.PullRequest>>(prs);
    }

    public Task<Octokit.PullRequest> GetPullRequestAsync(string owner, string repo, int number)
    {
        return Task.FromResult(CreateMockPullRequest(owner, repo, number, "Mock PR", "open"));
    }

    public Task<Octokit.PullRequest> CreatePullRequestAsync(
        string owner,
        string repo,
        NewPullRequest newPullRequest)
    {
        var prNumber = _nextPrNumber++;
        return Task.FromResult(CreateMockPullRequest(owner, repo, prNumber, newPullRequest.Title, "open"));
    }

    public Task<IReadOnlyList<PullRequestReview>> GetPullRequestReviewsAsync(
        string owner,
        string repo,
        int number)
    {
        var reviews = new List<PullRequestReview>();
        return Task.FromResult<IReadOnlyList<PullRequestReview>>(reviews);
    }

    public Task<CombinedCommitStatus> GetCombinedCommitStatusAsync(
        string owner,
        string repo,
        string reference)
    {
        // Return a successful status
        return Task.FromResult(CreateMockCombinedStatus());
    }

    public Task<PullRequestMerge> MergePullRequestAsync(
        string owner,
        string repo,
        int number,
        MergePullRequest merge)
    {
        return Task.FromResult(CreateMockPullRequestMerge());
    }

    public Task<IReadOnlyList<PullRequestReviewComment>> GetPullRequestReviewCommentsAsync(
        string owner,
        string repo,
        int number)
    {
        var comments = new List<PullRequestReviewComment>();
        return Task.FromResult<IReadOnlyList<PullRequestReviewComment>>(comments);
    }

    // Helper methods to create mock Octokit objects
    // Since Octokit types have complex constructors, we use internal APIs or minimal objects

    private static Repository CreateMockRepository(string owner, string repo)
    {
        // Repository is complex - return null and let callers handle it
        // In practice, the service wraps this and extracts needed data
        return null!;
    }

    private static Octokit.PullRequest CreateMockPullRequest(
        string owner,
        string repo,
        int number,
        string title,
        string state)
    {
        // PullRequest is complex - return null and let the service handle it
        // The MockGitHubService will construct PullRequestInfo directly
        return null!;
    }

    private static CombinedCommitStatus CreateMockCombinedStatus()
    {
        // Return null - MockGitHubService handles status directly
        return null!;
    }

    private static PullRequestMerge CreateMockPullRequestMerge()
    {
        // Return null - MockGitHubService handles merge result directly
        return null!;
    }
}
