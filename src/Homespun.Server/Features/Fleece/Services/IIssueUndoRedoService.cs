using Fleece.Core.EventSourcing.Events;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// In-memory per-project undo/redo stack of compensating Fleece events.
/// On undo, the captured inverse events are appended to the active change file
/// via <c>IEventStore.AppendEventsAsync</c>; on redo, the original forward
/// events are re-appended. The stacks are NOT persisted — server restart
/// clears them. See spec <c>fleece-issue-tracking</c> requirement
/// "Undo/redo issue history via compensating events".
///
/// The stack key is the project's local path; the controller resolves
/// projectId → projectPath before calling these methods.
/// </summary>
public interface IIssueUndoRedoService
{
    /// <summary>
    /// Pushes one undoable step onto the undo stack and clears the redo stack.
    /// Called by <c>ProjectFleeceService</c> after a successful user-initiated
    /// mutation when <c>recordUndo: true</c>.
    /// </summary>
    /// <param name="projectPath">Local path containing the <c>.fleece/</c> directory.</param>
    /// <param name="forwardEvents">The library events that the forward write
    /// produced. Re-appended on redo with refreshed timestamps.</param>
    /// <param name="inverseEvents">Compensating events that, when appended, roll
    /// back the forward write under last-writer-wins replay.</param>
    /// <param name="undoSnapshots">In-memory issue states to install on undo
    /// (each item replaces the cache entry for its id). May be empty for
    /// event-only operations.</param>
    /// <param name="redoSnapshots">In-memory issue states to install on redo.</param>
    void PushInverse(
        string projectPath,
        IReadOnlyList<IssueEvent> forwardEvents,
        IReadOnlyList<IssueEvent> inverseEvents,
        IReadOnlyList<Issue> undoSnapshots,
        IReadOnlyList<Issue> redoSnapshots);

    /// <summary>Current stack pointers for the given project path.</summary>
    Task<IssueHistoryState> GetStateAsync(string projectPath);

    /// <summary>
    /// Pops the top undo entry, appends its inverse events to the active change
    /// file, installs <see cref="UndoSnapshots"/> on the in-memory cache and
    /// snapshot, and pushes the entry onto the redo stack.
    /// </summary>
    /// <returns>True if an entry was undone; false when the stack was empty.</returns>
    Task<bool> UndoAsync(string projectPath, CancellationToken ct);

    /// <summary>Mirror of <see cref="UndoAsync"/> for the redo stack.</summary>
    Task<bool> RedoAsync(string projectPath, CancellationToken ct);

    /// <summary>
    /// Opens an ambient batch scope on the current async-execution context.
    /// Within the scope, <see cref="PushInverse"/> calls accumulate into a single
    /// stack entry committed when the scope is disposed. Used by HTTP endpoints
    /// that issue multiple internal writes for one logical user action (e.g.
    /// create-with-parent emits a <c>CreateEvent</c> plus an <c>AddEvent</c> but
    /// must surface as one undo step).
    /// </summary>
    IDisposable BeginBatch(string projectPath);
}

/// <summary>
/// Maximum entries kept in each per-project stack. Excess pushes drop the
/// oldest entry at the bottom of the stack (FIFO eviction).
/// </summary>
public static class IssueUndoRedoConstants
{
    public const int MaxStackDepth = 100;
}
