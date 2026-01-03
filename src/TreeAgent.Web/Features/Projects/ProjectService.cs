using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Projects;

public class ProjectService(TreeAgentDbContext db)
{
    public async Task<List<Project>> GetAllAsync()
    {
        return await db.Projects
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetByIdAsync(string id)
    {
        return await db.Projects.FindAsync(id);
    }

    public async Task<Project> CreateAsync(string name, string localPath, string? gitHubOwner = null, string? gitHubRepo = null, string defaultBranch = "main")
    {
        var project = new Project
        {
            Name = name,
            LocalPath = localPath,
            GitHubOwner = gitHubOwner,
            GitHubRepo = gitHubRepo,
            DefaultBranch = defaultBranch
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<Project?> UpdateAsync(
        string id,
        string name,
        string localPath,
        string? gitHubOwner,
        string? gitHubRepo,
        string defaultBranch,
        string? defaultSystemPrompt = null,
        string? defaultPromptTemplateId = null)
    {
        var project = await db.Projects.FindAsync(id);
        if (project == null) return null;

        project.Name = name;
        project.LocalPath = localPath;
        project.GitHubOwner = gitHubOwner;
        project.GitHubRepo = gitHubRepo;
        project.DefaultBranch = defaultBranch;
        project.DefaultSystemPrompt = defaultSystemPrompt;
        project.DefaultPromptTemplateId = string.IsNullOrEmpty(defaultPromptTemplateId) ? null : defaultPromptTemplateId;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var project = await db.Projects.FindAsync(id);
        if (project == null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        return true;
    }
}
