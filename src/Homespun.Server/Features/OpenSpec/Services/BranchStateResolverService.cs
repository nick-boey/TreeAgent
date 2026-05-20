using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Cache-first resolver that falls back to a live on-disk scan via
/// <see cref="IChangeReconciliationService"/> when no fresh snapshot exists.
/// </summary>
public class BranchStateResolverService(
    IBranchStateCacheService cache,
    IChangeReconciliationService reconciliation,
    IDataStore dataStore,
    IGitCloneService cloneService,
    TimeProvider timeProvider,
    ILogger<BranchStateResolverService> logger) : IBranchStateResolverService
{
    /// <inheritdoc />
    public async Task<BranchStateSnapshot?> GetOrScanAsync(
        string projectId,
        string branch,
        CancellationToken ct = default)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.state.resolve");
        activity?.SetTag("project.id", projectId);

        var cached = cache.TryGet(projectId, branch);
        if (cached is not null)
        {
            activity?.SetTag("cache.hit", true);
            return cached;
        }

        activity?.SetTag("cache.hit", false);

        var fleeceId = BranchNameParser.ExtractIssueId(branch);
        if (string.IsNullOrEmpty(fleeceId))
        {
            logger.LogDebug(
                "Branch {Branch} has no fleece-id suffix; skipping OpenSpec scan", branch);
            return null;
        }

        var project = dataStore.GetProject(projectId);
        if (project is null || string.IsNullOrEmpty(project.LocalPath))
        {
            logger.LogDebug(
                "Project {ProjectId} missing or has no local path; skipping OpenSpec scan", projectId);
            return null;
        }

        var clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath, branch);
        if (string.IsNullOrEmpty(clonePath) || !Directory.Exists(clonePath))
        {
            logger.LogDebug(
                "Clone for branch {Branch} not present on disk; skipping OpenSpec scan", branch);
            return null;
        }

        var scan = await reconciliation.ReconcileAsync(projectId, clonePath, fleeceId, baseBranch: null, ct);
        var snapshot = ToSnapshot(projectId, branch, fleeceId, scan, timeProvider.GetUtcNow());

        cache.Put(snapshot);
        return snapshot;
    }

    internal static BranchStateSnapshot ToSnapshot(
        string projectId,
        string branch,
        string fleeceId,
        BranchScanResult scan,
        DateTimeOffset capturedAt)
    {
        return new BranchStateSnapshot
        {
            ProjectId = projectId,
            Branch = branch,
            FleeceId = fleeceId,
            CapturedAt = capturedAt,
            Changes = scan.LinkedChanges.Select(c => new SnapshotChange
            {
                Name = c.Name,
                CreatedBy = c.CreatedBy,
                IsArchived = c.IsArchived,
                ArchivedFolderName = c.ArchivedFolderName,
                ArtifactState = c.ArtifactState,
                TasksDone = c.TaskState.TasksDone,
                TasksTotal = c.TaskState.TasksTotal,
                NextIncomplete = c.TaskState.NextIncomplete,
                Phases = c.TaskState.Phases
            }).ToList()
        };
    }
}
