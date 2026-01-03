using TreeAgent.Web.Features.Agents.Data;

namespace TreeAgent.Web.Features.PullRequests.Data.Entities;

public class Feature
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string ProjectId { get; set; }
    public string? ParentId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? BranchName { get; set; }
    public FeatureStatus Status { get; set; } = FeatureStatus.Future;
    public int? GitHubPRNumber { get; set; }
    public string? WorktreePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project Project { get; set; } = null!;
    public Feature? Parent { get; set; }
    public ICollection<Feature> Children { get; set; } = [];
    public ICollection<Agent> Agents { get; set; } = [];
}
