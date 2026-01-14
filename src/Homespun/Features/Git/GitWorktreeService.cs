using System.Text.RegularExpressions;
using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Features.Git;

public class GitWorktreeService(ICommandRunner commandRunner, ILogger<GitWorktreeService> logger) : IGitWorktreeService
{
    public GitWorktreeService() : this(
        new CommandRunner(
            new NullGitHubEnvironmentService(),
            NullLogger<CommandRunner>.Instance),
        NullLogger<GitWorktreeService>.Instance)
    {
    }

    /// <summary>
    /// A no-op implementation of IGitHubEnvironmentService for testing scenarios
    /// where GitHub authentication is not needed.
    /// </summary>
    private class NullGitHubEnvironmentService : IGitHubEnvironmentService
    {
        public bool IsConfigured => false;
        public IDictionary<string, string> GetGitHubEnvironment() => new Dictionary<string, string>();
        public string? GetMaskedToken() => null;
        public Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new GitHubAuthStatus { IsAuthenticated = false, AuthMethod = GitHubAuthMethod.None });
    }

    public async Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null)
    {
        var sanitizedName = SanitizeBranchName(branchName);
        // Create worktree as sibling of the main repo, not inside it
        // e.g., ~/.homespun/src/repo/main -> ~/.homespun/src/repo/<branch-name>
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            logger.LogError("Cannot determine parent directory of {RepoPath}", repoPath);
            throw new InvalidOperationException($"Cannot determine parent directory of {repoPath}");
        }
        
        // Normalize the path to use platform-native separators
        // This fixes issues on Windows where mixed forward/back slashes cause problems
        var worktreePath = Path.GetFullPath(Path.Combine(parentDir, sanitizedName));

        // Ensure parent directories exist for nested branch names (e.g., app/feature/id)
        var worktreeParentDir = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrEmpty(worktreeParentDir) && !Directory.Exists(worktreeParentDir))
        {
            Directory.CreateDirectory(worktreeParentDir);
        }

        if (createBranch)
        {
            var baseRef = baseBranch ?? "HEAD";
            
            var branchResult = await commandRunner.RunAsync("git", $"branch \"{branchName}\" \"{baseRef}\"", repoPath);
            if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
            {
                return null;
            }
        }

        var args = $"worktree add \"{worktreePath}\" \"{branchName}\"";
        var result = await commandRunner.RunAsync("git", args, repoPath);

        if (!result.Success)
        {
            return null;
        }
        
        logger.LogInformation("Created worktree at {WorktreePath} for branch {BranchName}", worktreePath, branchName);
        
        return worktreePath;
    }

    public async Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath)
    {
        var result = await commandRunner.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath);
        
        if (result.Success)
        {
            logger.LogInformation("Removed worktree {WorktreePath}", worktreePath);
        }
        
        return result.Success;
    }

    public async Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        var result = await commandRunner.RunAsync("git", "worktree list --porcelain", repoPath);

        if (!result.Success)
        {
            return [];
        }

        var worktrees = new List<WorktreeInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        WorktreeInfo? current = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                if (current != null)
                    worktrees.Add(current);
                current = new WorktreeInfo { Path = line[9..].Trim() };
            }
            else if (line.StartsWith("HEAD ") && current != null)
            {
                current.HeadCommit = line[5..].Trim();
            }
            else if (line.StartsWith("branch ") && current != null)
            {
                current.Branch = line[7..].Trim();
            }
            else if (line == "bare" && current != null)
            {
                current.IsBare = true;
            }
            else if (line == "detached" && current != null)
            {
                current.IsDetached = true;
            }
        }

        if (current != null)
            worktrees.Add(current);

        return worktrees;
    }

    public async Task PruneWorktreesAsync(string repoPath)
    {
        await commandRunner.RunAsync("git", "worktree prune", repoPath);
    }

    public async Task<bool> WorktreeExistsAsync(string repoPath, string branchName)
    {
        var worktrees = await ListWorktreesAsync(repoPath);
        return worktrees.Any(w => w.Branch?.EndsWith(branchName) == true);
    }

    public async Task<bool> PullLatestAsync(string worktreePath)
    {
        // First fetch from remote (may fail for local-only branches, which is OK)
        await commandRunner.RunAsync("git", "fetch origin", worktreePath);

        // Try to pull with rebase to get latest changes
        var pullResult = await commandRunner.RunAsync("git", "pull --rebase --autostash", worktreePath);
        
        // Pull might fail if no upstream is set, which is fine for new branches
        return pullResult.Success || 
               pullResult.Error.Contains("no tracking information") ||
               pullResult.Error.Contains("There is no tracking information");
    }

    public async Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName)
    {
        // Fetch the specific branch from origin
        var fetchResult = await commandRunner.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath);
        
        if (!fetchResult.Success)
        {
            // Try a simple fetch if the branch update fails (might be checked out)
            var simpleFetchResult = await commandRunner.RunAsync("git", "fetch origin", repoPath);
            
            if (!simpleFetchResult.Success)
            {
                return false;
            }
        }

        return true;
    }

    public static string SanitizeBranchName(string branchName)
    {
        // Normalize path separators to forward slashes
        var sanitized = branchName.Replace('\\', '/');
        // Replace special characters (except forward slash) with dashes
        sanitized = Regex.Replace(sanitized, @"[@#\s]+", "-");
        // Remove any remaining invalid characters (keep forward slashes for folder structure)
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-_./]", "-");
        // Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        // Remove consecutive slashes
        sanitized = Regex.Replace(sanitized, @"/+", "/");
        // Trim dashes and slashes from ends
        return sanitized.Trim('-', '/');
    }
}