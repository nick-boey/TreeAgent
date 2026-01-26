namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Parsed and structured tool result data for rich display.
/// </summary>
public class ToolResultData
{
    /// <summary>
    /// The name of the tool that produced this result.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Brief summary for collapsed view (generated from result).
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Whether the tool execution was successful.
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// Type-specific data for rendering (varies by tool type).
    /// </summary>
    public object? TypedData { get; init; }
}

/// <summary>
/// Typed data for Read tool results.
/// </summary>
public class ReadToolData
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public int StartLine { get; init; } = 1;
    public int TotalLines { get; init; }
    public string? Language { get; init; }
}

/// <summary>
/// Typed data for Write/Edit tool results.
/// </summary>
public class WriteToolData
{
    public required string FilePath { get; init; }
    public required string Operation { get; init; } // "created", "updated", "edited", "deleted"
    public int? LinesWritten { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Typed data for Bash tool results.
/// </summary>
public class BashToolData
{
    public string? Command { get; init; }
    public required string Output { get; init; }
    public int? ExitCode { get; init; }
    public bool IsError { get; init; }
}

/// <summary>
/// Typed data for Task/Explore agent tool results.
/// </summary>
public class AgentToolData
{
    public required string Summary { get; init; }
    public string? DetailedOutput { get; init; }
    public List<string>? FilesAffected { get; init; }
}

/// <summary>
/// Typed data for Grep tool results.
/// </summary>
public class GrepToolData
{
    public string? Pattern { get; init; }
    public required List<GrepMatch> Matches { get; init; }
    public int TotalMatches { get; init; }
}

/// <summary>
/// A single grep match result.
/// </summary>
public class GrepMatch
{
    public required string FilePath { get; init; }
    public int? LineNumber { get; init; }
    public string? Content { get; init; }
}

/// <summary>
/// Typed data for Glob tool results.
/// </summary>
public class GlobToolData
{
    public string? Pattern { get; init; }
    public required List<string> Files { get; init; }
    public int TotalFiles { get; init; }
}

/// <summary>
/// Typed data for web-related tools (WebFetch, WebSearch).
/// </summary>
public class WebToolData
{
    public string? Url { get; init; }
    public required string Content { get; init; }
    public bool IsError { get; init; }
}

/// <summary>
/// Generic tool data for unknown or simple tool results.
/// </summary>
public class GenericToolData
{
    public required string Content { get; init; }
}
