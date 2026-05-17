using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Homespun.Shared.Requests;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware service interface for Fleece issue tracking.
/// Wraps Fleece.Core's IFleeceService to provide project path context.
/// </summary>
public interface IProjectFleeceService
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
    /// When no filter and <paramref name="includeAll"/> is false, terminal-status
    /// issues (Deleted/Archived/Closed/Complete) are excluded.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="type">Optional type filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="includeAll">If true, return every issue regardless of status. Used by the visible-set
    /// endpoint to fetch the unfiltered list before applying ancestor traversal.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching issues.</returns>
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        bool includeAll = false,
        CancellationToken ct = default);

    /// <summary>
    /// Gets issues that are ready to work on (open status with no blocking parent issues).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of ready issues.</returns>
    Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Gets the prior sibling in series execution (sibling with lower sortOrder in same parent).
    /// Returns null if no prior sibling exists.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID to find the prior sibling for.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The prior sibling issue, or null if none exists.</returns>
    Task<Issue?> GetPriorSiblingAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Gets all direct children of an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The parent issue ID.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of child issues sorted by sortOrder.</returns>
    Task<IReadOnlyList<Issue>> GetChildrenAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Loads every issue from an arbitrary path (e.g. an agent clone) directly through
    /// a fresh <c>IFleeceService</c>, bypassing the in-memory cache used for the project's
    /// owned path. Use this for ad-hoc reads against paths the caller does not manage.
    /// </summary>
    /// <param name="path">Path to a working directory containing a .fleece/ folder.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Issue>> GetAllIssuesFromPathAsync(string path, CancellationToken ct = default);

    #endregion

    #region Cache Management

    /// <summary>
    /// Invalidates the in-memory cache and reloads all issues from disk for the specified project.
    /// Call this after external changes to .fleece/ files (e.g., git sync operations).
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="ct">Cancellation token</param>
    Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default);

    #endregion

    #region Task Graph Operations

    /// <summary>
    /// Builds a task graph layout for the specified project via <see cref="IIssueLayoutService"/>.
    /// The layout organizes issues with actionable items at lane 0 (left) and
    /// parent/blocking issues at higher lanes (right).
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The <see cref="GraphLayout{Issue}"/>, or null if no issues exist.</returns>
    Task<GraphLayout<Issue>?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Builds a task graph layout for the specified project, including additional issues by ID
    /// regardless of their status. Uses <see cref="IIssueLayoutService.LayoutForTree"/> on the
    /// pre-filtered issue set.
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="additionalIssueIds">Issue IDs to include regardless of status.
    /// These issues will be included in the graph even if their status would normally exclude them.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The <see cref="GraphLayout{Issue}"/>, or null if no issues exist.</returns>
    Task<GraphLayout<Issue>?> GetTaskGraphWithAdditionalIssuesAsync(
        string projectPath,
        IEnumerable<string>? additionalIssueIds,
        CancellationToken ct = default);

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
    /// <param name="executionMode">Optional execution mode for child issues (defaults to Series).</param>
    /// <param name="status">Optional initial status (defaults to Open).</param>
    /// <param name="assignedTo">Optional email of the user to assign the issue to.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created issue.</returns>
    Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        IssueStatus? status = null,
        string? assignedTo = null,
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
    /// <param name="executionMode">Optional execution mode for child issues.</param>
    /// <param name="workingBranchId">Optional working branch ID.</param>
    /// <param name="assignedTo">Optional email of the user to assign the issue to.</param>
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
        ExecutionMode? executionMode = null,
        string? workingBranchId = null,
        string? assignedTo = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an issue (sets status to Deleted).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the issue was found and deleted.</returns>
    Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Adds a parent relationship to an issue using Fleece.Core's DependencyService.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue that will have the parent added.</param>
    /// <param name="parentId">The ID of the parent issue to add.</param>
    /// <param name="siblingIssueId">Optional sibling issue ID for positioning (Before/After).</param>
    /// <param name="insertBefore">If true, insert before the sibling; if false, insert after.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue with the new parent relationship.</returns>
    Task<Issue> AddParentAsync(string projectPath, string childId, string parentId, string? siblingIssueId = null, bool insertBefore = false, CancellationToken ct = default);

    /// <summary>
    /// Removes a parent relationship from an issue using Fleece.Core's DependencyService.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue that will have the parent removed.</param>
    /// <param name="parentId">The ID of the parent issue to remove.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue with the parent relationship removed.</returns>
    Task<Issue> RemoveParentAsync(string projectPath, string childId, string parentId, CancellationToken ct = default);

    /// <summary>
    /// Removes all parent relationships from an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The ID of the issue to remove all parents from.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated issue with all parent relationships removed.</returns>
    /// <exception cref="KeyNotFoundException">If the issue is not found.</exception>
    Task<Issue> RemoveAllParentsAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether setting a parent relationship would create a cycle.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the issue that would become the child.</param>
    /// <param name="parentId">The ID of the issue that would become the parent.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the relationship would create a cycle, false otherwise.</returns>
    Task<bool> WouldCreateCycleAsync(string projectPath, string childId, string parentId, CancellationToken ct = default);

    /// <summary>
    /// Sets the parent of an issue, optionally replacing all existing parents.
    /// Uses Fleece.Core's DependencyService which handles cycle detection internally.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue.</param>
    /// <param name="parentId">The ID of the new parent issue.</param>
    /// <param name="addToExisting">If true, adds to existing parents; if false, replaces all existing parents.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue.</returns>
    /// <exception cref="InvalidOperationException">If the relationship would create a cycle.</exception>
    Task<Issue> SetParentAsync(string projectPath, string childId, string parentId, bool addToExisting = false, CancellationToken ct = default);

    /// <summary>
    /// Moves a series sibling issue up or down using Fleece.Core's DependencyService.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID to move.</param>
    /// <param name="direction">Direction to move (Up or Down).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated issue with its new sort order.</returns>
    /// <exception cref="KeyNotFoundException">If the issue is not found.</exception>
    /// <exception cref="InvalidOperationException">If the issue has no parent, multiple parents, or is already first/last.</exception>
    Task<Issue> MoveSeriesSiblingAsync(string projectPath, string issueId, MoveDirection direction, CancellationToken ct = default);

    #endregion
}