namespace Homespun.Features.GitHub;

/// <summary>
/// Represents the current GitHub authentication status.
/// </summary>
public record GitHubAuthStatus
{
    /// <summary>
    /// Whether GitHub authentication is configured and valid.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// The GitHub username if authenticated.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// A human-readable status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Error message if authentication check failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The authentication method in use.
    /// </summary>
    public GitHubAuthMethod AuthMethod { get; init; }
}

/// <summary>
/// Methods of GitHub authentication.
/// </summary>
public enum GitHubAuthMethod
{
    /// <summary>
    /// No authentication configured.
    /// </summary>
    None,

    /// <summary>
    /// Using GITHUB_TOKEN environment variable.
    /// </summary>
    Token,

    /// <summary>
    /// Using gh CLI authentication.
    /// </summary>
    GhCli,

    /// <summary>
    /// Both token and gh CLI are available.
    /// </summary>
    Both
}
