using System.Text.RegularExpressions;
using Homespun.Features.Commands;

namespace Homespun.Features.Git;

public class GitWorktreeService(ICommandRunner commandRunner) : IGitWorktreeService
{
    public GitWorktreeService() : this(new CommandRunner())
    {
    }

    public async Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null)
    {
        var sanitizedName = SanitizeBranchName(branchName);
        var worktreePath = Path.Combine(repoPath, ".worktrees", sanitizedName);

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

        return result.Success ? worktreePath : null;
    }

    public async Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath)
    {
        var result = await commandRunner.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath);
        return result.Success;
    }

    public async Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        var result = await commandRunner.RunAsync("git", "worktree list --porcelain", repoPath);

        if (!result.Success)
            return [];

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