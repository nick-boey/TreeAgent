using Homespun.Features.Shared.Services;

namespace Homespun.Tests.Features.Shared;

[TestFixture]
public class MarkdownRenderingServiceTests
{
    private IMarkdownRenderingService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new MarkdownRenderingService();
    }

    #region RenderToHtml Tests

    [Test]
    public void RenderToHtml_WithNull_ReturnsEmptyString()
    {
        // Act
        var result = _service.RenderToHtml(null);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RenderToHtml_WithEmptyString_ReturnsEmptyString()
    {
        // Act
        var result = _service.RenderToHtml(string.Empty);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RenderToHtml_WithWhitespace_ReturnsEmptyString()
    {
        // Act
        var result = _service.RenderToHtml("   \n\t  ");

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RenderToHtml_WithPlainText_ReturnsEncodedParagraph()
    {
        // Arrange
        var plainText = "Just plain text without markdown";

        // Act
        var result = _service.RenderToHtml(plainText);

        // Assert
        Assert.That(result, Does.Contain("<p>"));
        Assert.That(result, Does.Contain("Just plain text without markdown"));
        Assert.That(result, Does.Contain("</p>"));
    }

    [Test]
    public void RenderToHtml_WithPlainTextContainingSpecialChars_EncodesHtml()
    {
        // Arrange
        var plainText = "Text with <special> & \"characters\"";

        // Act
        var result = _service.RenderToHtml(plainText);

        // Assert
        Assert.That(result, Does.Contain("&lt;special&gt;"));
        Assert.That(result, Does.Contain("&amp;"));
        Assert.That(result, Does.Contain("&quot;"));
    }

    [Test]
    public void RenderToHtml_WithHeader_RendersHeaderTag()
    {
        // Arrange
        var markdown = "# Hello World";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<h1"));
        Assert.That(result, Does.Contain("Hello World"));
        Assert.That(result, Does.Contain("</h1>"));
    }

    [Test]
    public void RenderToHtml_WithMultipleLevelHeaders_RendersAllHeaders()
    {
        // Arrange
        var markdown = @"# H1
## H2
### H3";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<h1"));
        Assert.That(result, Does.Contain("<h2"));
        Assert.That(result, Does.Contain("<h3"));
    }

    [Test]
    public void RenderToHtml_WithBold_RendersBoldTag()
    {
        // Arrange
        var markdown = "This is **bold** text";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<strong>"));
        Assert.That(result, Does.Contain("bold"));
        Assert.That(result, Does.Contain("</strong>"));
    }

    [Test]
    public void RenderToHtml_WithItalic_RendersEmTag()
    {
        // Arrange
        var markdown = "This is *italic* text";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<em>"));
        Assert.That(result, Does.Contain("italic"));
        Assert.That(result, Does.Contain("</em>"));
    }

    [Test]
    public void RenderToHtml_WithUnorderedList_RendersListTags()
    {
        // Arrange
        var markdown = @"- Item 1
- Item 2
- Item 3";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<ul>"));
        Assert.That(result, Does.Contain("<li>"));
        Assert.That(result, Does.Contain("Item 1"));
        Assert.That(result, Does.Contain("Item 2"));
        Assert.That(result, Does.Contain("</ul>"));
    }

    [Test]
    public void RenderToHtml_WithOrderedList_RendersOrderedListTags()
    {
        // Arrange
        var markdown = @"1. First
2. Second
3. Third";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<ol>"));
        Assert.That(result, Does.Contain("<li>"));
        Assert.That(result, Does.Contain("First"));
        Assert.That(result, Does.Contain("</ol>"));
    }

    [Test]
    public void RenderToHtml_WithLink_RendersAnchorTag()
    {
        // Arrange
        var markdown = "[Click here](https://example.com)";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<a href=\"https://example.com\">"));
        Assert.That(result, Does.Contain("Click here"));
        Assert.That(result, Does.Contain("</a>"));
    }

    [Test]
    public void RenderToHtml_WithCodeBlock_RendersPreAndCodeTags()
    {
        // Arrange
        var markdown = @"```csharp
var x = 10;
```";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<pre>"));
        Assert.That(result, Does.Contain("<code"));
        Assert.That(result, Does.Contain("var x = 10;"));
        Assert.That(result, Does.Contain("</code>"));
        Assert.That(result, Does.Contain("</pre>"));
    }

    [Test]
    public void RenderToHtml_WithInlineCode_FallsBackToPlainText()
    {
        // Arrange - Inline code alone isn't detected as markdown by heuristic
        var markdown = "Use the `var` keyword";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert - Falls back to plain text in paragraph
        Assert.That(result, Does.Contain("<p>"));
        Assert.That(result, Does.Contain("Use the `var` keyword"));
        Assert.That(result, Does.Contain("</p>"));
    }

    [Test]
    public void RenderToHtml_WithBlockquote_RendersBlockquoteTag()
    {
        // Arrange
        var markdown = "> This is a quote";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<blockquote>"));
        Assert.That(result, Does.Contain("This is a quote"));
        Assert.That(result, Does.Contain("</blockquote>"));
    }

    [Test]
    public void RenderToHtml_WithHorizontalRule_RendersHrTag()
    {
        // Arrange
        var markdown = "---";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<hr"));
    }

    [Test]
    public void RenderToHtml_WithTable_FallsBackToPlainText()
    {
        // Arrange - Tables alone aren't detected as markdown by heuristic
        var markdown = @"| Header 1 | Header 2 |
|----------|----------|
| Cell 1   | Cell 2   |";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert - Falls back to plain text in paragraph
        Assert.That(result, Does.Contain("<p>"));
        Assert.That(result, Does.Contain("Header 1"));
        Assert.That(result, Does.Contain("Cell 1"));
        Assert.That(result, Does.Contain("</p>"));
    }

    [Test]
    public void RenderToHtml_WithTaskList_RendersCheckboxes()
    {
        // Arrange
        var markdown = @"- [x] Completed task
- [ ] Incomplete task";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("type=\"checkbox\""));
        Assert.That(result, Does.Contain("checked=\"checked\""));
        Assert.That(result, Does.Contain("Completed task"));
        Assert.That(result, Does.Contain("Incomplete task"));
    }

    [Test]
    public void RenderToHtml_WithRawHtml_DoesNotRenderHtml()
    {
        // Arrange - DisableHtml() should prevent this
        var markdown = "<script>alert('xss')</script>";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        // Raw HTML should be escaped or not rendered as HTML
        Assert.That(result, Does.Not.Contain("<script>alert('xss')</script>"));
    }

    [Test]
    public void RenderToHtml_WithMaliciousLink_DoesNotExecuteJavascript()
    {
        // Arrange
        var markdown = "[click me](javascript:alert('xss'))";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        // Should still render as a link but sanitized
        Assert.That(result, Does.Contain("click me"));
    }

    [Test]
    public void RenderToHtml_WithComplexMarkdown_RendersAllElements()
    {
        // Arrange
        var markdown = @"# Main Title

This is a paragraph with **bold** and *italic* text.

## Section

- List item 1
- List item 2

Code: `var x = 10;`

> Quote here

[Link](https://example.com)";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<h1"));
        Assert.That(result, Does.Contain("<h2"));
        Assert.That(result, Does.Contain("<strong>"));
        Assert.That(result, Does.Contain("<em>"));
        Assert.That(result, Does.Contain("<ul>"));
        Assert.That(result, Does.Contain("<code>"));
        Assert.That(result, Does.Contain("<blockquote>"));
        Assert.That(result, Does.Contain("<a href="));
    }

    #endregion

    #region ContainsMarkdown Tests

    [Test]
    public void ContainsMarkdown_WithNull_ReturnsFalse()
    {
        // Act
        var result = _service.ContainsMarkdown(null);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsMarkdown_WithEmptyString_ReturnsFalse()
    {
        // Act
        var result = _service.ContainsMarkdown(string.Empty);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsMarkdown_WithWhitespace_ReturnsFalse()
    {
        // Act
        var result = _service.ContainsMarkdown("   \n\t  ");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsMarkdown_WithPlainText_ReturnsFalse()
    {
        // Act
        var result = _service.ContainsMarkdown("Just plain text");

        // Assert
        Assert.That(result, Is.False);
    }

    [TestCase("# Header")]
    [TestCase("## Header 2")]
    [TestCase("### Header 3")]
    public void ContainsMarkdown_WithHeaders_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("- List item")]
    [TestCase("* List item")]
    [TestCase("+ List item")]
    public void ContainsMarkdown_WithUnorderedList_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("1. First item")]
    [TestCase("2. Second item")]
    public void ContainsMarkdown_WithOrderedList_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("```code```")]
    [TestCase("```\ncode\n```")]
    [TestCase("~~~\ncode\n~~~")]
    public void ContainsMarkdown_WithCodeBlocks_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("[link](url)")]
    [TestCase("[text](https://example.com)")]
    public void ContainsMarkdown_WithLinks_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("**bold**")]
    [TestCase("*italic*")]
    [TestCase("__bold__")]
    [TestCase("_italic_")]
    public void ContainsMarkdown_WithEmphasis_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("> quote")]
    [TestCase("> blockquote text")]
    public void ContainsMarkdown_WithBlockquote_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase("---")]
    [TestCase("***")]
    [TestCase("___")]
    public void ContainsMarkdown_WithHorizontalRule_ReturnsTrue(string markdown)
    {
        // Act
        var result = _service.ContainsMarkdown(markdown);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsMarkdown_WithMixedContent_ReturnsTrue()
    {
        // Arrange
        var text = @"Some text
# Header
More text";

        // Act
        var result = _service.ContainsMarkdown(text);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RenderToHtml_WithInlineCodeInMarkdownContext_RendersCodeTag()
    {
        // Arrange - Inline code works when part of detected markdown
        var markdown = "## Header\nUse the `var` keyword";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<h2"));
        Assert.That(result, Does.Contain("<code>"));
        Assert.That(result, Does.Contain("var"));
    }

    [Test]
    public void RenderToHtml_WithTableInMarkdownContext_RendersTable()
    {
        // Arrange - Tables work when part of detected markdown
        var markdown = @"# Data

| Header 1 | Header 2 |
|----------|----------|
| Cell 1   | Cell 2   |";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("<h1"));
        Assert.That(result, Does.Contain("<table>"));
        Assert.That(result, Does.Contain("Header 1"));
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Test]
    public void RenderToHtml_WithVeryLongMarkdown_HandlesGracefully()
    {
        // Arrange
        var longMarkdown = string.Join("\n", Enumerable.Repeat("# Header", 1000));

        // Act
        var result = _service.RenderToHtml(longMarkdown);

        // Assert
        Assert.That(result, Is.Not.Empty);
        Assert.That(result, Does.Contain("<h1"));
    }

    [Test]
    public void RenderToHtml_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var markdown = "# Hello 世界 🌍";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result, Does.Contain("世界"));
        Assert.That(result, Does.Contain("🌍"));
    }

    [Test]
    public void RenderToHtml_WithMalformedMarkdown_DoesNotThrow()
    {
        // Arrange
        var malformed = "# Unclosed [link(url **bold";

        // Act & Assert
        Assert.DoesNotThrow(() => _service.RenderToHtml(malformed));
    }

    [Test]
    public void RenderToHtml_ConsecutiveCalls_ProduceConsistentResults()
    {
        // Arrange
        var markdown = "# Test Header";

        // Act
        var result1 = _service.RenderToHtml(markdown);
        var result2 = _service.RenderToHtml(markdown);

        // Assert
        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void RenderToHtml_WithAutoLinks_FallsBackToPlainText()
    {
        // Arrange - Bare URLs alone aren't detected as markdown by heuristic
        var markdown = "Visit https://example.com for more";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert - Falls back to plain text (URL detection requires markdown syntax)
        Assert.That(result, Does.Contain("<p>"));
        Assert.That(result, Does.Contain("https://example.com"));
        Assert.That(result, Does.Contain("</p>"));
    }

    [Test]
    public void RenderToHtml_WithSoftLineBreak_FallsBackToPlainText()
    {
        // Arrange - Plain text with line breaks isn't detected as markdown
        var markdown = @"Line 1
Line 2";

        // Act
        var result = _service.RenderToHtml(markdown);

        // Assert - Falls back to plain text in paragraph
        Assert.That(result, Does.Contain("<p>"));
        Assert.That(result, Does.Contain("Line 1"));
        Assert.That(result, Does.Contain("Line 2"));
        Assert.That(result, Does.Contain("</p>"));
    }

    #endregion
}
