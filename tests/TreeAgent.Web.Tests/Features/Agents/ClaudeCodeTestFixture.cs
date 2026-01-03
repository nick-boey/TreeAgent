using System.Diagnostics;
using TreeAgent.Web.Features.Agents.Services;

namespace TreeAgent.Web.Tests.Features.Agents;

/// <summary>
/// Fixture for Claude Code integration tests.
/// Verifies that Claude Code is available and provides helper methods for testing.
/// </summary>
public class ClaudeCodeTestFixture : IDisposable
{
    public string WorkingDirectory { get; }
    public bool IsClaudeCodeAvailable { get; }
    public string ClaudeCodePath { get; }

    private bool _disposed;

    public ClaudeCodeTestFixture()
    {
        // Create a temp working directory
        WorkingDirectory = Path.Combine(Path.GetTempPath(), "TreeAgent_ClaudeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkingDirectory);

        // Create a minimal test file for Claude to work with
        File.WriteAllText(Path.Combine(WorkingDirectory, "test.txt"), "Hello, World!");

        // Determine Claude Code path using the resolver (checks env var and default locations)
        ClaudeCodePath = new ClaudeCodePathResolver().Resolve();

        // Check if Claude Code is available
        IsClaudeCodeAvailable = CheckClaudeCodeAvailable();
    }

    private bool CheckClaudeCodeAvailable()
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
            process.WaitForExit(5000);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the Claude Code version string, or null if not available.
    /// </summary>
    public string? GetClaudeCodeVersion()
    {
        if (!IsClaudeCodeAvailable)
            return null;

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

            return output.Trim();
        }
        catch
        {
            return null;
        }
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
