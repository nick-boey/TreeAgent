using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.PullRequests.Data;

/// <summary>
/// Service for managing locally tracked pull requests.
/// Only open PRs are stored in the database.
/// </summary>
public class PullRequestDataService(TreeAgentDbContext db, IGitWorktreeService worktreeService)
{
    public async Task<List<PullRequest>> GetByProjectIdAsync(string projectId)
    {
        return await db.PullRequests
            .Where(pr => pr.ProjectId == projectId)
            .OrderBy(pr => pr.CreatedAt)
            .ToListAsync();
    }

    public async Task<PullRequest?> GetByIdAsync(string id)
    {
        return await db.PullRequests
            .Include(pr => pr.Project)
            .Include(pr => pr.Children)
            .FirstOrDefaultAsync(pr => pr.Id == id);
    }

    public async Task<List<PullRequest>> GetTreeAsync(string projectId)
    {
        var pullRequests = await db.PullRequests
            .Where(pr => pr.ProjectId == projectId)
            .Include(pr => pr.Children)
            .OrderBy(pr => pr.CreatedAt)
            .ToListAsync();

        // Return only root pull requests (those without parents)
        return pullRequests.Where(pr => pr.ParentId == null).ToList();
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

        db.PullRequests.Add(pullRequest);
        await db.SaveChangesAsync();
        return pullRequest;
    }

    public async Task<PullRequest?> UpdateAsync(
        string id,
        string title,
        string? description,
        string? branchName,
        OpenPullRequestStatus status)
    {
        var pullRequest = await db.PullRequests.FindAsync(id);
        if (pullRequest == null) return null;

        pullRequest.Title = title;
        pullRequest.Description = description;
        pullRequest.BranchName = branchName;
        pullRequest.Status = status;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return pullRequest;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == id);

        if (pullRequest == null) return false;

        // Clean up worktree if exists
        if (!string.IsNullOrEmpty(pullRequest.WorktreePath))
        {
            await worktreeService.RemoveWorktreeAsync(pullRequest.Project.LocalPath, pullRequest.WorktreePath);
        }

        db.PullRequests.Remove(pullRequest);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StartDevelopmentAsync(string id)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == id);

        if (pullRequest == null) return false;
        if (string.IsNullOrEmpty(pullRequest.BranchName)) return false;

        // Create worktree for the pull request
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            pullRequest.Project.LocalPath,
            pullRequest.BranchName,
            createBranch: true,
            baseBranch: pullRequest.Project.DefaultBranch);

        if (worktreePath == null) return false;

        pullRequest.WorktreePath = worktreePath;
        pullRequest.Status = OpenPullRequestStatus.InDevelopment;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkReadyForReviewAsync(string id)
    {
        var pullRequest = await db.PullRequests.FindAsync(id);
        if (pullRequest == null) return false;

        pullRequest.Status = OpenPullRequestStatus.ReadyForReview;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Completes a pull request by removing it from local tracking.
    /// Merged/cancelled PRs should be retrieved from GitHub, not stored locally.
    /// </summary>
    public async Task<bool> CompleteAsync(string id)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == id);

        if (pullRequest == null) return false;

        // Clean up worktree
        if (!string.IsNullOrEmpty(pullRequest.WorktreePath))
        {
            await worktreeService.RemoveWorktreeAsync(pullRequest.Project.LocalPath, pullRequest.WorktreePath);
        }

        // Remove from local tracking - merged/cancelled PRs should be fetched from GitHub
        db.PullRequests.Remove(pullRequest);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetParentAsync(string id, string? parentId)
    {
        var pullRequest = await db.PullRequests.FindAsync(id);
        if (pullRequest == null) return false;

        // Prevent circular references
        if (parentId != null)
        {
            var parent = await db.PullRequests.FindAsync(parentId);
            if (parent == null) return false;

            // Check if setting this parent would create a cycle
            var currentParent = parent;
            while (currentParent != null)
            {
                if (currentParent.Id == id) return false;
                currentParent = currentParent.ParentId != null
                    ? await db.PullRequests.FindAsync(currentParent.ParentId)
                    : null;
            }
        }

        pullRequest.ParentId = parentId;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task CleanupStaleWorktreesAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null) return;

        await worktreeService.PruneWorktreesAsync(project.LocalPath);
    }
}
