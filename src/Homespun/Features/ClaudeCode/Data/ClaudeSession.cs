namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents an active Claude Code session.
/// </summary>
public class ClaudeSession
{
    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The entity ID this session is associated with (e.g., BeadsIssue ID, PR ID).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The project ID this session belongs to.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The working directory for this session.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The Claude model being used for this session.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The session mode (Plan or Build).
    /// </summary>
    public required SessionMode Mode { get; init; }

    /// <summary>
    /// Current status of the session.
    /// </summary>
    public ClaudeSessionStatus Status { get; set; } = ClaudeSessionStatus.Starting;

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the session was last active.
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Messages exchanged in this session.
    /// </summary>
    public List<ClaudeMessage> Messages { get; init; } = [];

    /// <summary>
    /// Optional error message if the session is in error state.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The conversation ID from the Claude SDK, if available.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Optional system prompt for the session.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Total cost in USD for this session.
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// Total duration in milliseconds for this session.
    /// </summary>
    public long TotalDurationMs { get; set; }
}
