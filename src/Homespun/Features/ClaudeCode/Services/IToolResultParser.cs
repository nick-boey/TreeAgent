using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Parses raw tool result content into structured data for display.
/// </summary>
public interface IToolResultParser
{
    /// <summary>
    /// Parses tool result content into structured ToolResultData.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="rawContent">The raw result content (string or object).</param>
    /// <param name="isError">Whether the tool reported an error.</param>
    /// <returns>Structured tool result data, or null if parsing fails.</returns>
    ToolResultData? Parse(string toolName, object? rawContent, bool isError);
}
