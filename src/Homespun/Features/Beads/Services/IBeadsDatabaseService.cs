using Homespun.Features.Beads.Data;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service interface for direct SQLite access to the beads database.
/// Provides fast read operations from in-memory cache and queue-based writes.
/// </summary>
public interface IBeadsDatabaseService
{
    #region Read Operations (from in-memory cache)

    /// <summary>
    /// Gets a single issue by ID from the in-memory cache.
    /// </summary>
    /// <param name="projectPath">Path to the project containing .beads/beads.db</param>
    /// <param name="issueId">The beads issue ID (e.g., "hsp-a3f8")</param>
    /// <returns>The issue, or null if not found.</returns>
    BeadsIssue? GetIssue(string projectPath, string issueId);

    /// <summary>
    /// Lists issues from the in-memory cache matching the specified options.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="options">Filter options.</param>
    /// <returns>List of matching issues.</returns>
    IReadOnlyList<BeadsIssue> ListIssues(string projectPath, BeadsListOptions? options = null);

    /// <summary>
    /// Gets issues that are ready to work on (open and no blocking dependencies).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>List of ready issues.</returns>
    IReadOnlyList<BeadsIssue> GetReadyIssues(string projectPath);

    /// <summary>
    /// Gets dependencies for an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <returns>List of dependencies.</returns>
    IReadOnlyList<BeadsDependency> GetDependencies(string projectPath, string issueId);

    /// <summary>
    /// Gets all unique groups from hsp: labels in the project's issues.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>Sorted list of unique group names.</returns>
    IReadOnlyList<string> GetUniqueGroups(string projectPath);

    #endregion

    #region Write Operations (queue-based)

    /// <summary>
    /// Creates a new issue. Updates in-memory cache immediately and queues database write.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="options">Options for creating the issue.</param>
    /// <returns>The created issue (optimistic result).</returns>
    Task<BeadsIssue> CreateIssueAsync(string projectPath, BeadsCreateOptions options);

    /// <summary>
    /// Updates an existing issue. Updates in-memory cache immediately and queues database write.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="options">Options for updating the issue.</param>
    /// <returns>True if the issue was found and update queued.</returns>
    Task<bool> UpdateIssueAsync(string projectPath, string issueId, BeadsUpdateOptions options);

    /// <summary>
    /// Closes an issue. Updates in-memory cache immediately and queues database write.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="reason">Optional reason for closing.</param>
    /// <returns>True if the issue was found and close queued.</returns>
    Task<bool> CloseIssueAsync(string projectPath, string issueId, string? reason = null);

    /// <summary>
    /// Reopens a closed issue. Updates in-memory cache immediately and queues database write.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="reason">Optional reason for reopening.</param>
    /// <returns>True if the issue was found and reopen queued.</returns>
    Task<bool> ReopenIssueAsync(string projectPath, string issueId, string? reason = null);

    /// <summary>
    /// Deletes an issue (sets status to tombstone).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <returns>True if the issue was found and delete queued.</returns>
    Task<bool> DeleteIssueAsync(string projectPath, string issueId);

    /// <summary>
    /// Adds a label to an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="label">The label to add.</param>
    /// <returns>True if the issue was found and label add queued.</returns>
    Task<bool> AddLabelAsync(string projectPath, string issueId, string label);

    /// <summary>
    /// Removes a label from an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="label">The label to remove.</param>
    /// <returns>True if the issue was found and label remove queued.</returns>
    Task<bool> RemoveLabelAsync(string projectPath, string issueId, string label);

    /// <summary>
    /// Adds a dependency between issues.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue that will depend on another.</param>
    /// <param name="dependsOnIssueId">The issue being depended upon.</param>
    /// <param name="type">Dependency type (default: "blocks").</param>
    /// <returns>True if dependency add was queued.</returns>
    Task<bool> AddDependencyAsync(string projectPath, string issueId, string dependsOnIssueId, string type = "blocks");

    /// <summary>
    /// Removes a dependency between issues.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue with the dependency.</param>
    /// <param name="dependsOnIssueId">The issue being depended upon.</param>
    /// <returns>True if dependency remove was queued.</returns>
    Task<bool> RemoveDependencyAsync(string projectPath, string issueId, string dependsOnIssueId);

    #endregion

    #region State Management

    /// <summary>
    /// Loads or refreshes the in-memory cache from the SQLite database.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    Task RefreshFromDatabaseAsync(string projectPath);

    /// <summary>
    /// Checks if a project has pending changes in the queue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>True if there are pending changes.</returns>
    bool HasPendingChanges(string projectPath);

    /// <summary>
    /// Gets the completed history for undo capability.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="limit">Maximum items to return.</param>
    /// <returns>List of completed queue items.</returns>
    IReadOnlyList<BeadsQueueItem> GetHistory(string projectPath, int limit = 50);

    /// <summary>
    /// Checks if the project has been loaded into the cache.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>True if the project is loaded.</returns>
    bool IsProjectLoaded(string projectPath);

    #endregion
}
