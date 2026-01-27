namespace Homespun.Features.Git;

/// <summary>
/// Interface for Git worktree operations.
/// </summary>
public interface IGitWorktreeService
{
    Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null);
    Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath);
    Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath);
    Task PruneWorktreesAsync(string repoPath);
    Task<bool> WorktreeExistsAsync(string repoPath, string branchName);

    /// <summary>
    /// Gets the worktree path for a given branch name, accounting for branch name sanitization.
    /// First checks for a direct branch name match, then falls back to matching the sanitized
    /// branch name against worktree paths.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">The original branch name (may contain special characters like +)</param>
    /// <returns>The worktree path if found, null otherwise</returns>
    Task<string?> GetWorktreePathForBranchAsync(string repoPath, string branchName);


    /// <summary>
    /// Pull the latest changes from the remote for a worktree.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree directory</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> PullLatestAsync(string worktreePath);

    /// <summary>
    /// Fetch and update a specific branch from remote to ensure it's up to date.
    /// This fetches the branch and updates the local ref without checking it out.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to update (e.g., "main")</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName);

    /// <summary>
    /// List all local branches in the repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>List of branch information</returns>
    Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath);

    /// <summary>
    /// List all remote branches that don't have a corresponding local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>List of remote-only branch names</returns>
    Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath);

    /// <summary>
    /// Check if a branch has been merged into the default branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to check</param>
    /// <param name="targetBranch">Target branch to check against (usually the default branch)</param>
    /// <returns>True if the branch has been merged</returns>
    Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Delete a local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="force">Force delete even if not merged</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false);

    /// <summary>
    /// Delete a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName);

    /// <summary>
    /// Create a local branch from a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteBranch">Name of the remote branch (e.g., "origin/feature-branch")</param>
    /// <returns>True if successful</returns>
    Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch);

    /// <summary>
    /// Get the commit count difference between two branches.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to compare</param>
    /// <param name="targetBranch">Target branch to compare against</param>
    /// <returns>Tuple of (ahead count, behind count)</returns>
    Task<(int ahead, int behind)> GetBranchDivergenceAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Fetch all remote branches.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>True if successful</returns>
    Task<bool> FetchAllAsync(string repoPath);
}
