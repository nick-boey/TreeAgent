using System.Text.Json.Serialization;

namespace Homespun.Features.PullRequests.Data.Entities;

/// <summary>
/// Represents a locally tracked pull request. Only open PRs are stored.
/// Closed/merged PRs should be retrieved from GitHub, and future work comes from ROADMAP.json.
/// </summary>
public class PullRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string ProjectId { get; set; }
    public string? ParentId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? BranchName { get; set; }
    public OpenPullRequestStatus Status { get; set; } = OpenPullRequestStatus.InDevelopment;
    public int? GitHubPRNumber { get; set; }
    public string? WorktreePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Port of the running OpenCode server for this PR, if any.
    /// Null means no agent is currently running.
    /// </summary>
    public int? AgentServerPort { get; set; }

    // Navigation properties excluded from JSON serialization - populated at runtime if needed
    [JsonIgnore]
    public Project Project { get; set; } = null!;
    
    [JsonIgnore]
    public PullRequest? Parent { get; set; }
    
    [JsonIgnore]
    public ICollection<PullRequest> Children { get; set; } = [];
}
