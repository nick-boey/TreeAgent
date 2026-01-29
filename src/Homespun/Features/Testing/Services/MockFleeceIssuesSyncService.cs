using Homespun.Features.Fleece.Models;
using Homespun.Features.Fleece.Services;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceIssuesSyncService for testing and demo mode.
/// </summary>
public class MockFleeceIssuesSyncService : IFleeceIssuesSyncService
{
    public Task<bool> DiscardChangesAsync(string projectPath, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        return Task.FromResult(new PullResult(Success: true, HasConflicts: false, ErrorMessage: null));
    }

    public Task<bool> StashChangesAsync(string projectPath, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    public Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        return Task.FromResult(new FleeceIssueSyncResult(
            Success: true,
            ErrorMessage: null,
            FilesCommitted: 0,
            PushSucceeded: true));
    }
}
