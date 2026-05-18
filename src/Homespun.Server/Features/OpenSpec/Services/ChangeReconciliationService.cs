using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Side-effectful wrapper around <see cref="IChangeScannerService"/>:
/// auto-completes Fleece issues when their linked change has archived.
/// Emits a unified <c>IssueChanged</c> event so connected clients
/// invalidate decoration caches without waiting for any background tick.
/// </summary>
public class ChangeReconciliationService(
    IChangeScannerService scanner,
    IFleeceIssueTransitionService transitionService,
    ILogger<ChangeReconciliationService> logger,
    IHubContext<NotificationHub>? notificationHub = null) : IChangeReconciliationService
{
    /// <inheritdoc />
    public async Task<BranchScanResult> ReconcileAsync(
        string projectId,
        string clonePath,
        string branchFleeceId,
        string? baseBranch = null,
        CancellationToken ct = default)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.reconcile");
        activity?.SetTag("project.id", projectId);

        var scan = await scanner.ScanBranchAsync(clonePath, branchFleeceId, baseBranch, ct);

        // Auto-transition fleece issue to complete when any linked change is archived.
        if (scan.LinkedChanges.Any(c => c.IsArchived))
        {
            var currentStatus = await transitionService.GetStatusAsync(projectId, branchFleeceId);
            if (currentStatus is not null
                && currentStatus != IssueStatus.Complete
                && currentStatus != IssueStatus.Closed)
            {
                var result = await transitionService.TransitionToCompleteAsync(projectId, branchFleeceId);
                if (!result.Success)
                {
                    logger.LogWarning(
                        "Failed to auto-complete issue {Issue} after archived change detected: {Error}",
                        branchFleeceId, result.Error);
                }
                else
                {
                    await InvalidateAndBroadcastAsync(projectId);
                }
            }
        }

        return scan;
    }

    private async Task InvalidateAndBroadcastAsync(string projectId)
    {
        if (notificationHub is not null)
        {
            await notificationHub.BroadcastIssueChanged(
                projectId, IssueChangeType.Updated, issueId: null, issue: null);
        }
    }
}
