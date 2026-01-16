using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Homespun.Features.Beads.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for direct SQLite access to the beads database with in-memory caching and queue-based writes.
/// </summary>
public partial class BeadsDatabaseService : IBeadsDatabaseService, IDisposable
{
    private readonly IBeadsQueueService _queueService;
    private readonly BeadsDatabaseOptions _options;
    private readonly ILogger<BeadsDatabaseService> _logger;
    private readonly ConcurrentDictionary<string, ProjectCache> _projectCaches = new();
    private bool _disposed;

    public BeadsDatabaseService(
        IBeadsQueueService queueService,
        IOptions<BeadsDatabaseOptions> options,
        ILogger<BeadsDatabaseService> logger)
    {
        _queueService = queueService;
        _options = options.Value;
        _logger = logger;
    }

    #region Read Operations

    public BeadsIssue? GetIssue(string projectPath, string issueId)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return null;
        }

        lock (cache.Lock)
        {
            return cache.Issues.TryGetValue(issueId, out var issue) ? issue : null;
        }
    }

    public IReadOnlyList<BeadsIssue> ListIssues(string projectPath, BeadsListOptions? options = null)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Array.Empty<BeadsIssue>();
        }

        lock (cache.Lock)
        {
            IEnumerable<BeadsIssue> query = cache.Issues.Values;

            if (options != null)
            {
                if (!string.IsNullOrEmpty(options.Status))
                {
                    var status = ParseStatus(options.Status);
                    query = query.Where(i => i.Status == status);
                }

                if (options.Type.HasValue)
                {
                    query = query.Where(i => i.Type == options.Type.Value);
                }

                if (options.Priority.HasValue)
                {
                    query = query.Where(i => i.Priority == options.Priority.Value);
                }

                if (!string.IsNullOrEmpty(options.Assignee))
                {
                    query = query.Where(i => i.Assignee == options.Assignee);
                }

                if (options.Labels is { Count: > 0 })
                {
                    query = query.Where(i => options.Labels.All(l => i.Labels.Contains(l)));
                }

                if (options.LabelAny is { Count: > 0 })
                {
                    query = query.Where(i => i.Labels.Any(l => options.LabelAny.Contains(l)));
                }

                if (!string.IsNullOrEmpty(options.TitleContains))
                {
                    query = query.Where(i => i.Title.Contains(options.TitleContains, StringComparison.OrdinalIgnoreCase));
                }
            }

            return query.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<BeadsIssue> GetReadyIssues(string projectPath)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Array.Empty<BeadsIssue>();
        }

        lock (cache.Lock)
        {
            // Get open issues that don't have blocking dependencies from open issues
            return cache.Issues.Values
                .Where(i => i.Status == BeadsIssueStatus.Open)
                .Where(i => !IsBlockedByOpenIssue(i.Id, cache))
                .ToList()
                .AsReadOnly();
        }
    }

    public IReadOnlyList<BeadsDependency> GetDependencies(string projectPath, string issueId)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Array.Empty<BeadsDependency>();
        }

        lock (cache.Lock)
        {
            if (cache.Dependencies.TryGetValue(issueId, out var deps))
            {
                return deps.ToList().AsReadOnly();
            }
            return Array.Empty<BeadsDependency>();
        }
    }

    public IReadOnlyList<string> GetUniqueGroups(string projectPath)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Array.Empty<string>();
        }

        lock (cache.Lock)
        {
            var groups = new HashSet<string>();

            foreach (var issue in cache.Issues.Values)
            {
                foreach (var label in issue.Labels)
                {
                    var match = HspLabelRegex().Match(label);
                    if (match.Success)
                    {
                        groups.Add(match.Groups[1].Value);
                    }
                }
            }

            return groups.OrderBy(g => g).ToList().AsReadOnly();
        }
    }

    #endregion

    #region Write Operations (Queue-Based)

    public Task<BeadsIssue> CreateIssueAsync(string projectPath, BeadsCreateOptions options)
    {
        // Generate a new issue ID (will be replaced by actual beads ID generation later)
        var issueId = $"hsp-{Guid.NewGuid().ToString()[..8]}";
        var now = DateTime.UtcNow;

        var issue = new BeadsIssue
        {
            Id = issueId,
            Title = options.Title,
            Description = options.Description,
            Status = BeadsIssueStatus.Open,
            Type = options.Type,
            Priority = options.Priority,
            ParentId = options.ParentId,
            Labels = options.Labels ?? [],
            CreatedAt = now,
            UpdatedAt = now
        };

        // Update cache immediately
        var cache = _projectCaches.GetOrAdd(projectPath, _ => new ProjectCache());
        lock (cache.Lock)
        {
            cache.Issues[issueId] = issue;
        }

        // Queue the database write
        var queueItem = BeadsQueueItem.ForCreate(projectPath, issueId, options);
        _queueService.Enqueue(queueItem);

        _logger.LogDebug("Created issue {IssueId} in cache, queued for database write", issueId);

        return Task.FromResult(issue);
    }

    public Task<bool> UpdateIssueAsync(string projectPath, string issueId, BeadsUpdateOptions options)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            // Capture previous state for undo
            var previousState = CloneIssue(issue);

            // Apply updates to cache
            if (options.Title != null) issue.Title = options.Title;
            if (options.Description != null) issue.Description = options.Description;
            if (options.Type.HasValue) issue.Type = options.Type.Value;
            if (options.Status.HasValue) issue.Status = options.Status.Value;
            if (options.Priority.HasValue) issue.Priority = options.Priority.Value;
            if (options.Assignee != null) issue.Assignee = options.Assignee;
            if (options.ParentId != null) issue.ParentId = options.ParentId;

            if (options.LabelsToAdd != null)
            {
                foreach (var label in options.LabelsToAdd)
                {
                    if (!issue.Labels.Contains(label))
                        issue.Labels.Add(label);
                }
            }

            if (options.LabelsToRemove != null)
            {
                foreach (var label in options.LabelsToRemove)
                {
                    issue.Labels.Remove(label);
                }
            }

            issue.UpdatedAt = DateTime.UtcNow;

            // Queue the database write
            var queueItem = BeadsQueueItem.ForUpdate(projectPath, issueId, options, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Updated issue {IssueId} in cache, queued for database write", issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> CloseIssueAsync(string projectPath, string issueId, string? reason = null)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            var previousState = CloneIssue(issue);
            issue.Status = BeadsIssueStatus.Closed;
            issue.ClosedAt = DateTime.UtcNow;
            issue.UpdatedAt = DateTime.UtcNow;

            var queueItem = BeadsQueueItem.ForClose(projectPath, issueId, reason, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Closed issue {IssueId} in cache, queued for database write", issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> ReopenIssueAsync(string projectPath, string issueId, string? reason = null)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            var previousState = CloneIssue(issue);
            issue.Status = BeadsIssueStatus.Open;
            issue.ClosedAt = null;
            issue.UpdatedAt = DateTime.UtcNow;

            var queueItem = BeadsQueueItem.ForReopen(projectPath, issueId, reason, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Reopened issue {IssueId} in cache, queued for database write", issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteIssueAsync(string projectPath, string issueId)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            var previousState = CloneIssue(issue);

            // Remove from cache
            cache.Issues.TryRemove(issueId, out _);
            cache.Dependencies.TryRemove(issueId, out _);

            var queueItem = BeadsQueueItem.ForDelete(projectPath, issueId, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Deleted issue {IssueId} from cache, queued for database write", issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> AddLabelAsync(string projectPath, string issueId, string label)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            var previousState = CloneIssue(issue);

            if (!issue.Labels.Contains(label))
            {
                issue.Labels.Add(label);
                issue.UpdatedAt = DateTime.UtcNow;
            }

            var queueItem = BeadsQueueItem.ForAddLabel(projectPath, issueId, label, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Added label '{Label}' to issue {IssueId} in cache", label, issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> RemoveLabelAsync(string projectPath, string issueId, string label)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (!cache.Issues.TryGetValue(issueId, out var issue))
            {
                return Task.FromResult(false);
            }

            var previousState = CloneIssue(issue);
            issue.Labels.Remove(label);
            issue.UpdatedAt = DateTime.UtcNow;

            var queueItem = BeadsQueueItem.ForRemoveLabel(projectPath, issueId, label, previousState);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Removed label '{Label}' from issue {IssueId} in cache", label, issueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> AddDependencyAsync(string projectPath, string issueId, string dependsOnIssueId, string type = "blocks")
    {
        var cache = _projectCaches.GetOrAdd(projectPath, _ => new ProjectCache());

        lock (cache.Lock)
        {
            var deps = cache.Dependencies.GetOrAdd(issueId, _ => []);

            var dependency = new BeadsDependency
            {
                FromIssueId = issueId,
                ToIssueId = dependsOnIssueId,
                Type = ParseDependencyType(type)
            };

            if (!deps.Any(d => d.ToIssueId == dependsOnIssueId))
            {
                deps.Add(dependency);
            }

            var queueItem = BeadsQueueItem.ForAddDependency(projectPath, issueId, dependsOnIssueId, type);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Added dependency {IssueId} -> {DependsOn} in cache", issueId, dependsOnIssueId);
        }

        return Task.FromResult(true);
    }

    public Task<bool> RemoveDependencyAsync(string projectPath, string issueId, string dependsOnIssueId)
    {
        if (!_projectCaches.TryGetValue(projectPath, out var cache))
        {
            return Task.FromResult(false);
        }

        lock (cache.Lock)
        {
            if (cache.Dependencies.TryGetValue(issueId, out var deps))
            {
                deps.RemoveAll(d => d.ToIssueId == dependsOnIssueId);
            }

            var queueItem = BeadsQueueItem.ForRemoveDependency(projectPath, issueId, dependsOnIssueId);
            _queueService.Enqueue(queueItem);

            _logger.LogDebug("Removed dependency {IssueId} -> {DependsOn} from cache", issueId, dependsOnIssueId);
        }

        return Task.FromResult(true);
    }

    #endregion

    #region State Management

    public async Task RefreshFromDatabaseAsync(string projectPath)
    {
        var dbPath = GetDatabasePath(projectPath);

        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database not found at {Path}", dbPath);
            return;
        }

        var cache = _projectCaches.GetOrAdd(projectPath, _ => new ProjectCache());

        _logger.LogDebug("Loading beads database from {Path}", dbPath);

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        // Load issues
        var issues = new ConcurrentDictionary<string, BeadsIssue>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, title, description, status, priority, issue_type, assignee,
                       created_at, updated_at, closed_at
                FROM issues
                WHERE status != 'tombstone' AND deleted_at IS NULL
                """;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var issue = new BeadsIssue
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Status = ParseStatus(reader.IsDBNull(3) ? "open" : reader.GetString(3)),
                    Priority = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Type = ParseType(reader.IsDBNull(5) ? "task" : reader.GetString(5)),
                    Assignee = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ParentId = null, // Not stored in beads SQLite schema
                    CreatedAt = DateTime.Parse(reader.GetString(7)),
                    UpdatedAt = DateTime.Parse(reader.GetString(8)),
                    ClosedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                    Labels = []
                };
                issues[issue.Id] = issue;
            }
        }

        // Load labels
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT issue_id, label FROM labels";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var issueId = reader.GetString(0);
                var label = reader.GetString(1);
                if (issues.TryGetValue(issueId, out var issue))
                {
                    issue.Labels.Add(label);
                }
            }
        }

        // Load dependencies
        var dependencies = new ConcurrentDictionary<string, List<BeadsDependency>>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT issue_id, depends_on_id, type FROM dependencies";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var issueId = reader.GetString(0);
                var dependsOnId = reader.GetString(1);
                var type = reader.IsDBNull(2) ? "blocks" : reader.GetString(2);

                var dep = new BeadsDependency
                {
                    FromIssueId = issueId,
                    ToIssueId = dependsOnId,
                    Type = ParseDependencyType(type)
                };

                var deps = dependencies.GetOrAdd(issueId, _ => []);
                deps.Add(dep);
            }
        }

        // Update cache atomically
        lock (cache.Lock)
        {
            cache.Issues = issues;
            cache.Dependencies = dependencies;
        }

        _logger.LogInformation("Loaded {IssueCount} issues and {DepCount} dependencies from {Path}",
            issues.Count, dependencies.Values.Sum(d => d.Count), dbPath);
    }

    public bool HasPendingChanges(string projectPath)
    {
        return _queueService.GetPendingItems(projectPath).Count > 0;
    }

    public IReadOnlyList<BeadsQueueItem> GetHistory(string projectPath, int limit = 50)
    {
        return _queueService.GetCompletedHistory(projectPath, limit);
    }

    public bool IsProjectLoaded(string projectPath)
    {
        return _projectCaches.ContainsKey(projectPath);
    }

    #endregion

    #region Helper Methods

    private static string GetDatabasePath(string projectPath)
    {
        return Path.Combine(projectPath, ".beads", "beads.db");
    }

    private bool IsBlockedByOpenIssue(string issueId, ProjectCache cache)
    {
        if (!cache.Dependencies.TryGetValue(issueId, out var deps))
        {
            return false;
        }

        foreach (var dep in deps)
        {
            if (dep.Type == BeadsDependencyType.Blocks)
            {
                if (cache.Issues.TryGetValue(dep.ToIssueId, out var blocker))
                {
                    if (blocker.Status != BeadsIssueStatus.Closed)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static BeadsIssue CloneIssue(BeadsIssue issue)
    {
        return new BeadsIssue
        {
            Id = issue.Id,
            Title = issue.Title,
            Description = issue.Description,
            Status = issue.Status,
            Type = issue.Type,
            Priority = issue.Priority,
            Assignee = issue.Assignee,
            ParentId = issue.ParentId,
            Labels = [..issue.Labels],
            CreatedAt = issue.CreatedAt,
            UpdatedAt = issue.UpdatedAt,
            ClosedAt = issue.ClosedAt
        };
    }

    private static BeadsIssueStatus ParseStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" => BeadsIssueStatus.Open,
            "in_progress" => BeadsIssueStatus.InProgress,
            "blocked" => BeadsIssueStatus.Blocked,
            "closed" => BeadsIssueStatus.Closed,
            "deferred" => BeadsIssueStatus.Deferred,
            "tombstone" => BeadsIssueStatus.Tombstone,
            "pinned" => BeadsIssueStatus.Pinned,
            _ => BeadsIssueStatus.Open
        };
    }

    private static BeadsIssueType ParseType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "feature" => BeadsIssueType.Feature,
            "bug" => BeadsIssueType.Bug,
            "task" => BeadsIssueType.Task,
            "epic" => BeadsIssueType.Epic,
            "chore" => BeadsIssueType.Chore,
            _ => BeadsIssueType.Task
        };
    }

    private static BeadsDependencyType ParseDependencyType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "blocks" => BeadsDependencyType.Blocks,
            "related" => BeadsDependencyType.Related,
            "parent_child" => BeadsDependencyType.ParentChild,
            "discovered_from" => BeadsDependencyType.DiscoveredFrom,
            _ => BeadsDependencyType.Blocks
        };
    }

    [GeneratedRegex(@"^hsp:([^/]+)/")]
    private static partial Regex HspLabelRegex();

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _projectCaches.Clear();
    }

    /// <summary>
    /// Internal cache for a single project.
    /// </summary>
    private class ProjectCache
    {
        public object Lock { get; } = new();
        public ConcurrentDictionary<string, BeadsIssue> Issues { get; set; } = new();
        public ConcurrentDictionary<string, List<BeadsDependency>> Dependencies { get; set; } = new();
    }
}
