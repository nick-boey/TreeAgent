using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.PullRequests.Data;

/// <summary>
/// Service for managing locally tracked pull requests.
/// Only open PRs are stored.
/// </summary>
public class PullRequestDataService(
    IDataStore dataStore, 
    IGitWorktreeService worktreeService,
    ILogger<PullRequestDataService> logger)
{
    public Task<List<PullRequest>> GetByProjectIdAsync(string projectId)
    {
        var pullRequests = dataStore.GetPullRequestsByProject(projectId)
            .OrderBy(pr => pr.CreatedAt)
            .ToList();
        return Task.FromResult(pullRequests);
    }

    public Task<PullRequest?> GetByIdAsync(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest != null)
        {
            // Populate navigation properties
            pullRequest.Project = dataStore.GetProject(pullRequest.ProjectId)!;
            pullRequest.Children = dataStore.PullRequests
                .Where(pr => pr.ParentId == id)
                .ToList();
        }
        return Task.FromResult(pullRequest);
    }

    public Task<List<PullRequest>> GetTreeAsync(string projectId)
    {
        var pullRequests = dataStore.GetPullRequestsByProject(projectId)
            .OrderBy(pr => pr.CreatedAt)
            .ToList();

        // Build the tree structure by populating Children
        var pullRequestMap = pullRequests.ToDictionary(pr => pr.Id);
        foreach (var pr in pullRequests)
        {
            pr.Children = pullRequests.Where(child => child.ParentId == pr.Id).ToList();
        }

        // Return only root pull requests (those without parents)
        return Task.FromResult(pullRequests.Where(pr => pr.ParentId == null).ToList());
    }

    public async Task<PullRequest> CreateAsync(
        string projectId,
        string title,
        string? description = null,
        string? branchName = null,
        string? parentId = null)
    {
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            ParentId = parentId,
            Title = title,
            Description = description,
            BranchName = branchName,
            Status = OpenPullRequestStatus.InDevelopment
        };

        await dataStore.AddPullRequestAsync(pullRequest);
        return pullRequest;
    }

    public async Task<PullRequest?> UpdateAsync(
        string id,
        string title,
        string? description,
        string? branchName,
        OpenPullRequestStatus status)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return null;

        pullRequest.Title = title;
        pullRequest.Description = description;
        pullRequest.BranchName = branchName;
        pullRequest.Status = status;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return pullRequest;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        // Get project for worktree cleanup
        var project = dataStore.GetProject(pullRequest.ProjectId);

        // Clean up worktree if exists
        if (!string.IsNullOrEmpty(pullRequest.WorktreePath) && project != null)
        {
            await worktreeService.RemoveWorktreeAsync(project.LocalPath, pullRequest.WorktreePath);
        }

        await dataStore.RemovePullRequestAsync(id);
        return true;
    }

    public async Task<bool> StartDevelopmentAsync(string id)
    {
        logger.LogDebug("Starting development for pull request {PullRequestId}", id);
        
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null)
        {
            logger.LogError("Pull request {PullRequestId} not found", id);
            return false;
        }
        
        if (string.IsNullOrEmpty(pullRequest.BranchName))
        {
            logger.LogError("Pull request {PullRequestId} has no branch name", id);
            return false;
        }

        var project = dataStore.GetProject(pullRequest.ProjectId);
        if (project == null)
        {
            logger.LogError("Project {ProjectId} not found for pull request {PullRequestId}", 
                pullRequest.ProjectId, id);
            return false;
        }

        logger.LogInformation(
            "Creating worktree for PR {PullRequestId} branch {BranchName} in {LocalPath}",
            id, pullRequest.BranchName, project.LocalPath);

        // Ensure the base branch is up-to-date before creating a new branch from it
        var baseBranch = project.DefaultBranch;
        var fetchSuccess = await worktreeService.FetchAndUpdateBranchAsync(project.LocalPath, baseBranch);
        if (!fetchSuccess)
        {
            logger.LogWarning(
                "Failed to fetch latest changes for base branch {BaseBranch}, continuing with local version",
                baseBranch);
        }

        // Create worktree for the pull request
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            pullRequest.BranchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            logger.LogError(
                "Failed to create worktree for PR {PullRequestId} branch {BranchName}. " +
                "Check git logs for details.",
                id, pullRequest.BranchName);
            return false;
        }

        logger.LogInformation("Worktree created at {WorktreePath} for PR {PullRequestId}", 
            worktreePath, id);

        pullRequest.WorktreePath = worktreePath;
        pullRequest.Status = OpenPullRequestStatus.InDevelopment;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return true;
    }

    public async Task<bool> MarkReadyForReviewAsync(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        pullRequest.Status = OpenPullRequestStatus.ReadyForReview;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return true;
    }

    public async Task<bool> UpdateStatusAsync(string id, OpenPullRequestStatus status)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        pullRequest.Status = status;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return true;
    }

    public async Task<bool> UpdateWorktreePathAsync(string id, string worktreePath)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        pullRequest.WorktreePath = worktreePath;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return true;
    }

    /// <summary>
    /// Completes a pull request by removing it from local tracking.
    /// Merged/cancelled PRs should be retrieved from GitHub, not stored locally.
    /// </summary>
    public async Task<bool> CompleteAsync(string id)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        var project = dataStore.GetProject(pullRequest.ProjectId);

        // Clean up worktree
        if (!string.IsNullOrEmpty(pullRequest.WorktreePath) && project != null)
        {
            await worktreeService.RemoveWorktreeAsync(project.LocalPath, pullRequest.WorktreePath);
        }

        // Remove from local tracking - merged/cancelled PRs should be fetched from GitHub
        await dataStore.RemovePullRequestAsync(id);
        return true;
    }

    public async Task<bool> SetParentAsync(string id, string? parentId)
    {
        var pullRequest = dataStore.GetPullRequest(id);
        if (pullRequest == null) return false;

        // Prevent circular references
        if (parentId != null)
        {
            var parent = dataStore.GetPullRequest(parentId);
            if (parent == null) return false;

            // Check if setting this parent would create a cycle
            var currentParent = parent;
            while (currentParent != null)
            {
                if (currentParent.Id == id) return false;
                currentParent = currentParent.ParentId != null
                    ? dataStore.GetPullRequest(currentParent.ParentId)
                    : null;
            }
        }

        pullRequest.ParentId = parentId;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdatePullRequestAsync(pullRequest);
        return true;
    }

    public async Task CleanupStaleWorktreesAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return;

        await worktreeService.PruneWorktreesAsync(project.LocalPath);
    }
}
