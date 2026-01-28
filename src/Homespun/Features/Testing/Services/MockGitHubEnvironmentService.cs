using Homespun.Features.GitHub;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGitHubEnvironmentService that simulates GitHub authentication.
/// </summary>
public class MockGitHubEnvironmentService : IGitHubEnvironmentService
{
    public bool IsConfigured => true;

    public IDictionary<string, string> GetGitHubEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["GITHUB_TOKEN"] = "mock-token-xxxx"
        };
    }

    public string? GetMaskedToken()
    {
        return "ghp_****mock";
    }

    public Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new GitHubAuthStatus
        {
            IsAuthenticated = true,
            Username = "mock-user",
            Message = "Logged in to github.com as mock-user (mock mode)",
            AuthMethod = GitHubAuthMethod.Token
        });
    }
}
