using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Context information for rendering prompt templates.
/// </summary>
public class PromptContext
{
    /// <summary>
    /// The title of the issue or PR.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The unique identifier (issue ID or PR number).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The description text.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// The type (e.g., Feature, Bug, Task for issues; or "PullRequest" for PRs).
    /// </summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Service for managing custom agent prompts.
/// </summary>
public interface IAgentPromptService
{
    /// <summary>
    /// Gets all agent prompts.
    /// </summary>
    IReadOnlyList<AgentPrompt> GetAllPrompts();

    /// <summary>
    /// Gets an agent prompt by ID.
    /// </summary>
    AgentPrompt? GetPrompt(string id);

    /// <summary>
    /// Creates a new agent prompt.
    /// </summary>
    Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Updates an existing agent prompt.
    /// </summary>
    Task<AgentPrompt> UpdatePromptAsync(string id, string name, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Deletes an agent prompt.
    /// </summary>
    Task DeletePromptAsync(string id);

    /// <summary>
    /// Renders a template string with context values.
    /// Supports placeholders: {{title}}, {{id}}, {{description}}, {{branch}}, {{type}}
    /// </summary>
    string? RenderTemplate(string? template, PromptContext context);

    /// <summary>
    /// Ensures default agent prompts (Plan, Build) exist.
    /// </summary>
    Task EnsureDefaultPromptsAsync();
}
