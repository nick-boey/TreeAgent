using Homespun.Features.Beads.Data;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Tests.Helpers;

/// <summary>
/// In-memory implementation of IDataStore for testing.
/// </summary>
public class TestDataStore : IDataStore
{
    private readonly List<Project> _projects = [];
    private readonly List<PullRequest> _pullRequests = [];
    private readonly List<BeadsIssueMetadata> _beadsIssueMetadata = [];
    private readonly List<string> _favoriteModels = [];

    public IReadOnlyList<Project> Projects => _projects.AsReadOnly();
    public IReadOnlyList<PullRequest> PullRequests => _pullRequests.AsReadOnly();
    public IReadOnlyList<BeadsIssueMetadata> BeadsIssueMetadata => _beadsIssueMetadata.AsReadOnly();
    public IReadOnlyList<string> FavoriteModels => _favoriteModels.AsReadOnly();

    public Project? GetProject(string id) => _projects.FirstOrDefault(p => p.Id == id);

    public PullRequest? GetPullRequest(string id) => _pullRequests.FirstOrDefault(pr => pr.Id == id);

    public IReadOnlyList<PullRequest> GetPullRequestsByProject(string projectId) =>
        _pullRequests.Where(pr => pr.ProjectId == projectId).ToList().AsReadOnly();
    
    public BeadsIssueMetadata? GetBeadsIssueMetadata(string issueId) =>
        _beadsIssueMetadata.FirstOrDefault(m => m.IssueId == issueId);
    
    public IReadOnlyList<BeadsIssueMetadata> GetBeadsIssueMetadataByProject(string projectId) =>
        _beadsIssueMetadata.Where(m => m.ProjectId == projectId).ToList().AsReadOnly();

    public Task AddProjectAsync(Project project)
    {
        _projects.Add(project);
        return Task.CompletedTask;
    }

    public Task UpdateProjectAsync(Project project)
    {
        var index = _projects.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            _projects[index] = project;
        }
        return Task.CompletedTask;
    }

    public Task RemoveProjectAsync(string projectId)
    {
        _projects.RemoveAll(p => p.Id == projectId);
        _pullRequests.RemoveAll(pr => pr.ProjectId == projectId);
        _beadsIssueMetadata.RemoveAll(m => m.ProjectId == projectId);
        return Task.CompletedTask;
    }

    public Task AddPullRequestAsync(PullRequest pullRequest)
    {
        _pullRequests.Add(pullRequest);
        return Task.CompletedTask;
    }

    public Task UpdatePullRequestAsync(PullRequest pullRequest)
    {
        var index = _pullRequests.FindIndex(pr => pr.Id == pullRequest.Id);
        if (index >= 0)
        {
            _pullRequests[index] = pullRequest;
        }
        return Task.CompletedTask;
    }

    public Task RemovePullRequestAsync(string pullRequestId)
    {
        _pullRequests.RemoveAll(pr => pr.Id == pullRequestId);
        return Task.CompletedTask;
    }
    
    public Task AddBeadsIssueMetadataAsync(BeadsIssueMetadata metadata)
    {
        _beadsIssueMetadata.Add(metadata);
        return Task.CompletedTask;
    }
    
    public Task UpdateBeadsIssueMetadataAsync(BeadsIssueMetadata metadata)
    {
        var index = _beadsIssueMetadata.FindIndex(m => m.IssueId == metadata.IssueId);
        if (index >= 0)
        {
            metadata.UpdatedAt = DateTime.UtcNow;
            _beadsIssueMetadata[index] = metadata;
        }
        else
        {
            _beadsIssueMetadata.Add(metadata);
        }
        return Task.CompletedTask;
    }
    
    public Task RemoveBeadsIssueMetadataAsync(string issueId)
    {
        _beadsIssueMetadata.RemoveAll(m => m.IssueId == issueId);
        return Task.CompletedTask;
    }

    public Task AddFavoriteModelAsync(string modelId)
    {
        if (!_favoriteModels.Contains(modelId))
        {
            _favoriteModels.Add(modelId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveFavoriteModelAsync(string modelId)
    {
        _favoriteModels.Remove(modelId);
        return Task.CompletedTask;
    }

    public bool IsFavoriteModel(string modelId) => _favoriteModels.Contains(modelId);

    public Task SaveAsync() => Task.CompletedTask;

    /// <summary>
    /// Clears all data from the store.
    /// </summary>
    public void Clear()
    {
        _projects.Clear();
        _pullRequests.Clear();
        _beadsIssueMetadata.Clear();
        _favoriteModels.Clear();
    }
}
