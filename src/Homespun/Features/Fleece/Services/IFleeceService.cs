using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware service interface for Fleece issue tracking.
/// Wraps Fleece.Core's IIssueService to provide project path context.
/// </summary>
public interface IFleeceService
{
    #region Read Operations

    /// <summary>
    /// Gets a single issue by ID from the specified project.
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The issue, or null if not found.</returns>
    Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Lists issues from the specified project matching the filters.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="type">Optional type filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching issues.</returns>
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets issues that are ready to work on (open status with no blocking parent issues).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of ready issues.</returns>
    Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default);

    #endregion

    #region Write Operations

    /// <summary>
    /// Creates a new issue in the specified project.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="title">Issue title.</param>
    /// <param name="type">Issue type.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="priority">Optional priority (1-5).</param>
    /// <param name="group">Optional group for categorization.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created issue.</returns>
    Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        string? group = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="title">Optional new title.</param>
    /// <param name="status">Optional new status.</param>
    /// <param name="type">Optional new type.</param>
    /// <param name="description">Optional new description.</param>
    /// <param name="priority">Optional new priority.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated issue, or null if not found.</returns>
    Task<Issue?> UpdateIssueAsync(
        string projectPath,
        string issueId,
        string? title = null,
        IssueStatus? status = null,
        IssueType? type = null,
        string? description = null,
        int? priority = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an issue (sets status to Deleted).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the issue was found and deleted.</returns>
    Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default);

    #endregion
}
