using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Roadmap.Sync;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Service for managing ROADMAP.json and future changes.
/// Uses ROADMAP.local.json as the source of truth and syncs to worktrees.
/// </summary>
public class RoadmapService : IRoadmapService
{
    private const string LocalRoadmapFileName = "ROADMAP.local.json";
    private const string RoadmapFileName = "ROADMAP.json";

    private readonly IDataStore _dataStore;
    private readonly ICommandRunner _commandRunner;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IRoadmapSyncService? _syncService;
    private readonly ILogger<RoadmapService> _logger;

    public RoadmapService(
        IDataStore dataStore,
        ICommandRunner commandRunner,
        IGitWorktreeService worktreeService,
        ILogger<RoadmapService> logger,
        IRoadmapSyncService? syncService = null)
    {
        _dataStore = dataStore;
        _commandRunner = commandRunner;
        _worktreeService = worktreeService;
        _logger = logger;
        _syncService = syncService;
    }

    /// <summary>
    /// Gets the path to the ROADMAP.local.json file for a project.
    /// This is the single source of truth for the roadmap.
    /// </summary>
    public Task<string?> GetRoadmapPathAsync(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return Task.FromResult<string?>(null);

        // Return path to ROADMAP.local.json at the project root level
        var parentDir = Path.GetDirectoryName(project.LocalPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(Path.Combine(parentDir, LocalRoadmapFileName));
    }

    /// <summary>
    /// Loads and parses the ROADMAP.local.json file for a project.
    /// </summary>
    public async Task<Roadmap?> LoadRoadmapAsync(string projectId)
    {
        var path = await GetRoadmapPathAsync(projectId);
        if (path == null || !File.Exists(path))
            return null;

        return await RoadmapParser.LoadAsync(path);
    }

    /// <summary>
    /// Gets all future changes with their calculated time values.
    /// </summary>
    public async Task<List<FutureChangeWithTime>> GetFutureChangesAsync(string projectId)
    {
        var roadmap = await LoadRoadmapAsync(projectId);
        if (roadmap == null)
            return [];

        return roadmap.GetAllChangesWithTime()
            .Select(c => new FutureChangeWithTime(c.Change, c.Time, c.Depth))
            .ToList();
    }

    /// <summary>
    /// Gets future changes grouped by their group field.
    /// </summary>
    public async Task<Dictionary<string, List<FutureChangeWithTime>>> GetFutureChangesByGroupAsync(string projectId)
    {
        var changes = await GetFutureChangesAsync(projectId);

        return changes
            .GroupBy(c => c.Change.Group)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Finds a specific change by ID in the roadmap.
    /// </summary>
    public async Task<FutureChange?> FindChangeByIdAsync(string projectId, string changeId)
    {
        var roadmap = await LoadRoadmapAsync(projectId);
        return roadmap?.Changes.FirstOrDefault(c => c.Id == changeId);
    }

    /// <summary>
    /// Promotes a future change to an active pull request with a worktree.
    /// Removes the change from ROADMAP.local.json and syncs to all worktrees.
    /// </summary>
    public async Task<PullRequest?> PromoteChangeAsync(string projectId, string changeId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return null;

        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return null;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return null;

        // The Id IS the branch name in the new schema
        var branchName = change.Id;

        // Create worktree
        var worktreePath = await _worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            // Try without creating branch if it already exists
            worktreePath = await _worktreeService.CreateWorktreeAsync(
                project.LocalPath,
                branchName);
        }

        // Create pull request entry
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = change.Title,
            Description = change.Description,
            BranchName = branchName,
            Status = OpenPullRequestStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        await _dataStore.AddPullRequestAsync(pullRequest);

        // Update roadmap - remove the promoted change and update parent references
        await RemoveChangeAndUpdateParentsAsync(roadmap, changeId, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        return pullRequest;
    }

    private async Task RemoveChangeAndUpdateParentsAsync(Roadmap roadmap, string changeId, string roadmapPath)
    {
        // Remove the change from the list
        var changeToRemove = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (changeToRemove == null) return;

        roadmap.Changes.Remove(changeToRemove);

        // Remove this change's ID from all other changes' parent lists
        foreach (var change in roadmap.Changes)
        {
            change.Parents.Remove(changeId);
        }

        await RoadmapParser.SaveAsync(roadmap, roadmapPath);
    }

    /// <summary>
    /// Generates a branch name for a plan-update PR.
    /// </summary>
    public string GeneratePlanUpdateBranchName(string description)
    {
        var sanitized = GitWorktreeService.SanitizeBranchName(description);
        return $"plan-update/chore/{sanitized}";
    }

    /// <summary>
    /// Checks if a pull request only modifies ROADMAP.json (plan update only).
    /// </summary>
    public async Task<bool> IsPlanUpdateOnlyAsync(string pullRequestId)
    {
        var pullRequest = _dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null) return false;

        var project = _dataStore.GetProject(pullRequest.ProjectId);
        if (project == null) return false;

        var workingDir = pullRequest.WorktreePath ?? project.LocalPath;
        var baseBranch = project.DefaultBranch ?? "main";

        // Get list of changed files
        var result = await _commandRunner.RunAsync(
            "git",
            $"diff --name-only origin/{baseBranch}...HEAD",
            workingDir);

        if (!result.Success) return false;

        var changedFiles = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToList();

        // Check if only ROADMAP.json is changed
        return changedFiles.Count == 1 && changedFiles[0] == RoadmapFileName;
    }

    /// <summary>
    /// Validates the ROADMAP.json in a pull request's worktree.
    /// </summary>
    public async Task<bool> ValidateRoadmapAsync(string pullRequestId)
    {
        var pullRequest = _dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null) return false;

        var project = _dataStore.GetProject(pullRequest.ProjectId);
        if (project == null) return false;

        var workingDir = pullRequest.WorktreePath ?? project.LocalPath;
        var roadmapPath = Path.Combine(workingDir, RoadmapFileName);

        if (!File.Exists(roadmapPath)) return true; // No roadmap is valid

        try
        {
            await RoadmapParser.LoadAsync(roadmapPath);
            return true;
        }
        catch (RoadmapValidationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a plan-update pull request for modifying the roadmap.
    /// Note: For creating PRs to sync roadmap to main, use IRoadmapSyncService.CreatePlanUpdatePRAsync instead.
    /// </summary>
    public async Task<PullRequest?> CreatePlanUpdatePullRequestAsync(string projectId, string description)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return null;

        var branchName = GeneratePlanUpdateBranchName(description);

        // Create worktree
        var worktreePath = await _worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null) return null;

        // Create pull request entry
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = $"Plan Update: {description}",
            Description = "Updates to ROADMAP.json",
            BranchName = branchName,
            Status = OpenPullRequestStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        await _dataStore.AddPullRequestAsync(pullRequest);

        return pullRequest;
    }

    /// <summary>
    /// Adds a new change to the roadmap. Creates ROADMAP.local.json if it doesn't exist.
    /// Syncs changes to all worktrees.
    /// </summary>
    public async Task<bool> AddChangeAsync(string projectId, FutureChange change)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return false;

        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null) return false;

        Roadmap roadmap;
        if (File.Exists(roadmapPath))
        {
            roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        }
        else
        {
            roadmap = new Roadmap
            {
                Version = "1.1"
            };
        }

        // Add the new change to the end of the changes list
        roadmap.Changes.Add(change);

        await RoadmapParser.SaveAsync(roadmap, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        return true;
    }

    /// <summary>
    /// Updates the status of a change in the roadmap.
    /// Syncs changes to all worktrees.
    /// </summary>
    public async Task<bool> UpdateChangeStatusAsync(string projectId, string changeId, FutureChangeStatus status)
    {
        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return false;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return false;

        change.Status = status;
        await RoadmapParser.SaveAsync(roadmap, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        return true;
    }

    /// <summary>
    /// Removes a parent reference from all changes that reference it.
    /// Used when a parent change is promoted to a PR.
    /// Syncs changes to all worktrees.
    /// </summary>
    public async Task<bool> RemoveParentReferenceAsync(string projectId, string parentId)
    {
        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return false;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var modified = false;

        foreach (var change in roadmap.Changes)
        {
            if (change.Parents.Remove(parentId))
            {
                modified = true;
            }
        }

        if (modified)
        {
            await RoadmapParser.SaveAsync(roadmap, roadmapPath);

            // Sync the updated roadmap to all worktrees
            if (_syncService != null)
            {
                await _syncService.SyncToAllWorktreesAsync(projectId);
            }
        }

        return modified;
    }

    /// <summary>
    /// Creates a git worktree for a FutureChange without promoting it to a PR.
    /// Updates the change's WorktreePath property in the roadmap.
    /// </summary>
    public async Task<string?> CreateWorktreeForChangeAsync(string projectId, string changeId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return null;

        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return null;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return null;

        // The Id IS the branch name
        var branchName = change.Id;

        // Create worktree
        var worktreePath = await _worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            // Try without creating branch if it already exists
            worktreePath = await _worktreeService.CreateWorktreeAsync(
                project.LocalPath,
                branchName);
        }

        if (worktreePath == null) return null;

        // Update the change's WorktreePath in the roadmap
        change.WorktreePath = worktreePath;
        await RoadmapParser.SaveAsync(roadmap, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        _logger.LogInformation(
            "Created worktree for change {ChangeId} at {WorktreePath}",
            changeId, worktreePath);

        return worktreePath;
    }

    /// <summary>
    /// Updates the active agent server ID for a change.
    /// </summary>
    public async Task<bool> UpdateChangeAgentAsync(string projectId, string changeId, string? agentServerId)
    {
        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return false;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return false;

        change.ActiveAgentServerId = agentServerId;
        await RoadmapParser.SaveAsync(roadmap, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        _logger.LogInformation(
            "Updated agent server ID for change {ChangeId} to {AgentServerId}",
            changeId, agentServerId ?? "(cleared)");

        return true;
    }

    /// <summary>
    /// Promotes a completed FutureChange to a PullRequest after confirming the GitHub PR exists.
    /// Removes the change from the roadmap and creates a PR record in homespun-data.json.
    /// </summary>
    public async Task<PullRequest?> PromoteCompletedChangeAsync(string projectId, string changeId, int prNumber)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return null;

        var roadmapPath = await GetRoadmapPathAsync(projectId);
        if (roadmapPath == null || !File.Exists(roadmapPath)) return null;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return null;

        // Create pull request entry with the confirmed GitHub PR number
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = change.Title,
            Description = change.Description,
            BranchName = change.Id,
            GitHubPRNumber = prNumber,
            Status = OpenPullRequestStatus.ReadyForReview,
            WorktreePath = change.WorktreePath
        };

        await _dataStore.AddPullRequestAsync(pullRequest);

        // Remove the change from the roadmap and update parent references
        await RemoveChangeAndUpdateParentsAsync(roadmap, changeId, roadmapPath);

        // Sync the updated roadmap to all worktrees
        if (_syncService != null)
        {
            await _syncService.SyncToAllWorktreesAsync(projectId);
        }

        _logger.LogInformation(
            "Promoted change {ChangeId} to PR #{PrNumber}",
            changeId, prNumber);

        return pullRequest;
    }
}
