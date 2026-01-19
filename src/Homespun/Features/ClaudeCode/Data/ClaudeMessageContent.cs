namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents a content block within a Claude message.
/// </summary>
public class ClaudeMessageContent
{
    /// <summary>
    /// The type of content block.
    /// </summary>
    public required ClaudeContentType Type { get; init; }

    /// <summary>
    /// The text content (for text blocks).
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// The thinking content (for thinking blocks).
    /// </summary>
    public string? Thinking { get; set; }

    /// <summary>
    /// The tool name (for tool_use blocks).
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// The tool input as JSON (for tool_use blocks).
    /// </summary>
    public string? ToolInput { get; set; }

    /// <summary>
    /// The tool result (for tool_result blocks).
    /// </summary>
    public string? ToolResult { get; set; }

    /// <summary>
    /// Whether the tool call was successful (for tool_result blocks).
    /// </summary>
    public bool? ToolSuccess { get; set; }

    /// <summary>
    /// Whether this content block is still being streamed.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// The index of this content block within the message (used for precise streaming block tracking).
    /// </summary>
    public int Index { get; set; } = -1;
}

/// <summary>
/// Types of content blocks in a Claude message.
/// </summary>
public enum ClaudeContentType
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    Text,

    /// <summary>
    /// Claude's thinking/reasoning.
    /// </summary>
    Thinking,

    /// <summary>
    /// A tool being used.
    /// </summary>
    ToolUse,

    /// <summary>
    /// Result from a tool call.
    /// </summary>
    ToolResult
}
