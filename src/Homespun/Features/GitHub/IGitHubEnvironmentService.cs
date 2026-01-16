namespace Homespun.Features.GitHub;

/// <summary>
/// Provides environment variables for GitHub authentication in spawned processes.
/// This service enables agents to authenticate with GitHub via the gh CLI and git commands
/// by injecting GITHUB_TOKEN and GIT_ASKPASS into their environment.
/// </summary>
public interface IGitHubEnvironmentService
{
    /// <summary>
    /// Gets environment variables required for GitHub authentication.
    /// Returns GITHUB_TOKEN, GIT_ASKPASS, and GIT_TERMINAL_PROMPT variables.
    /// </summary>
    /// <returns>Dictionary of environment variable names and values. Empty if not configured.</returns>
    IDictionary<string, string> GetGitHubEnvironment();

    /// <summary>
    /// Gets whether a GitHub token is configured and available.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets the GitHub token with most characters masked for display purposes.
    /// Returns null if no token is configured.
    /// </summary>
    /// <returns>Masked token like "ghp_***...xyz" or null</returns>
    string? GetMaskedToken();

    /// <summary>
    /// Checks gh CLI authentication status by running 'gh auth status'.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Authentication status including username if authenticated</returns>
    Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default);
}
