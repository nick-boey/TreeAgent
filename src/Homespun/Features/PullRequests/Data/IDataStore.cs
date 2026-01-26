using Homespun.Features.ClaudeCode.Data;

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

    #region Favorite Models

    /// <summary>
    /// Gets all favorite model IDs.
    /// </summary>
    IReadOnlyList<string> FavoriteModels { get; }

    /// <summary>
    /// Adds a model to favorites.
    /// </summary>
    Task AddFavoriteModelAsync(string modelId);

    /// <summary>
    /// Removes a model from favorites.
    /// </summary>
    Task RemoveFavoriteModelAsync(string modelId);

    /// <summary>
    /// Checks if a model is in favorites.
    /// </summary>
    bool IsFavoriteModel(string modelId);

    #endregion

    #region Agent Prompts

    /// <summary>
    /// Gets all agent prompts.
    /// </summary>
    IReadOnlyList<AgentPrompt> AgentPrompts { get; }

    /// <summary>
    /// Adds an agent prompt to the store.
    /// </summary>
    Task AddAgentPromptAsync(AgentPrompt prompt);

    /// <summary>
    /// Updates an agent prompt in the store.
    /// </summary>
    Task UpdateAgentPromptAsync(AgentPrompt prompt);

    /// <summary>
    /// Removes an agent prompt from the store.
    /// </summary>
    Task RemoveAgentPromptAsync(string promptId);

    /// <summary>
    /// Gets an agent prompt by ID.
    /// </summary>
    AgentPrompt? GetAgentPrompt(string id);

    #endregion

    /// <summary>
    /// Saves any pending changes to disk.
    /// </summary>
    Task SaveAsync();
}
