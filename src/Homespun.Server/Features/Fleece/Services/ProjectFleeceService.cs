using System.Collections.Concurrent;
using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;
using Homespun.Shared.Requests;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware implementation of IProjectFleeceService.
/// Uses a write-through cache pattern: reads are served from an in-memory cache,
/// while writes update the cache immediately and queue persistence to disk
/// via the <see cref="IIssueSerializationQueue"/>.
/// </summary>
public sealed class ProjectFleeceService : IProjectFleeceService, IDisposable
{
    private readonly ConcurrentDictionary<string, IFleeceService> _fleeceServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Issue>> _issueCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _cacheInitialized = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIssueSerializationQueue _serializationQueue;
    private readonly IIssueLayoutService _issueLayoutService;
    private readonly ILogger<ProjectFleeceService> _logger;
    private bool _disposed;

    public ProjectFleeceService(
        IIssueSerializationQueue serializationQueue,
        IIssueLayoutService issueLayoutService,
        ILogger<ProjectFleeceService> logger)
    {
        _serializationQueue = serializationQueue;
        _issueLayoutService = issueLayoutService;
        _logger = logger;
    }

    private IFleeceService GetOrCreateFleeceService(string projectPath)
    {
        return _fleeceServices.GetOrAdd(projectPath, path =>
        {
            _logger.LogDebug("Creating new IFleeceService for project: {ProjectPath}", path);
            var filePath = EnsureSnapshotFile(path);
            var settingsService = new SettingsService(path);
            var gitConfigService = new GitConfigService(settingsService);
            return FleeceService.ForFile(filePath, settingsService, gitConfigService);
        });
    }

    /// <summary>
    /// Ensures the stable snapshot file (.fleece/issues.jsonl) exists. The Fleece 3.1
    /// event-sourced storage projects events into this single file, so the legacy
    /// hash-file consolidation done at 3.0 is no longer needed — `fleece migrate`
    /// handled that once during the upgrade.
    /// </summary>
    private static string EnsureSnapshotFile(string projectPath)
    {
        var fleeceDir = Path.Combine(projectPath, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        var snapshotPath = Path.Combine(fleeceDir, "issues.jsonl");
        if (!File.Exists(snapshotPath))
            File.WriteAllText(snapshotPath, "");

        return snapshotPath;
    }

    private async Task<ConcurrentDictionary<string, Issue>> EnsureCacheLoadedAsync(string projectPath, CancellationToken ct)
    {
        var cache = _issueCache.GetOrAdd(projectPath, _ => new ConcurrentDictionary<string, Issue>(StringComparer.OrdinalIgnoreCase));

        if (!_cacheInitialized.TryGetValue(projectPath, out var initialized) || !initialized)
        {
            _logger.LogDebug("Cache miss for project {ProjectPath}, loading from disk", projectPath);
            var service = GetOrCreateFleeceService(projectPath);
            var allIssues = await service.GetAllAsync(ct);
            foreach (var issue in allIssues)
            {
                cache[issue.Id] = issue;
            }
            _cacheInitialized[projectPath] = true;
            _logger.LogDebug("Loaded {Count} issues into cache for project: {ProjectPath}", allIssues.Count, projectPath);
        }
        else
        {
            _logger.LogDebug("Cache hit for project {ProjectPath}, returning {Count} cached issues", projectPath, cache.Count);
        }

        return cache;
    }

    #region Cache Management

    public async Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Reloading issues from disk for project: {ProjectPath}", projectPath);
        _fleeceServices.TryRemove(projectPath, out _);
        _cacheInitialized.TryRemove(projectPath, out _);
        _issueCache.TryRemove(projectPath, out _);
        await EnsureCacheLoadedAsync(projectPath, ct);
        _logger.LogInformation("Reloaded issues from disk for project: {ProjectPath}", projectPath);
    }

    #endregion

    #region Read Operations

    public async Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.TryGetValue(issueId, out var issue) ? issue : null;
    }

    public async Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath, IssueStatus? status = null, IssueType? type = null, int? priority = null, bool includeAll = false, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        IEnumerable<Issue> issues = cache.Values;

        if (status.HasValue) issues = issues.Where(i => i.Status == status.Value);
        if (type.HasValue) issues = issues.Where(i => i.Type == type.Value);
        if (priority.HasValue) issues = issues.Where(i => i.Priority == priority.Value);

        if (!includeAll && !status.HasValue && !type.HasValue && !priority.HasValue)
        {
            issues = issues.Where(i => i.Status is not (IssueStatus.Deleted or IssueStatus.Archived or IssueStatus.Closed or IssueStatus.Complete));
        }

        return issues.ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var allIssues = cache.Values.ToList();
        var openIssues = allIssues.Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review).ToList();
        var issueMap = allIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        return openIssues
            .Where(issue =>
            {
                if (issue.ParentIssues.Count == 0) return true;
                return issue.ParentIssues.All(parentRef =>
                {
                    if (issueMap.TryGetValue(parentRef.ParentIssue, out var parent))
                        return parent.Status is IssueStatus.Complete or IssueStatus.Closed;
                    return true;
                });
            })
            .ToList();
    }

    public async Task<Issue?> GetPriorSiblingAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        if (!cache.TryGetValue(issueId, out var issue)) return null;
        if (issue.ParentIssues.Count == 0) return null;

        foreach (var parentRef in issue.ParentIssues)
        {
            var parentId = parentRef.ParentIssue;
            var targetSortOrder = parentRef.SortOrder ?? "0";
            var sibling = cache.Values
                .Where(i => i.Id != issueId && i.ParentIssues.Any(p => p.ParentIssue == parentId))
                .Select(i => new { Issue = i, SortOrder = i.ParentIssues.First(p => p.ParentIssue == parentId).SortOrder ?? "0" })
                .Where(s => string.Compare(s.SortOrder, targetSortOrder, StringComparison.Ordinal) < 0)
                .OrderByDescending(s => s.SortOrder, StringComparer.Ordinal)
                .FirstOrDefault();
            if (sibling != null) return sibling.Issue;
        }
        return null;
    }

    public async Task<IReadOnlyList<Issue>> GetChildrenAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.Values
            .Where(i => i.ParentIssues.Any(p => p.ParentIssue == issueId))
            .Select(i => new { Issue = i, SortOrder = i.ParentIssues.First(p => p.ParentIssue == issueId).SortOrder ?? "0" })
            .OrderBy(c => c.SortOrder, StringComparer.Ordinal)
            .Select(c => c.Issue)
            .ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetAllIssuesFromPathAsync(string path, CancellationToken ct = default)
    {
        var fleeceDir = Path.Combine(path, ".fleece");
        if (!Directory.Exists(fleeceDir))
            return [];

        var snapshotPath = EnsureSnapshotFile(path);
        var settingsService = new SettingsService(path);
        var gitConfigService = new GitConfigService(settingsService);
        var service = FleeceService.ForFile(snapshotPath, settingsService, gitConfigService);
        return await service.GetAllAsync(ct);
    }

    #endregion

    #region Write Operations

    public async Task<Issue> CreateIssueAsync(
        string projectPath, string title, IssueType type, string? description = null,
        int? priority = null, ExecutionMode? executionMode = null, IssueStatus? status = null,
        string? assignedTo = null, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var service = GetOrCreateFleeceService(projectPath);
        var issue = await service.CreateAsync(title: title, type: type, description: description,
            priority: priority, executionMode: executionMode, assignedTo: assignedTo, cancellationToken: ct);

        if (status.HasValue && status.Value != IssueStatus.Open)
            issue = await service.UpdateAsync(issue.Id, status: status.Value, cancellationToken: ct);

        cache[issue.Id] = issue;

        await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
            ProjectPath: projectPath, IssueId: issue.Id, Type: WriteOperationType.Create,
            WriteAction: async (innerCt) =>
            {
                var svc = GetOrCreateFleeceService(projectPath);
                var existing = await svc.GetByIdAsync(issue.Id, innerCt);
                if (existing == null)
                    await svc.CreateAsync(title: issue.Title, type: issue.Type, description: issue.Description,
                        priority: issue.Priority, executionMode: issue.ExecutionMode, cancellationToken: innerCt);
            },
            QueuedAt: DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Created issue '{IssueId}' ({Type}): {Title}{ExecutionMode}{Status}",
            issue.Id, type, title,
            executionMode.HasValue ? $" [ExecutionMode: {executionMode}]" : "",
            status.HasValue && status.Value != IssueStatus.Open ? $" [Status: {status}]" : "");

        return issue;
    }

    public async Task<Issue?> UpdateIssueAsync(
        string projectPath, string issueId, string? title = null, IssueStatus? status = null,
        IssueType? type = null, string? description = null, int? priority = null,
        ExecutionMode? executionMode = null, string? workingBranchId = null,
        string? assignedTo = null, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        if (!cache.TryGetValue(issueId, out _))
        {
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            return null;
        }

        var service = GetOrCreateFleeceService(projectPath);
        try
        {
            var issue = await service.UpdateAsync(id: issueId, title: title, status: status, type: type,
                description: description, priority: priority, executionMode: executionMode,
                workingBranchId: workingBranchId, assignedTo: assignedTo, cancellationToken: ct);
            cache[issueId] = issue;

            await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
                ProjectPath: projectPath, IssueId: issueId, Type: WriteOperationType.Update,
                WriteAction: async (innerCt) =>
                {
                    var svc = GetOrCreateFleeceService(projectPath);
                    await svc.UpdateAsync(id: issueId, title: title, status: status, type: type,
                        description: description, priority: priority, executionMode: executionMode,
                        workingBranchId: workingBranchId, assignedTo: assignedTo, cancellationToken: innerCt);
                },
                QueuedAt: DateTimeOffset.UtcNow), ct);

            var changes = new List<string>();
            if (title != null) changes.Add($"title='{title}'");
            if (status != null) changes.Add($"status={status}");
            if (type != null) changes.Add($"type={type}");
            if (description != null) changes.Add("description updated");
            if (priority != null) changes.Add($"priority={priority}");
            if (executionMode != null) changes.Add($"executionMode={executionMode}");
            if (workingBranchId != null) changes.Add($"workingBranchId='{workingBranchId}'");
            if (assignedTo != null) changes.Add($"assignedTo='{assignedTo}'");

            _logger.LogInformation("Updated issue '{IssueId}': {Changes}", issueId, string.Join(", ", changes));
            return issue;
        }
        catch (KeyNotFoundException)
        {
            cache.TryRemove(issueId, out _);
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            return null;
        }
    }

    public async Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var service = GetOrCreateFleeceService(projectPath);
        var deleted = await service.DeleteAsync(issueId, ct);

        if (deleted)
        {
            var updatedIssue = await service.GetByIdAsync(issueId, ct);
            if (updatedIssue != null) cache[issueId] = updatedIssue;
            else cache.TryRemove(issueId, out _);

            await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
                ProjectPath: projectPath, IssueId: issueId, Type: WriteOperationType.Delete,
                WriteAction: async (innerCt) => { var svc = GetOrCreateFleeceService(projectPath); await svc.DeleteAsync(issueId, innerCt); },
                QueuedAt: DateTimeOffset.UtcNow), ct);

            _logger.LogInformation("Deleted issue '{IssueId}'", issueId);
        }
        else
        {
            _logger.LogWarning("Failed to delete issue '{IssueId}' - not found", issueId);
        }
        return deleted;
    }

    public async Task<Issue> AddParentAsync(string projectPath, string childId, string parentId,
        string? siblingIssueId = null, bool insertBefore = false, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var service = GetOrCreateFleeceService(projectPath);

        DependencyPosition? position = null;
        if (!string.IsNullOrEmpty(siblingIssueId))
        {
            position = new DependencyPosition
            {
                Kind = insertBefore ? DependencyPositionKind.Before : DependencyPositionKind.After,
                SiblingId = siblingIssueId
            };
        }

        var issue = await service.AddDependencyAsync(parentId, childId, position, cancellationToken: ct);
        cache[childId] = issue;
        _logger.LogInformation("Added parent '{ParentId}' to issue '{ChildId}'", parentId, childId);
        return issue;
    }

    public async Task<Issue> RemoveParentAsync(string projectPath, string childId, string parentId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var service = GetOrCreateFleeceService(projectPath);
        var issue = await service.RemoveDependencyAsync(parentId, childId, ct);
        // Fleece.Core v2.1.0 soft-deletes parents (Active=false) instead of removing them.
        // Filter to only active parents so the rest of the codebase sees a clean state.
        issue = issue with { ParentIssues = issue.ParentIssues.Where(p => p.Active).ToList() };
        cache[childId] = issue;
        _logger.LogInformation("Removed parent '{ParentId}' from issue '{ChildId}'", parentId, childId);
        return issue;
    }

    public async Task<Issue> RemoveAllParentsAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        if (!cache.TryGetValue(issueId, out _))
        {
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            throw new KeyNotFoundException($"Issue '{issueId}' not found");
        }

        var service = GetOrCreateFleeceService(projectPath);
        var issue = await service.UpdateAsync(issueId, parentIssues: new List<ParentIssueRef>(), cancellationToken: ct);
        cache[issueId] = issue;

        await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
            ProjectPath: projectPath, IssueId: issueId, Type: WriteOperationType.Update,
            WriteAction: async (innerCt) =>
            {
                var svc = GetOrCreateFleeceService(projectPath);
                var currentIssue = await svc.GetByIdAsync(issueId, innerCt);
                if (currentIssue != null)
                    await svc.UpdateAsync(issueId, parentIssues: new List<ParentIssueRef>(), cancellationToken: innerCt);
            },
            QueuedAt: DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Removed all parents from issue '{IssueId}'", issueId);
        return issue;
    }

    public async Task<bool> WouldCreateCycleAsync(string projectPath, string childId, string parentId, CancellationToken ct = default)
    {
        if (string.Equals(childId, parentId, StringComparison.OrdinalIgnoreCase)) return true;

        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(parentId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (visited.Contains(currentId)) continue;
            visited.Add(currentId);
            if (string.Equals(currentId, childId, StringComparison.OrdinalIgnoreCase)) return true;
            if (cache.TryGetValue(currentId, out var issue))
            {
                foreach (var parentRef in issue.ParentIssues)
                    if (!visited.Contains(parentRef.ParentIssue))
                        queue.Enqueue(parentRef.ParentIssue);
            }
        }
        return false;
    }

    public async Task<Issue> SetParentAsync(string projectPath, string childId, string parentId, bool addToExisting = false, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var service = GetOrCreateFleeceService(projectPath);

        try
        {
            var issue = await service.AddDependencyAsync(parentId, childId, replaceExisting: !addToExisting, cancellationToken: ct);
            cache[childId] = issue;

            _logger.LogInformation("Set parent '{ParentId}' for issue '{ChildId}' (addToExisting: {AddToExisting})", parentId, childId, addToExisting);
            return issue;
        }
        catch (Exception ex) when (ex.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Setting '{parentId}' as parent of '{childId}' would create a cycle in the issue hierarchy.", ex);
        }
    }

    public async Task<Issue> MoveSeriesSiblingAsync(string projectPath, string issueId, MoveDirection direction, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        if (!cache.TryGetValue(issueId, out var issue))
        {
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            throw new KeyNotFoundException($"Issue '{issueId}' not found");
        }

        if (issue.ParentIssues.Count == 0)
            throw new InvalidOperationException($"Issue '{issueId}' has no parent. Cannot move siblings without a parent issue.");
        if (issue.ParentIssues.Count > 1)
            throw new InvalidOperationException($"Issue '{issueId}' has multiple parents. Move sibling operation requires exactly one parent.");

        var parentId = issue.ParentIssues[0].ParentIssue;
        var service = GetOrCreateFleeceService(projectPath);
        var result = direction == MoveDirection.Up
            ? await service.MoveUpAsync(parentId, issueId, ct)
            : await service.MoveDownAsync(parentId, issueId, ct);

        if (result.Outcome == MoveOutcome.Invalid)
            throw new InvalidOperationException(result.Message ?? $"Cannot move issue '{issueId}' {direction.ToString().ToLower()}.");

        var allIssues = await service.GetAllAsync(ct);
        foreach (var refreshedIssue in allIssues.Where(i => i.ParentIssues.Any(p => p.ParentIssue == parentId)))
            cache[refreshedIssue.Id] = refreshedIssue;

        if (result.UpdatedIssue != null) cache[issueId] = result.UpdatedIssue;

        _logger.LogInformation("Moved issue '{IssueId}' {Direction} under parent '{ParentId}'", issueId, direction, parentId);
        return result.UpdatedIssue ?? issue;
    }

    #endregion

    #region Task Graph Operations

    public async Task<GraphLayout<Issue>?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default)
    {
        return await GetTaskGraphWithAdditionalIssuesAsync(projectPath, additionalIssueIds: null, ct);
    }

    public async Task<GraphLayout<Issue>?> GetTaskGraphWithAdditionalIssuesAsync(
        string projectPath, IEnumerable<string>? additionalIssueIds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var additionalIds = additionalIssueIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var includedIssues = cache.Values
            .Where(i => i.Status is IssueStatus.Draft or IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review
                        || additionalIds.Contains(i.Id))
            .ToList();

        if (includedIssues.Count == 0)
        {
            _logger.LogDebug("No issues found for task graph in project: {ProjectPath}", projectPath);
            return null;
        }

        _logger.LogDebug(
            "Building task graph with {TotalCount} issues ({OpenCount} open, {AdditionalCount} additional) for project: {ProjectPath}",
            includedIssues.Count,
            includedIssues.Count(i => i.Status is IssueStatus.Draft or IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review),
            includedIssues.Count(i => additionalIds.Contains(i.Id) && i.Status is not (IssueStatus.Draft or IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review)),
            projectPath);

        try
        {
            var layout = _issueLayoutService.LayoutForTree(includedIssues, InactiveVisibility.Hide);
            _logger.LogDebug(
                "Built layout: {Nodes}n / {Lanes}l / {Rows}r / {Edges}e for {Path}",
                layout.Nodes.Count, layout.TotalLanes, layout.TotalRows, layout.Edges.Count, projectPath);
            return layout;
        }
        catch (InvalidGraphException ex)
        {
            _logger.LogWarning(ex, "Layout rejected for {Path}: {Msg}", projectPath, ex.Message);
            return null;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fleeceServices.Clear();
        _issueCache.Clear();
        _cacheInitialized.Clear();
    }
}
