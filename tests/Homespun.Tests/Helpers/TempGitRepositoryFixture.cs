using System.Diagnostics;

namespace Homespun.Tests.Helpers;

/// <summary>
/// Fixture that creates a temporary git repository for integration testing.
/// Automatically cleans up the repository when disposed.
/// </summary>
public class TempGitRepositoryFixture : IDisposable
{
    public string RepositoryPath { get; }
    public string InitialCommitHash { get; private set; } = "";

    private bool _disposed;

    public TempGitRepositoryFixture()
    {
        // Create a unique temp directory
        RepositoryPath = Path.Combine(Path.GetTempPath(), "Homespun_IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RepositoryPath);

        InitializeRepository();
    }

    private void InitializeRepository()
    {
        // Initialize git repo
        RunGit("init");

        // Configure git user for commits
        RunGit("config user.email \"test@example.com\"");
        RunGit("config user.name \"Test User\"");

        // Create an initial commit so we have a valid HEAD
        var readmePath = Path.Combine(RepositoryPath, "README.md");
        File.WriteAllText(readmePath, "# Test Repository\n\nThis is a test repository for integration tests.");

        RunGit("add .");
        RunGit("commit -m \"Initial commit\"");

        // Get the initial commit hash
        InitialCommitHash = RunGit("rev-parse HEAD").Trim();
    }

    /// <summary>
    /// Creates a new branch in the test repository.
    /// </summary>
    public void CreateBranch(string branchName, bool checkout = false)
    {
        RunGit($"branch \"{branchName}\"");
        if (checkout)
        {
            RunGit($"checkout \"{branchName}\"");
        }
    }

    /// <summary>
    /// Creates a file and commits it to the repository.
    /// </summary>
    public void CreateFileAndCommit(string fileName, string content, string commitMessage)
    {
        var filePath = Path.Combine(RepositoryPath, fileName);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
        RunGit($"add \"{fileName}\"");
        RunGit($"commit -m \"{commitMessage}\"");
    }

    /// <summary>
    /// Runs a git command in the test repository.
    /// </summary>
    public string RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = RepositoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: git {arguments}\nError: {error}");
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(RepositoryPath))
            {
                // First, get list of worktrees and clean them up
                CleanupWorktrees();

                // Force delete all files (handle read-only files in .git)
                ForceDeleteDirectory(RepositoryPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Cleans up any worktrees created during testing.
    /// </summary>
    private void CleanupWorktrees()
    {
        try
        {
            // Get list of worktrees
            var output = RunGit("worktree list --porcelain");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var worktreePaths = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("worktree "))
                {
                    var path = line.Substring(9).Trim();
                    // Don't try to remove the main worktree (the repo itself)
                    if (!path.Equals(RepositoryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        worktreePaths.Add(path);
                    }
                }
            }

            // Remove each worktree
            foreach (var path in worktreePaths)
            {
                try
                {
                    RunGit($"worktree remove \"{path}\" --force");
                }
                catch
                {
                    // Best effort - if git remove fails, try to delete the directory manually
                }

                // Also try to delete the directory if it still exists
                if (Directory.Exists(path))
                {
                    try
                    {
                        ForceDeleteDirectory(path);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }
        }
        catch
        {
            // Best effort - worktree cleanup is not critical
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }
}
