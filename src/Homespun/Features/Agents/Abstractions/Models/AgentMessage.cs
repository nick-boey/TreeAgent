namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// A message from an agent (response to a prompt).
/// </summary>
public class AgentMessage
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The agent that sent this message.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Role of the message sender ("assistant" or "user").
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// When the message was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Parts of the message (text, tool calls, etc.).
    /// </summary>
    public List<AgentMessagePart> Parts { get; init; } = [];
}
