using System.Text.Json.Serialization;

namespace Homespun.Features.PullRequests.Data.Entities;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public required string LocalPath { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }
    public string DefaultBranch { get; set; } = "main";

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
