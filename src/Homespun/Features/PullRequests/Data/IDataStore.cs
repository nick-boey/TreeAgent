using Homespun.Features.Beads.Data;

namespace Homespun.Features.PullRequests.Data;

/// <summary>
/// Interface for the JSON-based data store.
/// </summary>
public interface IDataStore
{
    #region Projects
    
    /// <summary>
    /// Gets all projects.
    /// </summary>
    IReadOnlyList<Entities.Project> Projects { get; }

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
    
    #endregion

    #region Pull Requests
    
    /// <summary>
    /// Gets all pull requests.
    /// </summary>
    IReadOnlyList<Entities.PullRequest> PullRequests { get; }

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
    
    #endregion
    
    #region Beads Issue Metadata
    
    /// <summary>
    /// Gets all beads issue metadata.
    /// </summary>
    IReadOnlyList<BeadsIssueMetadata> BeadsIssueMetadata { get; }
    
    /// <summary>
    /// Adds beads issue metadata to the store.
    /// </summary>
    Task AddBeadsIssueMetadataAsync(BeadsIssueMetadata metadata);
    
    /// <summary>
    /// Updates beads issue metadata in the store.
    /// </summary>
    Task UpdateBeadsIssueMetadataAsync(BeadsIssueMetadata metadata);
    
    /// <summary>
    /// Removes beads issue metadata from the store.
    /// </summary>
    Task RemoveBeadsIssueMetadataAsync(string issueId);
    
    /// <summary>
    /// Gets beads issue metadata by issue ID.
    /// </summary>
    BeadsIssueMetadata? GetBeadsIssueMetadata(string issueId);
    
    /// <summary>
    /// Gets beads issue metadata for a specific project.
    /// </summary>
    IReadOnlyList<BeadsIssueMetadata> GetBeadsIssueMetadataByProject(string projectId);
    
    #endregion

    /// <summary>
    /// Saves any pending changes to disk.
    /// </summary>
    Task SaveAsync();
}
