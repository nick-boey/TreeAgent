using System.Collections.Concurrent;
using Fleece.Core.EventSourcing.Events;
using Fleece.Core.EventSourcing.Services;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Features.Fleece.Services;

/// <inheritdoc />
public sealed class IssueUndoRedoService : IIssueUndoRedoService
{
    private readonly ConcurrentDictionary<string, ProjectUndoState> _stacks =
        new(StringComparer.Ordinal);

    private static readonly AsyncLocal<BatchAccumulator?> _ambientBatch = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IssueUndoRedoService> _logger;

    public IssueUndoRedoService(
        IServiceProvider serviceProvider,
        ILogger<IssueUndoRedoService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private IProjectFleeceService ProjectFleeceService =>
        _serviceProvider.GetRequiredService<IProjectFleeceService>();

    public void PushInverse(
        string projectPath,
        IReadOnlyList<IssueEvent> forwardEvents,
        IReadOnlyList<IssueEvent> inverseEvents,
        IReadOnlyList<Issue> undoSnapshots,
        IReadOnlyList<Issue> redoSnapshots)
    {
        var ambient = _ambientBatch.Value;
        if (ambient is not null && ambient.ProjectPath == projectPath)
        {
            ambient.AddStep(forwardEvents, inverseEvents, undoSnapshots, redoSnapshots);
            return;
        }

        PushEntry(projectPath, new UndoEntry(
            ForwardEvents: forwardEvents,
            InverseEvents: inverseEvents,
            UndoSnapshots: undoSnapshots,
            RedoSnapshots: redoSnapshots));
    }

    private void PushEntry(string projectPath, UndoEntry entry)
    {
        var state = _stacks.GetOrAdd(projectPath, _ => new ProjectUndoState());
        lock (state.SyncRoot)
        {
            state.Undo.Push(entry);
            EvictOldestIfOverBound(state.Undo);
            state.Redo.Clear();
        }
    }

    public IDisposable BeginBatch(string projectPath)
    {
        var prior = _ambientBatch.Value;
        var batch = new BatchAccumulator(projectPath);
        _ambientBatch.Value = batch;
        return new BatchScope(this, batch, prior);
    }

    private sealed class BatchScope : IDisposable
    {
        private readonly IssueUndoRedoService _owner;
        private readonly BatchAccumulator _batch;
        private readonly BatchAccumulator? _prior;
        private bool _disposed;

        public BatchScope(IssueUndoRedoService owner, BatchAccumulator batch, BatchAccumulator? prior)
        {
            _owner = owner;
            _batch = batch;
            _prior = prior;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ambientBatch.Value = _prior;
            if (_batch.IsEmpty) return;
            _owner.PushEntry(_batch.ProjectPath, _batch.ToEntry());
        }
    }

    private sealed class BatchAccumulator
    {
        public string ProjectPath { get; }
        private readonly List<IssueEvent> _forwards = new();
        private readonly List<IssueEvent> _inverses = new();
        // Undo: first occurrence wins (state before the batch began).
        // Redo: last occurrence wins (state after the batch completed).
        private readonly Dictionary<string, Issue> _undoByIssue = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Issue> _redoByIssue = new(StringComparer.OrdinalIgnoreCase);

        public BatchAccumulator(string projectPath) { ProjectPath = projectPath; }

        public bool IsEmpty => _forwards.Count == 0 && _inverses.Count == 0;

        public void AddStep(
            IReadOnlyList<IssueEvent> forwardEvents,
            IReadOnlyList<IssueEvent> inverseEvents,
            IReadOnlyList<Issue> undoSnapshots,
            IReadOnlyList<Issue> redoSnapshots)
        {
            _forwards.AddRange(forwardEvents);
            // Prepend so undo applies steps in reverse order.
            for (int i = inverseEvents.Count - 1; i >= 0; i--)
                _inverses.Insert(0, inverseEvents[i]);
            foreach (var issue in undoSnapshots)
            {
                if (!_undoByIssue.ContainsKey(issue.Id))
                    _undoByIssue[issue.Id] = issue;
            }
            foreach (var issue in redoSnapshots)
                _redoByIssue[issue.Id] = issue;
        }

        public UndoEntry ToEntry() => new(
            ForwardEvents: _forwards.ToList(),
            InverseEvents: _inverses.ToList(),
            UndoSnapshots: _undoByIssue.Values.ToList(),
            RedoSnapshots: _redoByIssue.Values.ToList());
    }

    public Task<IssueHistoryState> GetStateAsync(string projectPath)
    {
        if (_stacks.TryGetValue(projectPath, out var state))
        {
            lock (state.SyncRoot)
            {
                return Task.FromResult(new IssueHistoryState
                {
                    CanUndo = state.Undo.Count > 0,
                    CanRedo = state.Redo.Count > 0,
                    UndoCount = state.Undo.Count,
                    RedoCount = state.Redo.Count,
                });
            }
        }

        return Task.FromResult(new IssueHistoryState
        {
            CanUndo = false, CanRedo = false, UndoCount = 0, RedoCount = 0,
        });
    }

    public async Task<bool> UndoAsync(string projectPath, CancellationToken ct)
    {
        if (!_stacks.TryGetValue(projectPath, out var state)) return false;
        UndoEntry entry;
        lock (state.SyncRoot)
        {
            if (state.Undo.Count == 0) return false;
            entry = state.Undo.Pop();
        }

        try
        {
            await AppendWithFreshTimestampsAsync(projectPath, entry.InverseEvents, ct);
            await ProjectFleeceService.ApplyUndoSnapshotsAsync(projectPath, entry.UndoSnapshots, ct);
        }
        catch
        {
            lock (state.SyncRoot) { state.Undo.Push(entry); }
            throw;
        }

        lock (state.SyncRoot)
        {
            state.Redo.Push(entry);
            EvictOldestIfOverBound(state.Redo);
        }

        _logger.LogInformation("Undo applied for project {ProjectPath} ({Count} events)",
            projectPath, entry.InverseEvents.Count);
        return true;
    }

    public async Task<bool> RedoAsync(string projectPath, CancellationToken ct)
    {
        if (!_stacks.TryGetValue(projectPath, out var state)) return false;
        UndoEntry entry;
        lock (state.SyncRoot)
        {
            if (state.Redo.Count == 0) return false;
            entry = state.Redo.Pop();
        }

        try
        {
            await AppendWithFreshTimestampsAsync(projectPath, entry.ForwardEvents, ct);
            await ProjectFleeceService.ApplyUndoSnapshotsAsync(projectPath, entry.RedoSnapshots, ct);
        }
        catch
        {
            lock (state.SyncRoot) { state.Redo.Push(entry); }
            throw;
        }

        lock (state.SyncRoot)
        {
            state.Undo.Push(entry);
            EvictOldestIfOverBound(state.Undo);
        }

        _logger.LogInformation("Redo applied for project {ProjectPath} ({Count} events)",
            projectPath, entry.ForwardEvents.Count);
        return true;
    }

    private static async Task AppendWithFreshTimestampsAsync(
        string projectPath, IReadOnlyList<IssueEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        var refreshed = events.Select(e => WithTimestamp(e, now)).ToList();
        var eventStore = new EventStore(projectPath);
        await eventStore.AppendEventsAsync(refreshed, ct);
    }

    private static IssueEvent WithTimestamp(IssueEvent ev, DateTimeOffset at) => ev switch
    {
        SetEvent s => s with { At = at },
        AddEvent a => a with { At = at },
        RemoveEvent r => r with { At = at },
        CreateEvent c => c with { At = at },
        HardDeleteEvent h => h with { At = at },
        _ => throw new InvalidOperationException($"Unsupported event kind: {ev.Kind}")
    };

    private static void EvictOldestIfOverBound(Stack<UndoEntry> stack)
    {
        if (stack.Count <= IssueUndoRedoConstants.MaxStackDepth) return;
        var keep = new Stack<UndoEntry>(IssueUndoRedoConstants.MaxStackDepth);
        var asList = stack.ToArray();
        // stack.ToArray() returns top-first; drop the last (oldest) element.
        for (int i = asList.Length - 2; i >= 0; i--)
            keep.Push(asList[i]);
        stack.Clear();
        foreach (var e in keep.Reverse())
            stack.Push(e);
    }

    private sealed record UndoEntry(
        IReadOnlyList<IssueEvent> ForwardEvents,
        IReadOnlyList<IssueEvent> InverseEvents,
        IReadOnlyList<Issue> UndoSnapshots,
        IReadOnlyList<Issue> RedoSnapshots);

    private sealed class ProjectUndoState
    {
        public Stack<UndoEntry> Undo { get; } = new();
        public Stack<UndoEntry> Redo { get; } = new();
        public object SyncRoot { get; } = new();
    }
}
