using Homespun.Features.Beads.Data;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service interface for interacting with the beads CLI (bd).
/// All methods execute bd commands and parse their JSON output.
/// </summary>
public interface IBeadsService
{
    #region Issue CRUD
    
    /// <summary>
    /// Gets a single issue by ID.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID (e.g., "bd-a3f8").</param>
    /// <returns>The issue, or null if not found.</returns>
    Task<BeadsIssue?> GetIssueAsync(string workingDirectory, string issueId);
    
    /// <summary>
    /// Lists issues matching the specified options.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="options">Filter options.</param>
    /// <returns>List of matching issues.</returns>
    Task<List<BeadsIssue>> ListIssuesAsync(string workingDirectory, BeadsListOptions? options = null);
    
    /// <summary>
    /// Gets issues that are ready to work on (no blocking dependencies).
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <returns>List of ready issues.</returns>
    Task<List<BeadsIssue>> GetReadyIssuesAsync(string workingDirectory);
    
    /// <summary>
    /// Creates a new issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="options">Options for creating the issue.</param>
    /// <returns>The created issue.</returns>
    Task<BeadsIssue> CreateIssueAsync(string workingDirectory, BeadsCreateOptions options);
    
    /// <summary>
    /// Updates an existing issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="options">Options for updating the issue.</param>
    /// <returns>True if the update succeeded.</returns>
    Task<bool> UpdateIssueAsync(string workingDirectory, string issueId, BeadsUpdateOptions options);
    
    /// <summary>
    /// Closes an issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="reason">Optional reason for closing.</param>
    /// <returns>True if the close succeeded.</returns>
    Task<bool> CloseIssueAsync(string workingDirectory, string issueId, string? reason = null);
    
    /// <summary>
    /// Reopens a closed issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="reason">Optional reason for reopening.</param>
    /// <returns>True if the reopen succeeded.</returns>
    Task<bool> ReopenIssueAsync(string workingDirectory, string issueId, string? reason = null);
    
    #endregion
    
    #region Dependencies
    
    /// <summary>
    /// Adds a dependency between two issues.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="childId">The issue that depends on another.</param>
    /// <param name="parentId">The issue being depended upon.</param>
    /// <param name="type">The type of dependency (default: "blocks").</param>
    /// <returns>True if the dependency was added.</returns>
    Task<bool> AddDependencyAsync(string workingDirectory, string childId, string parentId, string type = "blocks");
    
    /// <summary>
    /// Gets the dependency tree for an issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <returns>List of dependencies.</returns>
    Task<List<BeadsDependency>> GetDependencyTreeAsync(string workingDirectory, string issueId);
    
    #endregion
    
    #region Labels
    
    /// <summary>
    /// Adds a label to an issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="label">The label to add.</param>
    /// <returns>True if the label was added.</returns>
    Task<bool> AddLabelAsync(string workingDirectory, string issueId, string label);
    
    /// <summary>
    /// Removes a label from an issue.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <param name="issueId">The beads issue ID.</param>
    /// <param name="label">The label to remove.</param>
    /// <returns>True if the label was removed.</returns>
    Task<bool> RemoveLabelAsync(string workingDirectory, string issueId, string label);
    
    #endregion
    
    #region Sync and Info
    
    /// <summary>
    /// Syncs beads data (export, commit, pull, import, push).
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    Task SyncAsync(string workingDirectory);
    
    /// <summary>
    /// Gets information about the beads installation.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the command in.</param>
    /// <returns>Beads installation info.</returns>
    Task<BeadsInfo> GetInfoAsync(string workingDirectory);
    
    /// <summary>
    /// Checks if beads is initialized in the given directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to check.</param>
    /// <returns>True if beads is initialized.</returns>
    Task<bool> IsInitializedAsync(string workingDirectory);
    
    #endregion
}
