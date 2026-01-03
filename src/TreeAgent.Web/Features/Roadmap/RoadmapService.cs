using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Roadmap;

/// <summary>
/// Service for managing ROADMAP.json and future changes.
/// </summary>
public class RoadmapService(
    TreeAgentDbContext db,
    ICommandRunner commandRunner,
    IGitWorktreeService worktreeService)
    : IRoadmapService
{
    #region 3.1 Read and Display Future Changes

    /// <summary>
    /// Gets the path to the ROADMAP.json file for a project.
    /// </summary>
    public async Task<string?> GetRoadmapPathAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null) return null;

        return Path.Combine(project.LocalPath, "ROADMAP.json");
    }

    /// <summary>
    /// Loads and parses the ROADMAP.json file for a project.
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
    public async Task<RoadmapChange?> FindChangeByIdAsync(string projectId, string changeId)
    {
        var roadmap = await LoadRoadmapAsync(projectId);
        if (roadmap == null) return null;

        return FindChangeById(roadmap.Changes, changeId);
    }

    private static RoadmapChange? FindChangeById(List<RoadmapChange> changes, string id)
    {
        foreach (var change in changes)
        {
            if (change.Id == id)
                return change;

            var found = FindChangeById(change.Children, id);
            if (found != null)
                return found;
        }

        return null;
    }

    #endregion

    #region 3.2 Promote Future Change to Current PR

    /// <summary>
    /// Promotes a future change to an active feature with a worktree.
    /// </summary>
    public async Task<Feature?> PromoteChangeAsync(string projectId, string changeId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null) return null;

        var roadmapPath = Path.Combine(project.LocalPath, "ROADMAP.json");
        if (!File.Exists(roadmapPath)) return null;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = FindChangeById(roadmap.Changes, changeId);
        if (change == null) return null;

        // Generate branch name
        var branchName = change.GetBranchName();

        // Create worktree
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            // Try without creating branch if it already exists
            worktreePath = await worktreeService.CreateWorktreeAsync(
                project.LocalPath,
                branchName);
        }

        // Create feature
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = change.Title,
            Description = change.Description,
            BranchName = branchName,
            Status = FeatureStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        db.Features.Add(feature);
        await db.SaveChangesAsync();

        // Update roadmap - remove the promoted change and promote children
        await RemoveChangeAndPromoteChildrenAsync(roadmap, changeId, roadmapPath);

        return feature;
    }

    private async Task RemoveChangeAndPromoteChildrenAsync(Roadmap roadmap, string changeId, string roadmapPath)
    {
        var removed = RemoveChangeAndPromoteChildren(roadmap.Changes, changeId, null);

        if (removed)
        {
            await RoadmapParser.SaveAsync(roadmap, roadmapPath);
        }
    }

    private static bool RemoveChangeAndPromoteChildren(List<RoadmapChange> changes, string id, List<RoadmapChange>? parentList)
    {
        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];

            if (change.Id == id)
            {
                // Remove the change
                changes.RemoveAt(i);

                // Promote children to this level
                if (change.Children.Count > 0)
                {
                    changes.InsertRange(i, change.Children);
                }

                return true;
            }

            // Recursively search children
            if (RemoveChangeAndPromoteChildren(change.Children, id, changes))
                return true;
        }

        return false;
    }

    #endregion

    #region 3.3 Plan Update PRs

    /// <summary>
    /// Generates a branch name for a plan-update PR.
    /// </summary>
    public string GeneratePlanUpdateBranchName(string description)
    {
        var sanitized = GitWorktreeService.SanitizeBranchName(description);
        return $"plan-update/chore/{sanitized}";
    }

    /// <summary>
    /// Checks if a feature only modifies ROADMAP.json (plan update only).
    /// </summary>
    public async Task<bool> IsPlanUpdateOnlyAsync(string featureId)
    {
        var feature = await db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null) return false;

        var workingDir = feature.WorktreePath ?? feature.Project.LocalPath;
        var baseBranch = feature.Project.DefaultBranch ?? "main";

        // Get list of changed files
        var result = await commandRunner.RunAsync(
            "git",
            $"diff --name-only origin/{baseBranch}...HEAD",
            workingDir);

        if (!result.Success) return false;

        var changedFiles = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToList();

        // Check if only ROADMAP.json is changed
        return changedFiles.Count == 1 && changedFiles[0] == "ROADMAP.json";
    }

    /// <summary>
    /// Validates the ROADMAP.json in a feature's worktree.
    /// </summary>
    public async Task<bool> ValidateRoadmapAsync(string featureId)
    {
        var feature = await db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null) return false;

        var workingDir = feature.WorktreePath ?? feature.Project.LocalPath;
        var roadmapPath = Path.Combine(workingDir, "ROADMAP.json");

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
    /// Creates a plan-update feature for modifying the roadmap.
    /// </summary>
    public async Task<Feature?> CreatePlanUpdateFeatureAsync(string projectId, string description)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null) return null;

        var branchName = GeneratePlanUpdateBranchName(description);

        // Create worktree
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null) return null;

        // Create feature
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = $"Plan Update: {description}",
            Description = "Updates to ROADMAP.json",
            BranchName = branchName,
            Status = FeatureStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        db.Features.Add(feature);
        await db.SaveChangesAsync();

        return feature;
    }

    #endregion
}
