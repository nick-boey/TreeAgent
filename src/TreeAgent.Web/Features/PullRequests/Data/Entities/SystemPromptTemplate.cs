namespace TreeAgent.Web.Features.PullRequests.Data.Entities;

/// <summary>
/// A reusable system prompt template that can be shared across projects and features
/// </summary>
public class SystemPromptTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Name of the template for easy identification
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what this template is for
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The actual prompt content. Can include template variables like:
    /// {{PROJECT_NAME}}, {{FEATURE_TITLE}}, {{FEATURE_DESCRIPTION}}, {{BRANCH_NAME}}, {{FEATURE_TREE}}
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Optional project ID if this template is project-specific
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Whether this is a global template available to all projects
    /// </summary>
    public bool IsGlobal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}
