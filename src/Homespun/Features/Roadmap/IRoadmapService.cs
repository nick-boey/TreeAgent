using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Interface for ROADMAP.json operations.
/// </summary>
public interface IRoadmapService
{
    Task<string?> GetRoadmapPathAsync(string projectId);
    Task<Roadmap?> LoadRoadmapAsync(string projectId);
    Task<List<FutureChangeWithTime>> GetFutureChangesAsync(string projectId);
    Task<Dictionary<string, List<FutureChangeWithTime>>> GetFutureChangesByGroupAsync(string projectId);
    Task<FutureChange?> FindChangeByIdAsync(string projectId, string changeId);
    Task<PullRequest?> PromoteChangeAsync(string projectId, string changeId);
    Task<bool> IsPlanUpdateOnlyAsync(string pullRequestId);
    Task<bool> ValidateRoadmapAsync(string pullRequestId);
    Task<PullRequest?> CreatePlanUpdatePullRequestAsync(string projectId, string description);
    string GeneratePlanUpdateBranchName(string description);
    
    /// <summary>
    /// Adds a new change to the roadmap. If no ROADMAP.json exists, creates one.
    /// </summary>
    Task<bool> AddChangeAsync(string projectId, FutureChange change);

    /// <summary>
    /// Updates the status of a change in the roadmap.
    /// </summary>
    Task<bool> UpdateChangeStatusAsync(string projectId, string changeId, FutureChangeStatus status);

    /// <summary>
    /// Removes a parent reference from all changes that reference it.
    /// Used when a parent change is promoted to a PR.
    /// </summary>
    Task<bool> RemoveParentReferenceAsync(string projectId, string parentId);

    /// <summary>
    /// Creates a git worktree for a FutureChange without promoting it to a PR.
    /// Updates the change's WorktreePath property in the roadmap.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The change ID (branch name)</param>
    /// <returns>The worktree path, or null if creation failed</returns>
    Task<string?> CreateWorktreeForChangeAsync(string projectId, string changeId);

    /// <summary>
    /// Updates the active agent server ID for a change.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The change ID</param>
    /// <param name="agentServerId">The agent server ID, or null to clear</param>
    Task<bool> UpdateChangeAgentAsync(string projectId, string changeId, string? agentServerId);

    /// <summary>
    /// Promotes a completed FutureChange to a PullRequest after confirming the GitHub PR exists.
    /// Removes the change from the roadmap and creates a PR record in homespun-data.json.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="changeId">The change ID (branch name)</param>
    /// <param name="prNumber">The GitHub PR number</param>
    /// <returns>The created PullRequest, or null if promotion failed</returns>
    Task<PullRequest?> PromoteCompletedChangeAsync(string projectId, string changeId, int prNumber);
}
