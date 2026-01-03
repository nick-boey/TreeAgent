namespace TreeAgent.Web.Features.PullRequests.Data.Entities;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Name { get; set; }
    public required string LocalPath { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }
    public string DefaultBranch { get; set; } = "main";

    /// <summary>
    /// Default system prompt used for new agents in this project
    /// </summary>
    public string? DefaultSystemPrompt { get; set; }

    /// <summary>
    /// Reference to a template used as the default prompt
    /// </summary>
    public string? DefaultPromptTemplateId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public SystemPromptTemplate? DefaultPromptTemplate { get; set; }
    public ICollection<Feature> Features { get; set; } = [];
    public ICollection<SystemPromptTemplate> PromptTemplates { get; set; } = [];
}
