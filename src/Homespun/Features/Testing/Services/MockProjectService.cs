using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IProjectService that operates on in-memory data.
/// </summary>
public class MockProjectService : IProjectService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockProjectService> _logger;

    public MockProjectService(IDataStore dataStore, ILogger<MockProjectService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public Task<List<Project>> GetAllAsync()
    {
        _logger.LogDebug("[Mock] GetAllProjects");
        return Task.FromResult(_dataStore.Projects.ToList());
    }

    public Task<Project?> GetByIdAsync(string id)
    {
        _logger.LogDebug("[Mock] GetProjectById {ProjectId}", id);
        return Task.FromResult(_dataStore.GetProject(id));
    }

    public async Task<CreateProjectResult> CreateLocalAsync(string name, string defaultBranch = "main")
    {
        _logger.LogDebug("[Mock] CreateLocalProject {Name} with branch {Branch}", name, defaultBranch);

        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            LocalPath = $"/mock/projects/{name.ToLowerInvariant().Replace(" ", "-")}",
            DefaultBranch = defaultBranch
        };

        await _dataStore.AddProjectAsync(project);

        return new CreateProjectResult
        {
            Success = true,
            Project = project
        };
    }

    public async Task<CreateProjectResult> CreateAsync(string ownerRepo)
    {
        _logger.LogDebug("[Mock] CreateProject from {OwnerRepo}", ownerRepo);

        var parts = ownerRepo.Split('/');
        if (parts.Length != 2)
        {
            return new CreateProjectResult
            {
                Success = false,
                ErrorMessage = "Invalid owner/repo format"
            };
        }

        var owner = parts[0];
        var repo = parts[1];

        var project = new Project
        {
            Id = Guid.NewGuid().ToString(),
            Name = repo,
            LocalPath = $"/mock/projects/{repo}",
            GitHubOwner = owner,
            GitHubRepo = repo,
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);

        return new CreateProjectResult
        {
            Success = true,
            Project = project
        };
    }

    public async Task<Project?> UpdateAsync(string id, string? defaultModel = null)
    {
        _logger.LogDebug("[Mock] UpdateProject {ProjectId}", id);

        var project = _dataStore.GetProject(id);
        if (project == null)
        {
            return null;
        }

        if (defaultModel != null)
        {
            project.DefaultModel = defaultModel;
        }

        await _dataStore.UpdateProjectAsync(project);
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        _logger.LogDebug("[Mock] DeleteProject {ProjectId}", id);

        var project = _dataStore.GetProject(id);
        if (project == null)
        {
            return false;
        }

        await _dataStore.RemoveProjectAsync(id);
        return true;
    }
}
