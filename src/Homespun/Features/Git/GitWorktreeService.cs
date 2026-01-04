using System.Text.RegularExpressions;
using Homespun.Features.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Features.Git;

public class GitWorktreeService(ICommandRunner commandRunner, ILogger<GitWorktreeService> logger) : IGitWorktreeService
{
    public GitWorktreeService() : this(new CommandRunner(), NullLogger<GitWorktreeService>.Instance)
    {
    }

    public async Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null)
    {
        logger.LogDebug(
            "Creating worktree for branch {BranchName} in repo {RepoPath} (createBranch={CreateBranch}, baseBranch={BaseBranch})",
            branchName, repoPath, createBranch, baseBranch);
        
        var sanitizedName = SanitizeBranchName(branchName);
        // Create worktree as sibling of the main repo, not inside it
        // e.g., ~/.homespun/src/repo/main -> ~/.homespun/src/repo/<branch-name>
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            logger.LogError("Cannot determine parent directory of {RepoPath}", repoPath);
            throw new InvalidOperationException($"Cannot determine parent directory of {repoPath}");
        }
        
        var worktreePath = Path.Combine(parentDir, sanitizedName);
        logger.LogDebug("Worktree path will be {WorktreePath}", worktreePath);

        if (createBranch)
        {
            var baseRef = baseBranch ?? "HEAD";
            logger.LogDebug("Creating branch {BranchName} from {BaseRef}", branchName, baseRef);
            
            var branchResult = await commandRunner.RunAsync("git", $"branch \"{branchName}\" \"{baseRef}\"", repoPath);
            if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
            {
                logger.LogError(
                    "Failed to create branch {BranchName} from {BaseRef}: {Error}",
                    branchName, baseRef, branchResult.Error);
                return null;
            }
            
            if (branchResult.Error.Contains("already exists"))
            {
                logger.LogDebug("Branch {BranchName} already exists, continuing", branchName);
            }
        }

        var args = $"worktree add \"{worktreePath}\" \"{branchName}\"";
        logger.LogDebug("Running: git {Args}", args);
        
        var result = await commandRunner.RunAsync("git", args, repoPath);

        if (!result.Success)
        {
            logger.LogError(
                "Failed to create worktree at {WorktreePath} for branch {BranchName}: {Error}",
                worktreePath, branchName, result.Error);
            return null;
        }
        
        logger.LogInformation(
            "Created worktree at {WorktreePath} for branch {BranchName}",
            worktreePath, branchName);
        
        return worktreePath;
    }

    public async Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath)
    {
        logger.LogDebug("Removing worktree {WorktreePath} from repo {RepoPath}", worktreePath, repoPath);
        
        var result = await commandRunner.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath);
        
        if (!result.Success)
        {
            logger.LogError("Failed to remove worktree {WorktreePath}: {Error}", worktreePath, result.Error);
        }
        else
        {
            logger.LogInformation("Removed worktree {WorktreePath}", worktreePath);
        }
        
        return result.Success;
    }

    public async Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        logger.LogDebug("Listing worktrees in repo {RepoPath}", repoPath);
        
        var result = await commandRunner.RunAsync("git", "worktree list --porcelain", repoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to list worktrees in {RepoPath}: {Error}", repoPath, result.Error);
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
        logger.LogDebug("Pulling latest changes in worktree {WorktreePath}", worktreePath);
        
        // First fetch from remote
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", worktreePath);
        if (!fetchResult.Success)
        {
            // Fetch might fail if no remote tracking, that's ok for local-only branches
            logger.LogDebug("Fetch failed (may be expected for local branches): {Error}", fetchResult.Error);
        }

        // Try to pull with rebase to get latest changes
        var pullResult = await commandRunner.RunAsync("git", "pull --rebase --autostash", worktreePath);
        
        // Pull might fail if no upstream is set, which is fine for new branches
        var success = pullResult.Success || 
                      pullResult.Error.Contains("no tracking information") ||
                      pullResult.Error.Contains("There is no tracking information");
        
        if (success)
        {
            logger.LogDebug("Pull completed for {WorktreePath}", worktreePath);
        }
        else
        {
            logger.LogWarning("Failed to pull latest in {WorktreePath}: {Error}", worktreePath, pullResult.Error);
        }
        
        return success;
    }

    public static string SanitizeBranchName(string branchName)
    {
        // Replace slashes and special characters with dashes
        var sanitized = Regex.Replace(branchName, @"[/\\@#\s]+", "-");
        // Remove any remaining invalid characters
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-_.]", "-");
        // Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        // Trim dashes from ends
        return sanitized.Trim('-');
    }
}