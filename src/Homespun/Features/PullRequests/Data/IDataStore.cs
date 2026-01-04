namespace Homespun.Features.PullRequests.Data;

/// <summary>
/// Interface for the JSON-based data store.
/// </summary>
public interface IDataStore
{
    /// <summary>
    /// Gets all projects.
    /// </summary>
    IReadOnlyList<Entities.Project> Projects { get; }

    /// <summary>
    /// Gets all pull requests.
    /// </summary>
    IReadOnlyList<Entities.PullRequest> PullRequests { get; }

    /// <summary>
    /// Adds a project to the store.
    /// </summary>
    Task AddProjectAsync(Entities.Project project);

    /// <summary>
    /// Updates a project in the store.
    /// </summary>
    Task UpdateProjectAsync(Entities.Project project);

    /// <summary>
    /// Removes a project from the store.
    /// </summary>
    Task RemoveProjectAsync(string projectId);

    /// <summary>
    /// Gets a project by ID.
    /// </summary>
    Entities.Project? GetProject(string id);

    /// <summary>
    /// Adds a pull request to the store.
    /// </summary>
    Task AddPullRequestAsync(Entities.PullRequest pullRequest);

    /// <summary>
    /// Updates a pull request in the store.
    /// </summary>
    Task UpdatePullRequestAsync(Entities.PullRequest pullRequest);

    /// <summary>
    /// Removes a pull request from the store.
    /// </summary>
    Task RemovePullRequestAsync(string pullRequestId);

    /// <summary>
    /// Gets a pull request by ID.
    /// </summary>
    Entities.PullRequest? GetPullRequest(string id);

    /// <summary>
    /// Gets pull requests for a specific project.
    /// </summary>
    IReadOnlyList<Entities.PullRequest> GetPullRequestsByProject(string projectId);

    /// <summary>
    /// Saves any pending changes to disk.
    /// </summary>
    Task SaveAsync();
}
