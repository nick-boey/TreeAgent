namespace Homespun.Features.Beads.Data;

/// <summary>
/// Result of a beads sync operation including queue processing.
/// </summary>
public class BeadsSyncResult
{
    /// <summary>
    /// Whether the sync operation completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Whether any conflicts were detected with remote changes.
    /// </summary>
    public bool HadConflicts { get; init; }

    /// <summary>
    /// Details of any conflicts found.
    /// </summary>
    public IReadOnlyList<BeadsQueueItemConflict> Conflicts { get; init; } = [];

    /// <summary>
    /// Error message if the sync failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Number of queue items that were processed.
    /// </summary>
    public int ItemsProcessed { get; init; }

    /// <summary>
    /// When the sync completed.
    /// </summary>
    public DateTime SyncedAt { get; init; }

    /// <summary>
    /// Creates a successful sync result.
    /// </summary>
    public static BeadsSyncResult Succeeded(int itemsProcessed) => new()
    {
        Success = true,
        ItemsProcessed = itemsProcessed,
        SyncedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Creates a failed sync result.
    /// </summary>
    public static BeadsSyncResult Failed(string error) => new()
    {
        Success = false,
        Error = error,
        SyncedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Creates a sync result with conflicts.
    /// </summary>
    public static BeadsSyncResult WithConflicts(IReadOnlyList<BeadsQueueItemConflict> conflicts, int itemsProcessed) => new()
    {
        Success = false,
        HadConflicts = true,
        Conflicts = conflicts,
        ItemsProcessed = itemsProcessed,
        SyncedAt = DateTime.UtcNow
    };
}

/// <summary>
/// Details of a conflict between a queued change and remote state.
/// </summary>
public class BeadsQueueItemConflict
{
    /// <summary>
    /// The queue item that conflicts with remote changes.
    /// </summary>
    public required BeadsQueueItem QueueItem { get; init; }

    /// <summary>
    /// The current state of the issue from the remote database.
    /// </summary>
    public required BeadsIssue RemoteState { get; init; }

    /// <summary>
    /// Human-readable description of the conflict.
    /// </summary>
    public required string ConflictDescription { get; init; }
}
