using System.Text.RegularExpressions;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Parses raw tool result content into structured data for rich display.
/// </summary>
public partial class ToolResultParser : IToolResultParser
{
    /// <inheritdoc />
    public ToolResultData? Parse(string toolName, object? rawContent, bool isError)
    {
        if (rawContent == null)
            return CreateGenericResult(toolName, "(no output)", isError);

        var contentString = rawContent.ToString() ?? "";

        // Normalize tool name for matching
        var normalizedName = toolName.ToLowerInvariant();

        return normalizedName switch
        {
            "read" => ParseReadResult(contentString, isError),
            "write" => ParseWriteResult(contentString, isError),
            "edit" => ParseEditResult(contentString, isError),
            "bash" => ParseBashResult(contentString, isError),
            "task" or "explore" => ParseAgentResult(normalizedName, contentString, isError),
            "grep" => ParseGrepResult(contentString, isError),
            "glob" => ParseGlobResult(contentString, isError),
            "webfetch" => ParseWebResult("WebFetch", contentString, isError),
            "websearch" => ParseWebResult("WebSearch", contentString, isError),
            _ => CreateGenericResult(toolName, contentString, isError)
        };
    }

    private ToolResultData ParseReadResult(string content, bool isError)
    {
        if (isError)
        {
            return new ToolResultData
            {
                ToolName = "Read",
                Summary = TruncateForSummary(content, 60),
                IsSuccess = false,
                TypedData = new ReadToolData
                {
                    FilePath = "unknown",
                    Content = content,
                    TotalLines = CountLines(content)
                }
            };
        }

        // Try to extract file path from the content
        // Read tool output often starts with line numbers like "     1→content"
        var filePath = TryExtractFilePath(content) ?? "file";
        var lineCount = CountLines(content);
        var language = DetectLanguageFromPath(filePath);

        return new ToolResultData
        {
            ToolName = "Read",
            Summary = $"{TruncateFilePath(filePath, 40)} ({lineCount} lines)",
            IsSuccess = true,
            TypedData = new ReadToolData
            {
                FilePath = filePath,
                Content = content,
                TotalLines = lineCount,
                Language = language
            }
        };
    }

    private ToolResultData ParseWriteResult(string content, bool isError)
    {
        var filePath = TryExtractFilePath(content) ?? "file";
        var operation = "created";

        if (content.Contains("created", StringComparison.OrdinalIgnoreCase))
            operation = "created";
        else if (content.Contains("updated", StringComparison.OrdinalIgnoreCase))
            operation = "updated";
        else if (content.Contains("written", StringComparison.OrdinalIgnoreCase))
            operation = "written";

        return new ToolResultData
        {
            ToolName = "Write",
            Summary = isError ? $"Failed to write {TruncateFilePath(filePath, 40)}" : $"Wrote {TruncateFilePath(filePath, 40)}",
            IsSuccess = !isError,
            TypedData = new WriteToolData
            {
                FilePath = filePath,
                Operation = operation,
                Message = content
            }
        };
    }

    private ToolResultData ParseEditResult(string content, bool isError)
    {
        var filePath = TryExtractFilePath(content) ?? "file";

        return new ToolResultData
        {
            ToolName = "Edit",
            Summary = isError ? $"Failed to edit {TruncateFilePath(filePath, 40)}" : $"Edited {TruncateFilePath(filePath, 40)}",
            IsSuccess = !isError,
            TypedData = new WriteToolData
            {
                FilePath = filePath,
                Operation = "edited",
                Message = content
            }
        };
    }

    private ToolResultData ParseBashResult(string content, bool isError)
    {
        // Try to extract command if it's echoed in output
        var command = TryExtractCommand(content);
        var outputPreview = TruncateForSummary(content, 50);

        string summary;
        if (isError)
        {
            summary = "Command failed";
        }
        else if (!string.IsNullOrEmpty(command))
        {
            summary = $"$ {TruncateForSummary(command, 40)}";
        }
        else
        {
            summary = string.IsNullOrWhiteSpace(content) ? "Command completed" : outputPreview;
        }

        return new ToolResultData
        {
            ToolName = "Bash",
            Summary = summary,
            IsSuccess = !isError,
            TypedData = new BashToolData
            {
                Command = command,
                Output = content,
                IsError = isError
            }
        };
    }

    private ToolResultData ParseAgentResult(string toolName, string content, bool isError)
    {
        // Agent/Task/Explore results - extract first meaningful line as summary
        var summary = ExtractFirstMeaningfulLine(content) ?? "Task completed";
        var displayName = toolName == "explore" ? "Explore" : "Task";

        return new ToolResultData
        {
            ToolName = displayName,
            Summary = TruncateForSummary(summary, 60),
            IsSuccess = !isError,
            TypedData = new AgentToolData
            {
                Summary = summary,
                DetailedOutput = content
            }
        };
    }

    private ToolResultData ParseGrepResult(string content, bool isError)
    {
        var matches = ParseGrepMatches(content);
        var matchCount = matches.Count;

        return new ToolResultData
        {
            ToolName = "Grep",
            Summary = matchCount == 0 ? "No matches found" : $"Found {matchCount} match{(matchCount == 1 ? "" : "es")}",
            IsSuccess = !isError,
            TypedData = new GrepToolData
            {
                Matches = matches,
                TotalMatches = matchCount
            }
        };
    }

    private ToolResultData ParseGlobResult(string content, bool isError)
    {
        var files = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        return new ToolResultData
        {
            ToolName = "Glob",
            Summary = files.Count == 0 ? "No files found" : $"Found {files.Count} file{(files.Count == 1 ? "" : "s")}",
            IsSuccess = !isError,
            TypedData = new GlobToolData
            {
                Files = files,
                TotalFiles = files.Count
            }
        };
    }

    private ToolResultData ParseWebResult(string toolName, string content, bool isError)
    {
        var summary = isError ? "Request failed" : "Content retrieved";

        return new ToolResultData
        {
            ToolName = toolName,
            Summary = summary,
            IsSuccess = !isError,
            TypedData = new WebToolData
            {
                Content = content,
                IsError = isError
            }
        };
    }

    private ToolResultData CreateGenericResult(string toolName, string content, bool isError)
    {
        // Capitalize first letter of tool name for display
        var displayName = string.IsNullOrEmpty(toolName) ? "Tool" :
            char.ToUpperInvariant(toolName[0]) + toolName[1..];

        return new ToolResultData
        {
            ToolName = displayName,
            Summary = isError ? $"{displayName} failed" : TruncateForSummary(content, 50),
            IsSuccess = !isError,
            TypedData = new GenericToolData
            {
                Content = content
            }
        };
    }

    // Helper methods

    private static string? TryExtractFilePath(string content)
    {
        // Look for common file path patterns
        // Pattern 1: "File created: /path/to/file" or similar
        var filePathMatch = FilePathMessageRegex().Match(content);
        if (filePathMatch.Success)
            return filePathMatch.Groups[1].Value;

        // Pattern 2: Standalone path-like string at start
        var standaloneMatch = StandalonePathRegex().Match(content);
        if (standaloneMatch.Success)
            return standaloneMatch.Value;

        // Pattern 3: Look for path in the content
        var pathMatch = PathInContentRegex().Match(content);
        if (pathMatch.Success)
            return pathMatch.Value;

        return null;
    }

    private static string? DetectLanguageFromPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".jsx" => "javascript",
            ".py" => "python",
            ".rb" => "ruby",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" or ".h" => "c",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" or ".sass" => "scss",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            ".sql" => "sql",
            ".sh" or ".bash" => "bash",
            ".ps1" => "powershell",
            ".razor" => "razor",
            _ => null
        };
    }

    private static string? TryExtractCommand(string content)
    {
        // Look for command patterns like "$ command" or lines starting with common commands
        var commandMatch = CommandPrefixRegex().Match(content);
        if (commandMatch.Success)
            return commandMatch.Groups[1].Value.Trim();

        return null;
    }

    private static string TruncateForSummary(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        // Get first line and truncate
        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.Length <= maxLength)
            return firstLine;

        return firstLine[..(maxLength - 3)] + "...";
    }

    private static string TruncateFilePath(string path, int maxLength)
    {
        if (path.Length <= maxLength)
            return path;

        // Keep the filename and as much of the path as possible
        var fileName = Path.GetFileName(path);
        if (fileName.Length >= maxLength - 3)
            return "..." + fileName[^(maxLength - 3)..];

        var remainingLength = maxLength - fileName.Length - 4; // ".../" takes 4 chars
        if (remainingLength <= 0)
            return ".../" + fileName;

        var directory = Path.GetDirectoryName(path) ?? "";
        if (directory.Length <= remainingLength)
            return path;

        return "..." + directory[^remainingLength..] + "/" + fileName;
    }

    private static string? ExtractFirstMeaningfulLine(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Skip empty lines, headers, separators
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            if (trimmed.All(c => c == '-' || c == '=' || c == '#'))
                continue;
            if (trimmed.Length < 3)
                continue;

            return trimmed;
        }
        return null;
    }

    private static List<GrepMatch> ParseGrepMatches(string content)
    {
        var matches = new List<GrepMatch>();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Try to parse grep output format: "file:line:content" or just "file"
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var filePath = line[..colonIndex];

                // Check if next part is a line number
                var remaining = line[(colonIndex + 1)..];
                var secondColonIndex = remaining.IndexOf(':');

                if (secondColonIndex > 0 && int.TryParse(remaining[..secondColonIndex], out var lineNum))
                {
                    matches.Add(new GrepMatch
                    {
                        FilePath = filePath,
                        LineNumber = lineNum,
                        Content = remaining[(secondColonIndex + 1)..]
                    });
                }
                else
                {
                    matches.Add(new GrepMatch
                    {
                        FilePath = filePath,
                        Content = remaining
                    });
                }
            }
            else
            {
                // Just a file path
                matches.Add(new GrepMatch
                {
                    FilePath = line.Trim()
                });
            }
        }

        return matches;
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return content.Split('\n').Length;
    }

    // Compiled regex patterns for performance
    [GeneratedRegex(@"(?:File|Created|Updated|Written|Edited)[^:]*:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathMessageRegex();

    [GeneratedRegex(@"^(/[^\s]+|[A-Za-z]:\\[^\s]+)")]
    private static partial Regex StandalonePathRegex();

    [GeneratedRegex(@"(/[\w./\-_]+\.\w+|[A-Za-z]:\\[\w\\./\-_]+\.\w+)")]
    private static partial Regex PathInContentRegex();

    [GeneratedRegex(@"^\$\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex CommandPrefixRegex();
}
