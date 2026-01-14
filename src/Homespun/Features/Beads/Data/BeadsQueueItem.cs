namespace Homespun.Features.Beads.Data;

/// <summary>
/// Represents a queued modification to the beads database.
/// Stores all information needed to apply the change and undo it if necessary.
/// </summary>
public class BeadsQueueItem
{
    /// <summary>
    /// Unique identifier for this queue item.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Path to the project/repository containing the beads database.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// The beads issue ID being modified (e.g., "hsp-a3f8").
    /// For Create operations, this is the ID that will be assigned.
    /// </summary>
    public required string IssueId { get; init; }

    /// <summary>
    /// The type of operation to perform.
    /// </summary>
    public required BeadsOperationType Operation { get; init; }

    /// <summary>
    /// When this queue item was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Options for creating a new issue (only for Create operations).
    /// </summary>
    public BeadsCreateOptions? CreateOptions { get; init; }

    /// <summary>
    /// Options for updating an issue (only for Update operations).
    /// </summary>
    public BeadsUpdateOptions? UpdateOptions { get; init; }

    /// <summary>
    /// Label to add or remove (only for AddLabel/RemoveLabel operations).
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// ID of the issue this one depends on (only for AddDependency/RemoveDependency operations).
    /// </summary>
    public string? DependsOnIssueId { get; init; }

    /// <summary>
    /// Type of dependency (only for AddDependency operations).
    /// </summary>
    public string? DependencyType { get; init; }

    /// <summary>
    /// Reason for close/reopen/delete operations.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Current processing status of this queue item.
    /// </summary>
    public BeadsQueueItemStatus Status { get; set; } = BeadsQueueItemStatus.Pending;

    /// <summary>
    /// When this item was processed (null if not yet processed).
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Snapshot of the issue before this change (for undo capability).
    /// Null for Create operations.
    /// </summary>
    public BeadsIssue? PreviousState { get; init; }

    /// <summary>
    /// Creates a queue item for creating a new issue.
    /// </summary>
    public static BeadsQueueItem ForCreate(string projectPath, string issueId, BeadsCreateOptions options)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.Create,
            CreatedAt = DateTime.UtcNow,
            CreateOptions = options
        };
    }

    /// <summary>
    /// Creates a queue item for updating an issue.
    /// </summary>
    public static BeadsQueueItem ForUpdate(string projectPath, string issueId, BeadsUpdateOptions options, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.Update,
            CreatedAt = DateTime.UtcNow,
            UpdateOptions = options,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for closing an issue.
    /// </summary>
    public static BeadsQueueItem ForClose(string projectPath, string issueId, string? reason = null, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.Close,
            CreatedAt = DateTime.UtcNow,
            Reason = reason,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for reopening an issue.
    /// </summary>
    public static BeadsQueueItem ForReopen(string projectPath, string issueId, string? reason = null, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.Reopen,
            CreatedAt = DateTime.UtcNow,
            Reason = reason,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for deleting an issue.
    /// </summary>
    public static BeadsQueueItem ForDelete(string projectPath, string issueId, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.Delete,
            CreatedAt = DateTime.UtcNow,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for adding a label.
    /// </summary>
    public static BeadsQueueItem ForAddLabel(string projectPath, string issueId, string label, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.AddLabel,
            CreatedAt = DateTime.UtcNow,
            Label = label,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for removing a label.
    /// </summary>
    public static BeadsQueueItem ForRemoveLabel(string projectPath, string issueId, string label, BeadsIssue? previousState = null)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.RemoveLabel,
            CreatedAt = DateTime.UtcNow,
            Label = label,
            PreviousState = previousState
        };
    }

    /// <summary>
    /// Creates a queue item for adding a dependency.
    /// </summary>
    public static BeadsQueueItem ForAddDependency(string projectPath, string issueId, string dependsOnIssueId, string dependencyType = "blocks")
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.AddDependency,
            CreatedAt = DateTime.UtcNow,
            DependsOnIssueId = dependsOnIssueId,
            DependencyType = dependencyType
        };
    }

    /// <summary>
    /// Creates a queue item for removing a dependency.
    /// </summary>
    public static BeadsQueueItem ForRemoveDependency(string projectPath, string issueId, string dependsOnIssueId)
    {
        return new BeadsQueueItem
        {
            Id = Guid.NewGuid().ToString(),
            ProjectPath = projectPath,
            IssueId = issueId,
            Operation = BeadsOperationType.RemoveDependency,
            CreatedAt = DateTime.UtcNow,
            DependsOnIssueId = dependsOnIssueId
        };
    }
}
