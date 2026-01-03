using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Agents.Data;

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string FeatureId { get; set; }
    public int? ProcessId { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public string? SystemPrompt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Feature Feature { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
}
