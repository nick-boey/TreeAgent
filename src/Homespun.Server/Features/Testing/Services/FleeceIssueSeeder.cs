using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Helper service to write Fleece issues as JSONL files.
/// Matches Fleece.Core's serialization format for compatibility with the real FleeceService.
/// </summary>
public sealed class FleeceIssueSeeder
{
    private readonly ILogger<FleeceIssueSeeder> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FleeceIssueSeeder(ILogger<FleeceIssueSeeder> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    /// <summary>
    /// Seeds issues as the lean v3.1 snapshot at `{projectPath}/.fleece/issues.jsonl`.
    /// A fresh repo with no active change file is a valid v3.1 starting state — replay
    /// over an empty event log is a no-op, so no `.fleece/changes/` files are needed.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory.</param>
    /// <param name="issues">The issues to seed.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SeedIssuesAsync(string projectPath, IReadOnlyList<Issue> issues, CancellationToken ct = default)
    {
        if (issues.Count == 0)
        {
            _logger.LogDebug("No issues to seed for project: {ProjectPath}", projectPath);
            return;
        }

        var fleeceDir = Path.Combine(projectPath, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        var issuesFilePath = Path.Combine(fleeceDir, "issues.jsonl");

        await using var writer = new StreamWriter(issuesFilePath, false);
        foreach (var issue in issues)
        {
            var json = JsonSerializer.Serialize(issue, _jsonOptions);
            await writer.WriteLineAsync(json);
        }

        _logger.LogDebug(
            "Seeded {Count} issues to {FilePath}",
            issues.Count,
            issuesFilePath);
    }

    /// <summary>
    /// Creates a minimal project structure with .gitignore and README.md files.
    /// This ensures the project directory is valid for FleeceService operations.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory.</param>
    /// <param name="projectName">The display name of the project.</param>
    public void CreateMinimalProjectStructure(string projectPath, string projectName)
    {
        Directory.CreateDirectory(projectPath);

        // Create .gitignore
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            File.WriteAllText(gitignorePath, """
                # IDE
                .idea/
                .vscode/
                *.swp

                # Build
                bin/
                obj/
                node_modules/

                # OS
                .DS_Store
                Thumbs.db
                """);
        }

        // Create README.md
        var readmePath = Path.Combine(projectPath, "README.md");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath, $"""
                # {projectName}

                This is a mock project for testing and demonstration purposes.
                """);
        }

        _logger.LogDebug("Created minimal project structure at {ProjectPath}", projectPath);
    }

    /// <summary>
    /// Initializes a git repository in the given project path with an initial commit.
    /// This makes the temp folder a real git repo that supports git operations.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory.</param>
    public void InitializeGitRepository(string projectPath)
    {
        RunGitCommand(projectPath, "init");
        RunGitCommand(projectPath, "config user.name 'Test'");
        RunGitCommand(projectPath, "config user.email 'test@test.com'");
        RunGitCommand(projectPath, "add -A");
        RunGitCommand(projectPath, "commit -m 'Initial commit'");

        _logger.LogDebug("Initialized git repository at {ProjectPath}", projectPath);
    }

    private void RunGitCommand(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            _logger.LogWarning(
                "Git command failed: git {Arguments} in {WorkingDirectory} - {Error}",
                arguments, workingDirectory, error);
        }
    }
}
