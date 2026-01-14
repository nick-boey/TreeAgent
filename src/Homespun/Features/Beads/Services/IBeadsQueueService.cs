using Homespun.Features.Beads.Data;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service interface for managing the queue of beads database modifications.
/// Implements debouncing and history tracking for undo capability.
/// </summary>
public interface IBeadsQueueService
{
    /// <summary>
    /// Gets the debounce interval before queue processing starts.
    /// </summary>
    TimeSpan DebounceInterval { get; }

    /// <summary>
    /// Adds an item to the modification queue for a project.
    /// Resets the debounce timer.
    /// </summary>
    /// <param name="item">The queue item to add.</param>
    void Enqueue(BeadsQueueItem item);

    /// <summary>
    /// Gets all pending items for a project that haven't been processed yet.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>Read-only list of pending items.</returns>
    IReadOnlyList<BeadsQueueItem> GetPendingItems(string projectPath);

    /// <summary>
    /// Gets all pending items across all projects.
    /// </summary>
    /// <returns>Read-only list of all pending items.</returns>
    IReadOnlyList<BeadsQueueItem> GetAllPendingItems();

    /// <summary>
    /// Clears all pending items for a project (e.g., after successful processing).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    void ClearPendingItems(string projectPath);

    /// <summary>
    /// Whether a project is currently in a debounce wait period.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>True if waiting for debounce to complete.</returns>
    bool IsDebouncing(string projectPath);

    /// <summary>
    /// Gets the time of the last modification for a project.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>Time of last modification, or null if no pending items.</returns>
    DateTime? GetLastModificationTime(string projectPath);

    /// <summary>
    /// Gets completed history items for a project (for undo capability).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <returns>Read-only list of completed items, newest first.</returns>
    IReadOnlyList<BeadsQueueItem> GetCompletedHistory(string projectPath, int limit = 50);

    /// <summary>
    /// Adds an item to the completed history (called after successful processing).
    /// </summary>
    /// <param name="item">The completed item to add to history.</param>
    void AddToHistory(BeadsQueueItem item);

    /// <summary>
    /// Gets all project paths that have pending items.
    /// </summary>
    /// <returns>Collection of project paths with pending work.</returns>
    IReadOnlyCollection<string> GetProjectsWithPendingItems();

    /// <summary>
    /// Marks a project as currently processing (prevents new debounce timers).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    void MarkAsProcessing(string projectPath);

    /// <summary>
    /// Marks a project as done processing.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="success">Whether processing was successful.</param>
    void MarkAsProcessingComplete(string projectPath, bool success);

    /// <summary>
    /// Whether a project is currently being processed.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <returns>True if processing is in progress.</returns>
    bool IsProcessing(string projectPath);

    /// <summary>
    /// Fired when an item is added to the queue.
    /// </summary>
    event Action<BeadsQueueItem>? ItemEnqueued;

    /// <summary>
    /// Fired when a project's debounce timer completes and processing should start.
    /// </summary>
    event Action<string>? DebounceCompleted;

    /// <summary>
    /// Fired when queue processing completes for a project.
    /// Parameter is (projectPath, success).
    /// </summary>
    event Action<string, bool>? QueueProcessingCompleted;
}
