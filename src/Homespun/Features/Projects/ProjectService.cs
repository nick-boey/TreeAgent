using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Projects;

public class ProjectService(IDataStore dataStore)
{
    public Task<List<Project>> GetAllAsync()
    {
        var projects = dataStore.Projects
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();
        return Task.FromResult(projects);
    }

    public Task<Project?> GetByIdAsync(string id)
    {
        return Task.FromResult(dataStore.GetProject(id));
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

        await dataStore.AddProjectAsync(project);
        return project;
    }

    public async Task<Project?> UpdateAsync(
        string id,
        string name,
        string localPath,
        string? gitHubOwner,
        string? gitHubRepo,
        string defaultBranch,
        string? defaultModel = null)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return null;

        project.Name = name;
        project.LocalPath = localPath;
        project.GitHubOwner = gitHubOwner;
        project.GitHubRepo = gitHubRepo;
        project.DefaultBranch = defaultBranch;
        project.DefaultModel = defaultModel;
        project.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdateProjectAsync(project);
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return false;

        await dataStore.RemoveProjectAsync(id);
        return true;
    }
}
