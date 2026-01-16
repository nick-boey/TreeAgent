namespace Homespun.Features.Beads.Data;

/// <summary>
/// Types of operations that can be queued for the beads database.
/// </summary>
public enum BeadsOperationType
{
    /// <summary>
    /// Create a new issue.
    /// </summary>
    Create,

    /// <summary>
    /// Update an existing issue.
    /// </summary>
    Update,

    /// <summary>
    /// Close an issue.
    /// </summary>
    Close,

    /// <summary>
    /// Reopen a closed issue.
    /// </summary>
    Reopen,

    /// <summary>
    /// Delete an issue (set status to tombstone).
    /// </summary>
    Delete,

    /// <summary>
    /// Add a label to an issue.
    /// </summary>
    AddLabel,

    /// <summary>
    /// Remove a label from an issue.
    /// </summary>
    RemoveLabel,

    /// <summary>
    /// Add a dependency between issues.
    /// </summary>
    AddDependency,

    /// <summary>
    /// Remove a dependency between issues.
    /// </summary>
    RemoveDependency
}

/// <summary>
/// Status of a queue item during processing.
/// </summary>
public enum BeadsQueueItemStatus
{
    /// <summary>
    /// Item is waiting to be processed.
    /// </summary>
    Pending,

    /// <summary>
    /// Item is currently being processed.
    /// </summary>
    Processing,

    /// <summary>
    /// Item was processed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Item processing failed.
    /// </summary>
    Failed
}
