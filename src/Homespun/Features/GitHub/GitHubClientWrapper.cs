using Octokit;

namespace Homespun.Features.GitHub;

/// <summary>
/// Implementation of IGitHubClientWrapper using Octokit
/// </summary>
public class GitHubClientWrapper : IGitHubClientWrapper
{
    private readonly Func<string, GitHubClient> _clientFactory;

    public GitHubClientWrapper()
    {
        _clientFactory = token =>
        {
            var client = new GitHubClient(new ProductHeaderValue("Homespun"));
            if (!string.IsNullOrEmpty(token))
            {
                client.Credentials = new Credentials(token);
            }
            return client;
        };
    }

    private string? _currentToken;
    private GitHubClient? _client;

    public void SetToken(string? token)
    {
        _currentToken = token;
        _client = _clientFactory(token ?? "");
    }

    private GitHubClient GetClient()
    {
        if (_client == null)
        {
            _client = _clientFactory(_currentToken ?? "");
        }
        return _client;
    }

    public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repo, PullRequestRequest request)
    {
        return await GetClient().PullRequest.GetAllForRepository(owner, repo, request);
    }

    public async Task<PullRequest> GetPullRequestAsync(string owner, string repo, int number)
    {
        return await GetClient().PullRequest.Get(owner, repo, number);
    }

    public async Task<PullRequest> CreatePullRequestAsync(string owner, string repo, NewPullRequest newPullRequest)
    {
        return await GetClient().PullRequest.Create(owner, repo, newPullRequest);
    }

    public async Task<IReadOnlyList<PullRequestReview>> GetPullRequestReviewsAsync(string owner, string repo, int number)
    {
        return await GetClient().PullRequest.Review.GetAll(owner, repo, number);
    }

    public async Task<CombinedCommitStatus> GetCombinedCommitStatusAsync(string owner, string repo, string reference)
    {
        return await GetClient().Repository.Status.GetCombined(owner, repo, reference);
    }
}