namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// A real-time event from an agent.
/// </summary>
public class AgentEvent
{
    /// <summary>
    /// Event type (see <see cref="AgentEventTypes"/>).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The agent that emitted this event.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Session ID (if applicable).
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Message ID (if applicable).
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Content associated with the event.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool name (for tool events).
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Status associated with the event.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Error message (if applicable).
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Additional properties (harness-specific).
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = [];
}

/// <summary>
/// Known agent event types (harness-agnostic).
/// </summary>
public static class AgentEventTypes
{
    /// <summary>
    /// Agent has connected and is ready.
    /// </summary>
    public const string Connected = "agent.connected";

    /// <summary>
    /// Agent has disconnected.
    /// </summary>
    public const string Disconnected = "agent.disconnected";

    /// <summary>
    /// A new session was created.
    /// </summary>
    public const string SessionCreated = "session.created";

    /// <summary>
    /// A session was updated.
    /// </summary>
    public const string SessionUpdated = "session.updated";

    /// <summary>
    /// A new message was created.
    /// </summary>
    public const string MessageCreated = "message.created";

    /// <summary>
    /// A message was updated.
    /// </summary>
    public const string MessageUpdated = "message.updated";

    /// <summary>
    /// A tool execution started.
    /// </summary>
    public const string ToolStarted = "tool.started";

    /// <summary>
    /// A tool execution completed.
    /// </summary>
    public const string ToolCompleted = "tool.completed";

    /// <summary>
    /// Agent status changed.
    /// </summary>
    public const string StatusChanged = "status.changed";
}
