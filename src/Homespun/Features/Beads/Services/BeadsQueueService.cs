using System.Collections.Concurrent;
using Homespun.Features.Beads.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for managing the queue of beads database modifications with debouncing.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public class BeadsQueueService : IBeadsQueueService, IDisposable
{
    private readonly ConcurrentDictionary<string, ProjectQueueState> _projectQueues = new();
    private readonly TimeSpan _debounceInterval;
    private readonly int _maxHistoryItems;
    private readonly ILogger<BeadsQueueService> _logger;
    private bool _disposed;

    public TimeSpan DebounceInterval => _debounceInterval;

    public event Action<BeadsQueueItem>? ItemEnqueued;
    public event Action<string>? DebounceCompleted;
    public event Action<string, bool>? QueueProcessingCompleted;

    public BeadsQueueService(IOptions<BeadsDatabaseOptions> options, ILogger<BeadsQueueService> logger)
    {
        _debounceInterval = TimeSpan.FromMilliseconds(options.Value.DebounceIntervalMs);
        _maxHistoryItems = options.Value.MaxHistoryItems;
        _logger = logger;
    }

    public void Enqueue(BeadsQueueItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var state = _projectQueues.GetOrAdd(item.ProjectPath, _ => new ProjectQueueState());

        lock (state.Lock)
        {
            state.PendingItems.Add(item);
            state.LastModificationTime = DateTime.UtcNow;

            _logger.LogDebug("Enqueued {Operation} for issue {IssueId} in project {ProjectPath}",
                item.Operation, item.IssueId, item.ProjectPath);

            // Only start debounce timer if not currently processing
            if (!state.IsProcessing)
            {
                StartDebounceTimer(item.ProjectPath, state);
            }
        }

        ItemEnqueued?.Invoke(item);
    }

    public IReadOnlyList<BeadsQueueItem> GetPendingItems(string projectPath)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                return state.PendingItems.ToList().AsReadOnly();
            }
        }
        return Array.Empty<BeadsQueueItem>();
    }

    public IReadOnlyList<BeadsQueueItem> GetAllPendingItems()
    {
        var allItems = new List<BeadsQueueItem>();
        foreach (var kvp in _projectQueues)
        {
            lock (kvp.Value.Lock)
            {
                allItems.AddRange(kvp.Value.PendingItems);
            }
        }
        return allItems.AsReadOnly();
    }

    public void ClearPendingItems(string projectPath)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                state.PendingItems.Clear();
                state.DebounceCts?.Cancel();
                state.DebounceCts?.Dispose();
                state.DebounceCts = null;
                _logger.LogDebug("Cleared pending items for project {ProjectPath}", projectPath);
            }
        }
    }

    public bool IsDebouncing(string projectPath)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                return state.DebounceCts != null && !state.DebounceCts.IsCancellationRequested;
            }
        }
        return false;
    }

    public DateTime? GetLastModificationTime(string projectPath)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                return state.LastModificationTime;
            }
        }
        return null;
    }

    public IReadOnlyList<BeadsQueueItem> GetCompletedHistory(string projectPath, int limit = 50)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                // Return newest first, up to limit
                return state.CompletedHistory
                    .AsEnumerable()
                    .Reverse()
                    .Take(limit)
                    .ToList()
                    .AsReadOnly();
            }
        }
        return Array.Empty<BeadsQueueItem>();
    }

    public void AddToHistory(BeadsQueueItem item)
    {
        var state = _projectQueues.GetOrAdd(item.ProjectPath, _ => new ProjectQueueState());

        lock (state.Lock)
        {
            state.CompletedHistory.Add(item);

            // Trim to max history items
            while (state.CompletedHistory.Count > _maxHistoryItems)
            {
                state.CompletedHistory.RemoveAt(0);
            }

            _logger.LogDebug("Added {Operation} for issue {IssueId} to history",
                item.Operation, item.IssueId);
        }
    }

    public IReadOnlyCollection<string> GetProjectsWithPendingItems()
    {
        var projects = new List<string>();
        foreach (var kvp in _projectQueues)
        {
            lock (kvp.Value.Lock)
            {
                if (kvp.Value.PendingItems.Count > 0)
                {
                    projects.Add(kvp.Key);
                }
            }
        }
        return projects.AsReadOnly();
    }

    public void MarkAsProcessing(string projectPath)
    {
        var state = _projectQueues.GetOrAdd(projectPath, _ => new ProjectQueueState());

        lock (state.Lock)
        {
            state.IsProcessing = true;
            // Cancel any pending debounce timer
            state.DebounceCts?.Cancel();
            state.DebounceCts?.Dispose();
            state.DebounceCts = null;
            _logger.LogDebug("Marked project {ProjectPath} as processing", projectPath);
        }
    }

    public void MarkAsProcessingComplete(string projectPath, bool success)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                state.IsProcessing = false;
                _logger.LogDebug("Marked project {ProjectPath} as processing complete. Success: {Success}",
                    projectPath, success);
            }
        }

        QueueProcessingCompleted?.Invoke(projectPath, success);
    }

    public bool IsProcessing(string projectPath)
    {
        if (_projectQueues.TryGetValue(projectPath, out var state))
        {
            lock (state.Lock)
            {
                return state.IsProcessing;
            }
        }
        return false;
    }

    private void StartDebounceTimer(string projectPath, ProjectQueueState state)
    {
        // Cancel any existing timer
        state.DebounceCts?.Cancel();
        state.DebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        state.DebounceCts = cts;

        _logger.LogDebug("Starting debounce timer for project {ProjectPath}, interval {Interval}ms",
            projectPath, _debounceInterval.TotalMilliseconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceInterval, cts.Token);

                // Check if still valid after delay
                if (!cts.IsCancellationRequested)
                {
                    lock (state.Lock)
                    {
                        state.DebounceCts = null;
                    }

                    _logger.LogDebug("Debounce completed for project {ProjectPath}", projectPath);
                    DebounceCompleted?.Invoke(projectPath);
                }
            }
            catch (OperationCanceledException)
            {
                // Timer was cancelled (reset or disposed)
                _logger.LogDebug("Debounce timer cancelled for project {ProjectPath}", projectPath);
            }
        }, cts.Token);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var kvp in _projectQueues)
        {
            lock (kvp.Value.Lock)
            {
                kvp.Value.DebounceCts?.Cancel();
                kvp.Value.DebounceCts?.Dispose();
            }
        }

        _projectQueues.Clear();
    }

    /// <summary>
    /// Internal state for a single project's queue.
    /// </summary>
    private class ProjectQueueState
    {
        public object Lock { get; } = new();
        public List<BeadsQueueItem> PendingItems { get; } = [];
        public List<BeadsQueueItem> CompletedHistory { get; } = [];
        public DateTime? LastModificationTime { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
        public bool IsProcessing { get; set; }
    }
}
