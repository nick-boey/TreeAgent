using System.Diagnostics;
using TreeAgent.Web.Features.Agents.Services;
using TreeAgent.Web.Tests.Helpers;

namespace TreeAgent.Web.Tests.Features.Agents;

/// <summary>
/// Fixture for Claude Code integration tests using query mode.
/// Creates isolated working directories and provides ClaudeCodeTestProcess instances.
/// Designed to support parallel test execution.
/// </summary>
public class ClaudeCodeQueryFixture : IDisposable
{
    public string WorkingDirectory { get; }
    public bool IsClaudeCodeAvailable { get; }
    public string ClaudeCodePath { get; }
    public string? ClaudeCodeVersion { get; }

    private bool _disposed;

    public ClaudeCodeQueryFixture()
    {
        // Create a unique temp working directory for this fixture instance
        // Using Guid ensures isolation even when tests run in parallel
        WorkingDirectory = Path.Combine(
            Path.GetTempPath(),
            "TreeAgent_ClaudeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkingDirectory);

        // Create a minimal test file for Claude to work with
        File.WriteAllText(Path.Combine(WorkingDirectory, "test.txt"), "Hello, World!");

        // Determine Claude Code path using the resolver
        ClaudeCodePath = new ClaudeCodePathResolver().Resolve();

        // Check availability and get version
        (IsClaudeCodeAvailable, ClaudeCodeVersion) = CheckClaudeCodeAvailable();
    }

    private (bool available, string? version) CheckClaudeCodeAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ClaudeCodePath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                return (true, output.Trim());
            }
            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Creates a new test process for executing queries.
    /// Each call creates a new process instance that can run independently.
    /// </summary>
    public ClaudeCodeTestProcess CreateTestProcess(string? systemPrompt = null)
    {
        return new ClaudeCodeTestProcess(ClaudeCodePath, WorkingDirectory, systemPrompt);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(WorkingDirectory))
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
