using Markdig;
using System.Text.RegularExpressions;
using System.Web;

namespace Homespun.Features.Shared.Services;

/// <summary>
/// Service for rendering markdown text to HTML using Markdig.
/// </summary>
public class MarkdownRenderingService : IMarkdownRenderingService
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderingService()
    {
        // Configure Markdig pipeline with safe, commonly-used extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()      // Tables, definition lists, task lists, etc.
            .UseAutoLinks()               // Auto-convert URLs to links
            .UseEmojiAndSmiley()         // :emoji: support
            .UseSoftlineBreakAsHardlineBreak()  // Single line breaks become <br>
            .DisableHtml()                // SECURITY: Disable raw HTML in markdown
            .Build();
    }

    /// <inheritdoc/>
    public string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // If it doesn't look like markdown, return as plain text wrapped in <p>
        if (!ContainsMarkdown(markdown))
        {
            return $"<p>{HttpUtility.HtmlEncode(markdown)}</p>";
        }

        // Convert markdown to HTML
        return Markdown.ToHtml(markdown, _pipeline);
    }

    /// <inheritdoc/>
    public bool ContainsMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Heuristic checks for common markdown patterns
        // Headers
        if (Regex.IsMatch(text, @"^#{1,6}\s", RegexOptions.Multiline))
            return true;

        // Lists (unordered or ordered)
        if (Regex.IsMatch(text, @"^[\*\-\+]\s", RegexOptions.Multiline))
            return true;
        if (Regex.IsMatch(text, @"^\d+\.\s", RegexOptions.Multiline))
            return true;

        // Code blocks (fenced or indented)
        if (text.Contains("```") || text.Contains("~~~"))
            return true;

        // Links
        if (Regex.IsMatch(text, @"\[.+?\]\(.+?\)"))
            return true;

        // Emphasis
        if (Regex.IsMatch(text, @"\*\*.+?\*\*|\*.+?\*|__.+?__|_.+?_"))
            return true;

        // Blockquotes
        if (Regex.IsMatch(text, @"^>\s", RegexOptions.Multiline))
            return true;

        // Horizontal rules
        if (Regex.IsMatch(text, @"^(\*\*\*|---|___)$", RegexOptions.Multiline))
            return true;

        // No markdown patterns detected
        return false;
    }
}
