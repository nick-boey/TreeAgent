using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Testing;

/// <summary>
/// In-memory data store for mock/demo mode and testing.
/// Thread-safe implementation using locks for concurrent access.
/// Consolidates functionality from test project implementations.
/// </summary>
public class MockDataStore : IDataStore
{
    private readonly object _lock = new();
    private readonly List<Project> _projects = [];
    private readonly List<PullRequest> _pullRequests = [];
    private readonly List<string> _favoriteModels = [];
    private readonly List<AgentPrompt> _agentPrompts = [];

    public IReadOnlyList<Project> Projects
    {
        get
        {
            lock (_lock)
            {
                return _projects.ToList().AsReadOnly();
            }
        }
    }

    public IReadOnlyList<PullRequest> PullRequests
    {
        get
        {
            lock (_lock)
            {
                return _pullRequests.ToList().AsReadOnly();
            }
        }
    }

    public IReadOnlyList<string> FavoriteModels
    {
        get
        {
            lock (_lock)
            {
                return _favoriteModels.ToList().AsReadOnly();
            }
        }
    }

    public IReadOnlyList<AgentPrompt> AgentPrompts
    {
        get
        {
            lock (_lock)
            {
                return _agentPrompts.ToList().AsReadOnly();
            }
        }
    }

    public Project? GetProject(string id)
    {
        lock (_lock)
        {
            return _projects.FirstOrDefault(p => p.Id == id);
        }
    }

    public Task AddProjectAsync(Project project)
    {
        lock (_lock)
        {
            _projects.Add(project);
        }
        return Task.CompletedTask;
    }

    public Task UpdateProjectAsync(Project project)
    {
        lock (_lock)
        {
            var index = _projects.FindIndex(p => p.Id == project.Id);
            if (index >= 0)
            {
                _projects[index] = project;
            }
        }
        return Task.CompletedTask;
    }

    public Task RemoveProjectAsync(string projectId)
    {
        lock (_lock)
        {
            _projects.RemoveAll(p => p.Id == projectId);
            // Cascade delete pull requests for the project
            _pullRequests.RemoveAll(pr => pr.ProjectId == projectId);
        }
        return Task.CompletedTask;
    }

    public PullRequest? GetPullRequest(string id)
    {
        lock (_lock)
        {
            return _pullRequests.FirstOrDefault(pr => pr.Id == id);
        }
    }

    public IReadOnlyList<PullRequest> GetPullRequestsByProject(string projectId)
    {
        lock (_lock)
        {
            return _pullRequests.Where(pr => pr.ProjectId == projectId).ToList().AsReadOnly();
        }
    }

    public Task AddPullRequestAsync(PullRequest pullRequest)
    {
        lock (_lock)
        {
            _pullRequests.Add(pullRequest);
        }
        return Task.CompletedTask;
    }

    public Task UpdatePullRequestAsync(PullRequest pullRequest)
    {
        lock (_lock)
        {
            var index = _pullRequests.FindIndex(pr => pr.Id == pullRequest.Id);
            if (index >= 0)
            {
                _pullRequests[index] = pullRequest;
            }
        }
        return Task.CompletedTask;
    }

    public Task RemovePullRequestAsync(string pullRequestId)
    {
        lock (_lock)
        {
            _pullRequests.RemoveAll(pr => pr.Id == pullRequestId);
        }
        return Task.CompletedTask;
    }

    public Task AddFavoriteModelAsync(string modelId)
    {
        lock (_lock)
        {
            if (!_favoriteModels.Contains(modelId))
            {
                _favoriteModels.Add(modelId);
            }
        }
        return Task.CompletedTask;
    }

    public Task RemoveFavoriteModelAsync(string modelId)
    {
        lock (_lock)
        {
            _favoriteModels.Remove(modelId);
        }
        return Task.CompletedTask;
    }

    public bool IsFavoriteModel(string modelId)
    {
        lock (_lock)
        {
            return _favoriteModels.Contains(modelId);
        }
    }

    public AgentPrompt? GetAgentPrompt(string id)
    {
        lock (_lock)
        {
            return _agentPrompts.FirstOrDefault(p => p.Id == id);
        }
    }

    public Task AddAgentPromptAsync(AgentPrompt prompt)
    {
        lock (_lock)
        {
            _agentPrompts.Add(prompt);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAgentPromptAsync(AgentPrompt prompt)
    {
        lock (_lock)
        {
            var index = _agentPrompts.FindIndex(p => p.Id == prompt.Id);
            if (index >= 0)
            {
                _agentPrompts[index] = prompt;
            }
        }
        return Task.CompletedTask;
    }

    public Task RemoveAgentPromptAsync(string promptId)
    {
        lock (_lock)
        {
            _agentPrompts.RemoveAll(p => p.Id == promptId);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync() => Task.CompletedTask;

    /// <summary>
    /// Clears all data from the store. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _projects.Clear();
            _pullRequests.Clear();
            _favoriteModels.Clear();
            _agentPrompts.Clear();
        }
    }

    /// <summary>
    /// Seeds a project directly. Useful for testing and demo data.
    /// </summary>
    public void SeedProject(Project project)
    {
        lock (_lock)
        {
            _projects.Add(project);
        }
    }

    /// <summary>
    /// Seeds a pull request directly. Useful for testing and demo data.
    /// </summary>
    public void SeedPullRequest(PullRequest pullRequest)
    {
        lock (_lock)
        {
            _pullRequests.Add(pullRequest);
        }
    }

    /// <summary>
    /// Seeds an agent prompt directly. Useful for testing and demo data.
    /// </summary>
    public void SeedAgentPrompt(AgentPrompt prompt)
    {
        lock (_lock)
        {
            _agentPrompts.Add(prompt);
        }
    }

    /// <summary>
    /// Seeds a favorite model directly. Useful for testing and demo data.
    /// </summary>
    public void SeedFavoriteModel(string modelId)
    {
        lock (_lock)
        {
            if (!_favoriteModels.Contains(modelId))
            {
                _favoriteModels.Add(modelId);
            }
        }
    }
}
