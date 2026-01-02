using System.Diagnostics;

namespace TreeAgent.Web.Tests.Integration.Fixtures;

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
        RepositoryPath = Path.Combine(Path.GetTempPath(), "TreeAgent_IntegrationTests", Guid.NewGuid().ToString("N"));
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
            // Clean up the temp directory
            if (Directory.Exists(RepositoryPath))
            {
                // Force delete all files (handle read-only files in .git)
                ForceDeleteDirectory(RepositoryPath);
            }
        }
        catch
        {
            // Best effort cleanup
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
