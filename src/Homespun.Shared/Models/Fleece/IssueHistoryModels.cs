namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Snapshot of the per-project undo/redo stacks.
/// </summary>
public sealed record IssueHistoryState
{
    public required bool CanUndo { get; init; }
    public required bool CanRedo { get; init; }
    public required int UndoCount { get; init; }
    public required int RedoCount { get; init; }
}

/// <summary>
/// Response shape for /history/undo and /history/redo.
/// On empty-stack calls, <see cref="Success"/> is false and
/// <see cref="ErrorMessage"/> carries the user-facing reason.
/// </summary>
public sealed record IssueHistoryOperationResponse
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required IssueHistoryState State { get; init; }
}
