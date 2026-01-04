using System.Text.Json;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.PullRequests.Data;

/// <summary>
/// JSON file-based data store for projects and pull requests.
/// Thread-safe implementation that persists data to a JSON file.
/// </summary>
public class JsonDataStore : IDataStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<JsonDataStore> _logger;
    private StoreData _data = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonDataStore(string filePath, ILogger<JsonDataStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        LoadSync();
    }

    public IReadOnlyList<Project> Projects => _data.Projects.AsReadOnly();
    public IReadOnlyList<PullRequest> PullRequests => _data.PullRequests.AsReadOnly();

    public Project? GetProject(string id) => _data.Projects.FirstOrDefault(p => p.Id == id);

    public PullRequest? GetPullRequest(string id) => _data.PullRequests.FirstOrDefault(pr => pr.Id == id);

    public IReadOnlyList<PullRequest> GetPullRequestsByProject(string projectId) =>
        _data.PullRequests.Where(pr => pr.ProjectId == projectId).ToList().AsReadOnly();

    public async Task AddProjectAsync(Project project)
    {
        await _lock.WaitAsync();
        try
        {
            _data.Projects.Add(project);
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateProjectAsync(Project project)
    {
        await _lock.WaitAsync();
        try
        {
            var index = _data.Projects.FindIndex(p => p.Id == project.Id);
            if (index >= 0)
            {
                _data.Projects[index] = project;
                await SaveInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveProjectAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            _data.Projects.RemoveAll(p => p.Id == projectId);
            // Also remove associated pull requests
            _data.PullRequests.RemoveAll(pr => pr.ProjectId == projectId);
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddPullRequestAsync(PullRequest pullRequest)
    {
        await _lock.WaitAsync();
        try
        {
            _data.PullRequests.Add(pullRequest);
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdatePullRequestAsync(PullRequest pullRequest)
    {
        await _lock.WaitAsync();
        try
        {
            var index = _data.PullRequests.FindIndex(pr => pr.Id == pullRequest.Id);
            if (index >= 0)
            {
                _data.PullRequests[index] = pullRequest;
                await SaveInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemovePullRequestAsync(string pullRequestId)
    {
        await _lock.WaitAsync();
        try
        {
            _data.PullRequests.RemoveAll(pr => pr.Id == pullRequestId);
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await SaveInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void LoadSync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _data = JsonSerializer.Deserialize<StoreData>(json, JsonOptions) ?? new StoreData();
                _logger.LogInformation("Loaded data store from {FilePath}: {ProjectCount} projects, {PullRequestCount} pull requests",
                    _filePath, _data.Projects.Count, _data.PullRequests.Count);
            }
            else
            {
                _logger.LogInformation("Data store file not found at {FilePath}, starting with empty store", _filePath);
                _data = new StoreData();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data store from {FilePath}, starting with empty store", _filePath);
            _data = new StoreData();
        }
    }

    private async Task SaveInternalAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_data, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
            _logger.LogDebug("Saved data store to {FilePath}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data store to {FilePath}", _filePath);
            throw;
        }
    }

    /// <summary>
    /// Internal data structure for JSON serialization.
    /// </summary>
    private class StoreData
    {
        public List<Project> Projects { get; set; } = [];
        public List<PullRequest> PullRequests { get; set; } = [];
    }
}
