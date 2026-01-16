namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// A part of an agent message (text, tool use, or tool result).
/// </summary>
public class AgentMessagePart
{
    /// <summary>
    /// Type of the message part ("text", "tool_use", "tool_result").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Text content (for text parts).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Tool name (for tool_use and tool_result parts).
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Tool use ID (for correlating tool_use and tool_result).
    /// </summary>
    public string? ToolUseId { get; init; }

    /// <summary>
    /// Tool input (for tool_use parts).
    /// </summary>
    public object? ToolInput { get; init; }

    /// <summary>
    /// Tool output (for tool_result parts).
    /// </summary>
    public object? ToolOutput { get; init; }

    /// <summary>
    /// Tool execution state.
    /// </summary>
    public ToolExecutionState? ToolState { get; init; }
}

/// <summary>
/// State of tool execution.
/// </summary>
public class ToolExecutionState
{
    /// <summary>
    /// Status of tool execution ("running", "completed", "error").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Error message if the tool failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// When tool execution started (Unix timestamp).
    /// </summary>
    public long? StartTime { get; init; }

    /// <summary>
    /// When tool execution ended (Unix timestamp).
    /// </summary>
    public long? EndTime { get; init; }

    /// <summary>
    /// Title or description of the tool execution.
    /// </summary>
    public string? Title { get; init; }
}
