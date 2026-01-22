using Fleece.Core.Models;
using Homespun.Features.Notifications;
using Homespun.Features.PullRequests.Data;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for managing status transitions for Fleece issues.
/// Coordinates between Fleece updates, agent workflow, and SignalR notifications.
/// </summary>
public class FleeceIssueTransitionService(
    IFleeceService fleeceService,
    IDataStore dataStore,
    IHubContext<NotificationHub> hubContext,
    ILogger<FleeceIssueTransitionService> logger)
    : IFleeceIssueTransitionService
{
    public async Task<FleeceTransitionResult> TransitionToInProgressAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return FleeceTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition - Fleece uses Open for active work
        if (issue.Status is IssueStatus.Complete or IssueStatus.Closed)
        {
            return FleeceTransitionResult.Fail(
                $"Cannot transition {issue.Status} issue to InProgress",
                issue.Status);
        }

        // If already Open, just ensure awaiting-pr tag is removed
        var previousStatus = issue.Status;

        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            status: IssueStatus.Progress);

        if (updated == null)
        {
            return FleeceTransitionResult.Fail("Failed to update issue status", previousStatus);
        }

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to Open (InProgress)",
            issueId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, issueId, IssueStatus.Progress);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Progress);
    }

    public async Task<FleeceTransitionResult> TransitionToAwaitingPRAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return FleeceTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition
        if (issue.Status is IssueStatus.Complete or IssueStatus.Closed)
        {
            return FleeceTransitionResult.Fail(
                $"Cannot transition {issue.Status} issue to AwaitingPR",
                issue.Status);
        }

        var previousStatus = issue.Status;

        // Keep status as Open but add awaiting-pr tag
        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            status: IssueStatus.Progress);

        if (updated == null)
        {
            return FleeceTransitionResult.Fail("Failed to update issue status", previousStatus);
        }

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to AwaitingPR (Open)",
            issueId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, issueId, IssueStatus.Progress);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Progress);
    }

    public async Task<FleeceTransitionResult> TransitionToCompleteAsync(string projectId, string issueId, int? prNumber = null)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return FleeceTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition
        if (issue.Status is IssueStatus.Complete or IssueStatus.Closed)
        {
            return FleeceTransitionResult.Fail($"Issue is already {issue.Status}", issue.Status);
        }

        var previousStatus = issue.Status;

        // Update status to Complete
        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            status: IssueStatus.Complete);

        if (updated == null)
        {
            return FleeceTransitionResult.Fail("Failed to complete issue", previousStatus);
        }

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to Complete (PR #{PrNumber})",
            issueId, previousStatus, prNumber);

        await BroadcastStatusChangeAsync(projectId, issueId, IssueStatus.Complete);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Complete, prNumber);
    }

    public async Task<FleeceTransitionResult> HandleAgentFailureAsync(string projectId, string issueId, string error)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return FleeceTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Can't revert a completed/closed issue
        if (issue.Status is IssueStatus.Complete or IssueStatus.Closed)
        {
            return FleeceTransitionResult.Fail(
                $"Cannot revert a {issue.Status} issue",
                issue.Status);
        }

        var previousStatus = issue.Status;

        // If already open, just log and return success
        if (issue.Status == IssueStatus.Progress)
        {
            logger.LogWarning(
                "Agent failure handled for issue '{IssueId}' but it was already Open. Error: {Error}",
                issueId, error);

            return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Progress);
        }

        var updated = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            status: IssueStatus.Progress);

        if (updated == null)
        {
            return FleeceTransitionResult.Fail("Failed to revert issue status", previousStatus);
        }

        logger.LogWarning(
            "Issue '{IssueId}' reverted from {PreviousStatus} to Open due to agent failure: {Error}",
            issueId, previousStatus, error);

        await BroadcastStatusChangeAsync(projectId, issueId, IssueStatus.Progress);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Progress);
    }

    public async Task<IssueStatus?> GetStatusAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return null;
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        return issue?.Status;
    }

    private async Task BroadcastStatusChangeAsync(string projectId, string issueId, IssueStatus newStatus)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(
                "FleeceIssueStatusChanged",
                projectId,
                issueId,
                newStatus.ToString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast status change for '{IssueId}'", issueId);
        }
    }
}
