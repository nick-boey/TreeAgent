using System.Collections.Concurrent;
using Homespun.Features.Git;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGitWorktreeService with in-memory worktree tracking.
/// </summary>
public class MockGitWorktreeService : IGitWorktreeService
{
    private readonly ConcurrentDictionary<string, List<WorktreeInfo>> _worktreesByRepo = new();
    private readonly ConcurrentDictionary<string, List<BranchInfo>> _branchesByRepo = new();
    private readonly ILogger<MockGitWorktreeService> _logger;

    public MockGitWorktreeService(ILogger<MockGitWorktreeService> logger)
    {
        _logger = logger;
    }

    public Task<string?> CreateWorktreeAsync(
        string repoPath,
        string branchName,
        bool createBranch = false,
        string? baseBranch = null)
    {
        _logger.LogDebug("[Mock] CreateWorktree {BranchName} in {RepoPath}", branchName, repoPath);

        var worktreePath = $"{repoPath}-worktrees/{branchName.Replace("/", "-")}";

        var worktrees = _worktreesByRepo.GetOrAdd(repoPath, _ => []);
        lock (worktrees)
        {
            worktrees.Add(new WorktreeInfo
            {
                Path = worktreePath,
                Branch = branchName,
                HeadCommit = Guid.NewGuid().ToString("N")[..7],
                IsBare = false,
                IsDetached = false
            });
        }

        // Also track the branch
        var branches = _branchesByRepo.GetOrAdd(repoPath, _ => [new BranchInfo { Name = "main", ShortName = "main", IsCurrent = true }]);
        lock (branches)
        {
            if (!branches.Any(b => b.ShortName == branchName))
            {
                branches.Add(new BranchInfo
                {
                    Name = branchName,
                    ShortName = branchName,
                    IsCurrent = false,
                    HasWorktree = true,
                    WorktreePath = worktreePath
                });
            }
        }

        return Task.FromResult<string?>(worktreePath);
    }

    public Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath)
    {
        _logger.LogDebug("[Mock] RemoveWorktree {WorktreePath} from {RepoPath}", worktreePath, repoPath);

        if (_worktreesByRepo.TryGetValue(repoPath, out var worktrees))
        {
            lock (worktrees)
            {
                var removed = worktrees.RemoveAll(w => w.Path == worktreePath) > 0;
                return Task.FromResult(removed);
            }
        }

        return Task.FromResult(false);
    }

    public Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListWorktrees in {RepoPath}", repoPath);

        if (_worktreesByRepo.TryGetValue(repoPath, out var worktrees))
        {
            lock (worktrees)
            {
                return Task.FromResult(worktrees.ToList());
            }
        }

        // Return default worktree (the main repo)
        return Task.FromResult(new List<WorktreeInfo>
        {
            new()
            {
                Path = repoPath,
                Branch = "main",
                HeadCommit = "abc1234",
                IsBare = false,
                IsDetached = false
            }
        });
    }

    public Task PruneWorktreesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] PruneWorktrees in {RepoPath}", repoPath);
        return Task.CompletedTask;
    }

    public Task<bool> WorktreeExistsAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] WorktreeExists {BranchName} in {RepoPath}", branchName, repoPath);

        if (_worktreesByRepo.TryGetValue(repoPath, out var worktrees))
        {
            lock (worktrees)
            {
                return Task.FromResult(worktrees.Any(w => w.Branch == branchName));
            }
        }

        return Task.FromResult(false);
    }

    public Task<string?> GetWorktreePathForBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] GetWorktreePathForBranch {BranchName} in {RepoPath}", branchName, repoPath);

        if (_worktreesByRepo.TryGetValue(repoPath, out var worktrees))
        {
            lock (worktrees)
            {
                var worktree = worktrees.FirstOrDefault(w => w.Branch == branchName);
                return Task.FromResult(worktree?.Path);
            }
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> PullLatestAsync(string worktreePath)
    {
        _logger.LogDebug("[Mock] PullLatest in {WorktreePath}", worktreePath);
        return Task.FromResult(true);
    }

    public Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] FetchAndUpdateBranch {BranchName} in {RepoPath}", branchName, repoPath);
        return Task.FromResult(true);
    }

    public Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListLocalBranches in {RepoPath}", repoPath);

        if (_branchesByRepo.TryGetValue(repoPath, out var branches))
        {
            lock (branches)
            {
                return Task.FromResult(branches.ToList());
            }
        }

        // Return default branches
        return Task.FromResult(new List<BranchInfo>
        {
            new()
            {
                Name = "main",
                ShortName = "main",
                IsCurrent = true,
                CommitSha = "abc1234567890",
                Upstream = "origin/main"
            }
        });
    }

    public Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListRemoteOnlyBranches in {RepoPath}", repoPath);
        return Task.FromResult(new List<string>());
    }

    public Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        _logger.LogDebug("[Mock] IsBranchMerged {BranchName} into {TargetBranch} in {RepoPath}",
            branchName, targetBranch, repoPath);
        return Task.FromResult(false);
    }

    public Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false)
    {
        _logger.LogDebug("[Mock] DeleteLocalBranch {BranchName} in {RepoPath}", branchName, repoPath);

        if (_branchesByRepo.TryGetValue(repoPath, out var branches))
        {
            lock (branches)
            {
                branches.RemoveAll(b => b.ShortName == branchName);
            }
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] DeleteRemoteBranch {BranchName} in {RepoPath}", branchName, repoPath);
        return Task.FromResult(true);
    }

    public Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch)
    {
        _logger.LogDebug("[Mock] CreateLocalBranchFromRemote {RemoteBranch} in {RepoPath}", remoteBranch, repoPath);

        var localBranchName = remoteBranch.Replace("origin/", "");
        var branches = _branchesByRepo.GetOrAdd(repoPath, _ => []);
        lock (branches)
        {
            branches.Add(new BranchInfo
            {
                Name = localBranchName,
                ShortName = localBranchName,
                IsCurrent = false,
                Upstream = remoteBranch
            });
        }

        return Task.FromResult(true);
    }

    public Task<(int ahead, int behind)> GetBranchDivergenceAsync(
        string repoPath,
        string branchName,
        string targetBranch)
    {
        _logger.LogDebug("[Mock] GetBranchDivergence {BranchName} vs {TargetBranch} in {RepoPath}",
            branchName, targetBranch, repoPath);
        return Task.FromResult((ahead: 1, behind: 0));
    }

    public Task<bool> FetchAllAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] FetchAll in {RepoPath}", repoPath);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Clears all tracked data. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        _worktreesByRepo.Clear();
        _branchesByRepo.Clear();
    }
}
