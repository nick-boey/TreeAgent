using Homespun.Features.OpenCode.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Service for managing status transitions for future changes in the roadmap.
/// Coordinates between roadmap updates, agent workflow, and SignalR notifications.
/// </summary>
public class FutureChangeTransitionService(
    IRoadmapService roadmapService,
    IHubContext<AgentHub> hubContext,
    ILogger<FutureChangeTransitionService> logger)
    : IFutureChangeTransitionService
{
    public async Task<TransitionResult> TransitionToInProgressAsync(string projectId, string changeId)
    {
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return TransitionResult.Fail($"Change '{changeId}' not found in project '{projectId}'");
        }

        // Validate transition
        if (change.Status == FutureChangeStatus.InProgress)
        {
            return TransitionResult.Fail($"Change is already InProgress", change.Status);
        }

        if (change.Status == FutureChangeStatus.Complete)
        {
            return TransitionResult.Fail($"Cannot transition Complete change to InProgress", change.Status);
        }

        if (change.Status == FutureChangeStatus.AwaitingPR)
        {
            return TransitionResult.Fail($"Cannot transition AwaitingPR change to InProgress", change.Status);
        }

        var previousStatus = change.Status;
        var success = await roadmapService.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.InProgress);
        
        if (!success)
        {
            return TransitionResult.Fail("Failed to update change status", previousStatus);
        }

        logger.LogInformation(
            "Change '{ChangeId}' transitioned from {PreviousStatus} to InProgress",
            changeId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, changeId, FutureChangeStatus.InProgress);

        return TransitionResult.Ok(previousStatus, FutureChangeStatus.InProgress);
    }

    public async Task<TransitionResult> TransitionToAwaitingPRAsync(string projectId, string changeId)
    {
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return TransitionResult.Fail($"Change '{changeId}' not found in project '{projectId}'");
        }

        // Validate transition - allow from Pending or InProgress
        if (change.Status == FutureChangeStatus.Complete)
        {
            return TransitionResult.Fail($"Cannot transition Complete change to AwaitingPR", change.Status);
        }

        if (change.Status == FutureChangeStatus.AwaitingPR)
        {
            return TransitionResult.Fail($"Change is already AwaitingPR", change.Status);
        }

        var previousStatus = change.Status;
        var success = await roadmapService.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.AwaitingPR);
        
        if (!success)
        {
            return TransitionResult.Fail("Failed to update change status", previousStatus);
        }

        logger.LogInformation(
            "Change '{ChangeId}' transitioned from {PreviousStatus} to AwaitingPR (awaiting PR creation)",
            changeId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, changeId, FutureChangeStatus.AwaitingPR);

        return TransitionResult.Ok(previousStatus, FutureChangeStatus.AwaitingPR);
    }

    public async Task<TransitionResult> TransitionToCompleteAsync(string projectId, string changeId)
    {
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return TransitionResult.Fail($"Change '{changeId}' not found in project '{projectId}'");
        }

        // Validate transition - can complete from InProgress or AwaitingPR
        if (change.Status == FutureChangeStatus.Complete)
        {
            return TransitionResult.Fail($"Change is already Complete", change.Status);
        }

        if (change.Status == FutureChangeStatus.Pending)
        {
            return TransitionResult.Fail($"Cannot complete a Pending change without going through InProgress/AwaitingPR", change.Status);
        }

        var previousStatus = change.Status;
        var success = await roadmapService.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Complete);
        
        if (!success)
        {
            return TransitionResult.Fail("Failed to update change status", previousStatus);
        }

        // Remove this change from other changes' parent lists
        await roadmapService.RemoveParentReferenceAsync(projectId, changeId);

        logger.LogInformation(
            "Change '{ChangeId}' transitioned from {PreviousStatus} to Complete",
            changeId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, changeId, FutureChangeStatus.Complete);

        return TransitionResult.Ok(previousStatus, FutureChangeStatus.Complete);
    }

    public async Task<TransitionResult> HandleAgentFailureAsync(string projectId, string changeId, string error)
    {
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return TransitionResult.Fail($"Change '{changeId}' not found in project '{projectId}'");
        }

        // Can't revert a completed change
        if (change.Status == FutureChangeStatus.Complete)
        {
            return TransitionResult.Fail($"Cannot revert a complete change", change.Status);
        }

        var previousStatus = change.Status;

        // If already pending, nothing to do
        if (change.Status == FutureChangeStatus.Pending)
        {
            logger.LogWarning(
                "Agent failure handled for change '{ChangeId}' but it was already Pending. Error: {Error}",
                changeId, error);
            return TransitionResult.Ok(previousStatus, FutureChangeStatus.Pending);
        }

        var success = await roadmapService.UpdateChangeStatusAsync(projectId, changeId, FutureChangeStatus.Pending);
        
        if (!success)
        {
            return TransitionResult.Fail("Failed to revert change status", previousStatus);
        }

        logger.LogWarning(
            "Change '{ChangeId}' reverted from {PreviousStatus} to Pending due to agent failure: {Error}",
            changeId, previousStatus, error);

        await BroadcastStatusChangeAsync(projectId, changeId, FutureChangeStatus.Pending);

        return TransitionResult.Ok(previousStatus, FutureChangeStatus.Pending);
    }

    public async Task<FutureChangeStatus?> GetStatusAsync(string projectId, string changeId)
    {
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        return change?.Status;
    }

    private async Task BroadcastStatusChangeAsync(string projectId, string changeId, FutureChangeStatus newStatus)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(
                "FutureChangeStatusChanged",
                projectId,
                changeId,
                newStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast status change for '{ChangeId}'", changeId);
        }
    }
}
