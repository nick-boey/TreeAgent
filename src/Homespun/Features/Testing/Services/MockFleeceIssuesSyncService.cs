using Homespun.Features.Fleece.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceIssuesSyncService.
/// Returns successful results without performing any actual git operations.
/// </summary>
public class MockFleeceIssuesSyncService : IFleeceIssuesSyncService
{
    private readonly ILogger<MockFleeceIssuesSyncService> _logger;

    public MockFleeceIssuesSyncService(ILogger<MockFleeceIssuesSyncService> logger)
    {
        _logger = logger;
    }

    public Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] SyncAsync for project at {ProjectPath}, branch {DefaultBranch}", projectPath, defaultBranch);
        return Task.FromResult(new FleeceIssueSyncResult(
            Success: true,
            ErrorMessage: null,
            FilesCommitted: 1,
            PushSucceeded: true));
    }

    public Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] PullChangesAsync for project at {ProjectPath}, branch {DefaultBranch}", projectPath, defaultBranch);
        return Task.FromResult(new PullResult(
            Success: true,
            HasConflicts: false,
            ErrorMessage: null));
    }

    public Task<bool> StashChangesAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] StashChangesAsync for project at {ProjectPath}", projectPath);
        return Task.FromResult(true);
    }

    public Task<bool> DiscardChangesAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] DiscardChangesAsync for project at {ProjectPath}", projectPath);
        return Task.FromResult(true);
    }
}
