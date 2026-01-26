namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents a custom agent prompt template that can be used to start sessions.
/// </summary>
public class AgentPrompt
{
    /// <summary>
    /// Unique identifier for the agent prompt.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];

    /// <summary>
    /// Display name for the agent prompt (e.g., "Plan", "Build").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The initial message template sent to the agent.
    /// Can contain placeholders like {{title}}, {{description}}, {{branch}}, {{id}}, {{type}}.
    /// </summary>
    public string? InitialMessage { get; set; }

    /// <summary>
    /// The session mode (Plan or Build) which determines available tools.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Build;

    /// <summary>
    /// When the agent prompt was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the agent prompt was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
