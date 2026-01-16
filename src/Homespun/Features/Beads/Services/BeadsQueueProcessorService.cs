using System.Collections.Concurrent;
using Homespun.Features.Beads.Data;
using Homespun.Features.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Background service that processes queued beads database modifications.
/// Listens for debounce completion events and applies changes to SQLite.
/// </summary>
public class BeadsQueueProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBeadsQueueService _queueService;
    private readonly BeadsDatabaseOptions _options;
    private readonly ILogger<BeadsQueueProcessorService> _logger;
    private readonly ConcurrentQueue<string> _projectsToProcess = new();
    private readonly SemaphoreSlim _processingSignal = new(0);

    public BeadsQueueProcessorService(
        IServiceScopeFactory scopeFactory,
        IBeadsQueueService queueService,
        IOptions<BeadsDatabaseOptions> options,
        ILogger<BeadsQueueProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _queueService = queueService;
        _options = options.Value;
        _logger = logger;

        // Subscribe to debounce completion events
        _queueService.DebounceCompleted += OnDebounceCompleted;
    }

    private void OnDebounceCompleted(string projectPath)
    {
        _projectsToProcess.Enqueue(projectPath);
        _processingSignal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Beads queue processor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for work or cancellation
                await _processingSignal.WaitAsync(stoppingToken);

                // Process all queued projects
                while (_projectsToProcess.TryDequeue(out var projectPath))
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await ProcessProjectAsync(projectPath, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing queue for project {ProjectPath}", projectPath);
                        _queueService.MarkAsProcessingComplete(projectPath, success: false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in queue processor");
            }
        }

        _logger.LogInformation("Beads queue processor service stopped");
    }

    private async Task ProcessProjectAsync(string projectPath, CancellationToken ct)
    {
        var pendingItems = _queueService.GetPendingItems(projectPath);
        if (pendingItems.Count == 0)
        {
            _logger.LogDebug("No pending items for project {ProjectPath}, skipping", projectPath);
            return;
        }

        _logger.LogInformation("Processing {Count} queued items for project {ProjectPath}",
            pendingItems.Count, projectPath);

        _queueService.MarkAsProcessing(projectPath);

        using var scope = _scopeFactory.CreateScope();
        var commandRunner = scope.ServiceProvider.GetRequiredService<ICommandRunner>();
        var databaseService = scope.ServiceProvider.GetRequiredService<IBeadsDatabaseService>();

        bool success = true;
        int processedCount = 0;

        try
        {
            // 1. Run bd sync before processing (pull remote changes)
            if (_options.SyncBeforeApply)
            {
                var preSyncResult = await RunBdSyncAsync(projectPath, commandRunner, ct);
                if (!preSyncResult)
                {
                    _logger.LogWarning("Pre-sync failed for {ProjectPath}, continuing with local changes", projectPath);
                    // Continue anyway - we'll sync after
                }
            }

            // 2. Apply queue items to SQLite database
            var dbPath = GetDatabasePath(projectPath);
            if (File.Exists(dbPath))
            {
                processedCount = await ApplyQueueItemsAsync(projectPath, pendingItems, dbPath, ct);
            }
            else
            {
                _logger.LogWarning("Database not found at {Path}, skipping write operations", dbPath);
            }

            // 3. Run bd sync after processing (push local changes)
            if (_options.SyncAfterApply)
            {
                var postSyncResult = await RunBdSyncAsync(projectPath, commandRunner, ct);
                if (!postSyncResult)
                {
                    _logger.LogWarning("Post-sync failed for {ProjectPath}, changes are local only", projectPath);
                    success = false;
                }
            }

            // 4. Refresh in-memory cache from database
            await databaseService.RefreshFromDatabaseAsync(projectPath);

            // 5. Move processed items to history
            foreach (var item in pendingItems)
            {
                item.Status = BeadsQueueItemStatus.Completed;
                item.ProcessedAt = DateTime.UtcNow;
                _queueService.AddToHistory(item);
            }

            // 6. Clear pending items
            _queueService.ClearPendingItems(projectPath);

            _logger.LogInformation("Successfully processed {Count} items for project {ProjectPath}",
                processedCount, projectPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queue for project {ProjectPath}", projectPath);
            success = false;

            // Mark items as failed but keep in queue for retry
            foreach (var item in pendingItems)
            {
                item.Status = BeadsQueueItemStatus.Failed;
                item.Error = ex.Message;
            }
        }

        _queueService.MarkAsProcessingComplete(projectPath, success);
    }

    private async Task<bool> RunBdSyncAsync(string projectPath, ICommandRunner commandRunner, CancellationToken ct)
    {
        _logger.LogDebug("Running bd sync for {ProjectPath}", projectPath);

        try
        {
            var result = await commandRunner.RunAsync("bd", "sync", projectPath);

            if (result.ExitCode != 0)
            {
                _logger.LogWarning("bd sync failed with exit code {ExitCode}: {Error}",
                    result.ExitCode, result.Error);
                return false;
            }

            _logger.LogDebug("bd sync completed successfully for {ProjectPath}", projectPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running bd sync for {ProjectPath}", projectPath);
            return false;
        }
    }

    private async Task<int> ApplyQueueItemsAsync(
        string projectPath,
        IReadOnlyList<BeadsQueueItem> items,
        string dbPath,
        CancellationToken ct)
    {
        var connectionString = $"Data Source={dbPath}";
        int processedCount = 0;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        // Set busy timeout for handling concurrent access
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA busy_timeout = {_options.BusyTimeoutMs};";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await ApplyQueueItemAsync(connection, item, ct);
                processedCount++;
                _logger.LogDebug("Applied {Operation} for issue {IssueId}", item.Operation, item.IssueId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply {Operation} for issue {IssueId}",
                    item.Operation, item.IssueId);
                item.Status = BeadsQueueItemStatus.Failed;
                item.Error = ex.Message;
            }
        }

        return processedCount;
    }

    private async Task ApplyQueueItemAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        switch (item.Operation)
        {
            case BeadsOperationType.Create:
                await ApplyCreateAsync(connection, item, ct);
                break;

            case BeadsOperationType.Update:
                await ApplyUpdateAsync(connection, item, ct);
                break;

            case BeadsOperationType.Close:
                await ApplyCloseAsync(connection, item, ct);
                break;

            case BeadsOperationType.Reopen:
                await ApplyReopenAsync(connection, item, ct);
                break;

            case BeadsOperationType.Delete:
                await ApplyDeleteAsync(connection, item, ct);
                break;

            case BeadsOperationType.AddLabel:
                await ApplyAddLabelAsync(connection, item, ct);
                break;

            case BeadsOperationType.RemoveLabel:
                await ApplyRemoveLabelAsync(connection, item, ct);
                break;

            case BeadsOperationType.AddDependency:
                await ApplyAddDependencyAsync(connection, item, ct);
                break;

            case BeadsOperationType.RemoveDependency:
                await ApplyRemoveDependencyAsync(connection, item, ct);
                break;

            default:
                _logger.LogWarning("Unknown operation type: {Operation}", item.Operation);
                break;
        }
    }

    private async Task ApplyCreateAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        var options = item.CreateOptions!;
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = """
            INSERT INTO issues (id, title, description, status, priority, issue_type, created_at, updated_at)
            VALUES ($id, $title, $description, 'open', $priority, $issueType, $createdAt, $updatedAt)
            """;

        cmd.Parameters.AddWithValue("$id", item.IssueId);
        cmd.Parameters.AddWithValue("$title", options.Title);
        cmd.Parameters.AddWithValue("$description", options.Description ?? "");
        cmd.Parameters.AddWithValue("$priority", options.Priority ?? 2);
        cmd.Parameters.AddWithValue("$issueType", options.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);

        // Add labels if any
        if (options.Labels != null)
        {
            foreach (var label in options.Labels)
            {
                await InsertLabelAsync(connection, item.IssueId, label, ct);
            }
        }

        // Add event for audit trail
        await InsertEventAsync(connection, item.IssueId, "created", null, options.Title, ct);
    }

    private async Task ApplyUpdateAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        var options = item.UpdateOptions!;
        var setClauses = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (options.Title != null)
        {
            setClauses.Add("title = $title");
            parameters.Add(("$title", options.Title));
        }

        if (options.Description != null)
        {
            setClauses.Add("description = $description");
            parameters.Add(("$description", options.Description));
        }

        if (options.Status.HasValue)
        {
            setClauses.Add("status = $status");
            parameters.Add(("$status", MapStatus(options.Status.Value)));
        }

        if (options.Type.HasValue)
        {
            setClauses.Add("issue_type = $issueType");
            parameters.Add(("$issueType", options.Type.Value.ToString().ToLowerInvariant()));
        }

        if (options.Priority.HasValue)
        {
            setClauses.Add("priority = $priority");
            parameters.Add(("$priority", options.Priority.Value));
        }

        if (options.Assignee != null)
        {
            setClauses.Add("assignee = $assignee");
            parameters.Add(("$assignee", options.Assignee));
        }

        // Note: parent_id is not stored in beads SQLite schema - skip if provided

        if (setClauses.Count > 0)
        {
            setClauses.Add("updated_at = $updatedAt");
            parameters.Add(("$updatedAt", DateTime.UtcNow.ToString("O")));

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"UPDATE issues SET {string.Join(", ", setClauses)} WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", item.IssueId);

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Handle label changes
        if (options.LabelsToAdd != null)
        {
            foreach (var label in options.LabelsToAdd)
            {
                await InsertLabelAsync(connection, item.IssueId, label, ct);
            }
        }

        if (options.LabelsToRemove != null)
        {
            foreach (var label in options.LabelsToRemove)
            {
                await DeleteLabelAsync(connection, item.IssueId, label, ct);
            }
        }
    }

    private async Task ApplyCloseAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE issues
            SET status = 'closed', closed_at = $closedAt, close_reason = $reason, updated_at = $updatedAt
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", item.IssueId);
        cmd.Parameters.AddWithValue("$closedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", item.Reason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        await InsertEventAsync(connection, item.IssueId, "status_changed", "open", "closed", ct);
    }

    private async Task ApplyReopenAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE issues
            SET status = 'open', closed_at = NULL, close_reason = NULL, updated_at = $updatedAt
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", item.IssueId);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        await InsertEventAsync(connection, item.IssueId, "status_changed", "closed", "open", ct);
    }

    private async Task ApplyDeleteAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE issues
            SET status = 'tombstone', deleted_at = $deletedAt, updated_at = $updatedAt
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", item.IssueId);
        cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplyAddLabelAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await InsertLabelAsync(connection, item.IssueId, item.Label!, ct);
        await InsertEventAsync(connection, item.IssueId, "label_added", null, item.Label, ct);
    }

    private async Task ApplyRemoveLabelAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await DeleteLabelAsync(connection, item.IssueId, item.Label!, ct);
        await InsertEventAsync(connection, item.IssueId, "label_removed", item.Label, null, ct);
    }

    private async Task ApplyAddDependencyAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO dependencies (issue_id, depends_on_id, type, created_at)
            VALUES ($issueId, $dependsOnId, $type, $createdAt)
            """;
        cmd.Parameters.AddWithValue("$issueId", item.IssueId);
        cmd.Parameters.AddWithValue("$dependsOnId", item.DependsOnIssueId!);
        cmd.Parameters.AddWithValue("$type", item.DependencyType ?? "blocks");
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplyRemoveDependencyAsync(SqliteConnection connection, BeadsQueueItem item, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM dependencies WHERE issue_id = $issueId AND depends_on_id = $dependsOnId";
        cmd.Parameters.AddWithValue("$issueId", item.IssueId);
        cmd.Parameters.AddWithValue("$dependsOnId", item.DependsOnIssueId!);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertLabelAsync(SqliteConnection connection, string issueId, string label, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO labels (issue_id, label) VALUES ($issueId, $label)";
        cmd.Parameters.AddWithValue("$issueId", issueId);
        cmd.Parameters.AddWithValue("$label", label);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteLabelAsync(SqliteConnection connection, string issueId, string label, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM labels WHERE issue_id = $issueId AND label = $label";
        cmd.Parameters.AddWithValue("$issueId", issueId);
        cmd.Parameters.AddWithValue("$label", label);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        string issueId,
        string eventType,
        string? oldValue,
        string? newValue,
        CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO events (issue_id, event_type, old_value, new_value, actor, created_at)
            VALUES ($issueId, $eventType, $oldValue, $newValue, $actor, $createdAt)
            """;
        cmd.Parameters.AddWithValue("$issueId", issueId);
        cmd.Parameters.AddWithValue("$eventType", eventType);
        cmd.Parameters.AddWithValue("$oldValue", oldValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$newValue", newValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$actor", "homespun");
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string MapStatus(BeadsIssueStatus status)
    {
        return status switch
        {
            BeadsIssueStatus.Open => "open",
            BeadsIssueStatus.InProgress => "in_progress",
            BeadsIssueStatus.Blocked => "blocked",
            BeadsIssueStatus.Closed => "closed",
            BeadsIssueStatus.Deferred => "deferred",
            BeadsIssueStatus.Tombstone => "tombstone",
            BeadsIssueStatus.Pinned => "pinned",
            _ => "open"
        };
    }

    private static string GetDatabasePath(string projectPath)
    {
        return Path.Combine(projectPath, ".beads", "beads.db");
    }

    public override void Dispose()
    {
        _queueService.DebounceCompleted -= OnDebounceCompleted;
        _processingSignal.Dispose();
        base.Dispose();
    }
}
