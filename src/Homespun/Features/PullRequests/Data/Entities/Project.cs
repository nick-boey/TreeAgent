using System.Text.Json.Serialization;

namespace Homespun.Features.PullRequests.Data.Entities;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Project name (derived from repository name during creation).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Path to the local Git repository worktree for the default branch.
    /// Format: ~/.homespun/src/{repository-name}/{branch-name}
    /// </summary>
    public required string LocalPath { get; set; }

    /// <summary>
    /// GitHub repository owner (user or organization).
    /// Null for local-only projects.
    /// </summary>
    public string? GitHubOwner { get; set; }

    /// <summary>
    /// GitHub repository name.
    /// Null for local-only projects.
    /// </summary>
    public string? GitHubRepo { get; set; }

    /// <summary>
    /// Default branch name retrieved from GitHub.
    /// </summary>
    public required string DefaultBranch { get; set; }

    /// <summary>
    /// Default model used for new agent sessions in this project.
    /// Format: "provider/model" (e.g., "anthropic/claude-sonnet-4-5")
    /// </summary>
    public string? DefaultModel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property excluded from JSON serialization - populated at runtime
    [JsonIgnore]
    public ICollection<PullRequest> PullRequests { get; set; } = [];
}
