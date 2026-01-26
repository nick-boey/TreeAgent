namespace Homespun.Features.Shared.Services;

/// <summary>
/// Service for rendering markdown text to HTML.
/// </summary>
public interface IMarkdownRenderingService
{
    /// <summary>
    /// Converts markdown text to HTML using the Markdig pipeline.
    /// </summary>
    /// <param name="markdown">The markdown text to convert.</param>
    /// <returns>HTML string safe for rendering in Blazor components.</returns>
    string RenderToHtml(string? markdown);

    /// <summary>
    /// Determines if the text contains markdown syntax worth rendering.
    /// Falls back to plain text for simple content.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <returns>True if the text should be rendered as markdown.</returns>
    bool ContainsMarkdown(string? text);
}
