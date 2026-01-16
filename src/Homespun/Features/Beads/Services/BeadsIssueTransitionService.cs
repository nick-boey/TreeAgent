using Homespun.Features.Agents.Hubs;
using Homespun.Features.Beads.Data;
using Homespun.Features.PullRequests.Data;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for managing status transitions for beads issues.
/// Coordinates between beads updates, agent workflow, and SignalR notifications.
/// Uses direct SQLite access via IBeadsDatabaseService for performance.
/// </summary>
public class BeadsIssueTransitionService(
    IBeadsDatabaseService beadsDatabaseService,
    IDataStore dataStore,
    IHubContext<AgentHub> hubContext,
    ILogger<BeadsIssueTransitionService> logger)
    : IBeadsIssueTransitionService
{
    public async Task<BeadsTransitionResult> TransitionToInProgressAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return BeadsTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = beadsDatabaseService.GetIssue(project.LocalPath, issueId);
        if (issue == null)
        {
            return BeadsTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition
        if (issue.Status == BeadsIssueStatus.InProgress)
        {
            return BeadsTransitionResult.Fail("Issue is already InProgress", issue.Status);
        }

        if (issue.Status == BeadsIssueStatus.Closed)
        {
            return BeadsTransitionResult.Fail("Cannot transition Closed issue to InProgress", issue.Status);
        }

        var previousStatus = issue.Status;
        var success = await beadsDatabaseService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            new BeadsUpdateOptions { Status = BeadsIssueStatus.InProgress });

        if (!success)
        {
            return BeadsTransitionResult.Fail("Failed to update issue status", previousStatus);
        }

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to InProgress",
            issueId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, issueId, BeadsIssueStatus.InProgress);

        return BeadsTransitionResult.Ok(previousStatus, BeadsIssueStatus.InProgress);
    }
    
    public async Task<BeadsTransitionResult> TransitionToAwaitingPRAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return BeadsTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = beadsDatabaseService.GetIssue(project.LocalPath, issueId);
        if (issue == null)
        {
            return BeadsTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition - for beads we use "blocked" status to indicate awaiting PR
        // since beads doesn't have an "awaiting_pr" status
        if (issue.Status == BeadsIssueStatus.Closed)
        {
            return BeadsTransitionResult.Fail("Cannot transition Closed issue to AwaitingPR", issue.Status);
        }

        var previousStatus = issue.Status;

        // Use "blocked" status and add a label to indicate awaiting PR
        var success = await beadsDatabaseService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            new BeadsUpdateOptions { Status = BeadsIssueStatus.Blocked });

        if (!success)
        {
            return BeadsTransitionResult.Fail("Failed to update issue status", previousStatus);
        }

        // Add label to indicate awaiting PR
        await beadsDatabaseService.AddLabelAsync(project.LocalPath, issueId, "awaiting-pr");

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to AwaitingPR (using Blocked status)",
            issueId, previousStatus);

        await BroadcastStatusChangeAsync(projectId, issueId, BeadsIssueStatus.Blocked);

        return BeadsTransitionResult.Ok(previousStatus, BeadsIssueStatus.Blocked);
    }
    
    public async Task<BeadsTransitionResult> TransitionToCompleteAsync(string projectId, string issueId, int? prNumber = null)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return BeadsTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = beadsDatabaseService.GetIssue(project.LocalPath, issueId);
        if (issue == null)
        {
            return BeadsTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Validate transition
        if (issue.Status == BeadsIssueStatus.Closed)
        {
            return BeadsTransitionResult.Fail("Issue is already Closed", issue.Status);
        }

        var previousStatus = issue.Status;

        // Add PR label if we have a PR number
        if (prNumber.HasValue)
        {
            await beadsDatabaseService.AddLabelAsync(project.LocalPath, issueId, $"pr:{prNumber}");
        }

        // Close the issue
        var reason = prNumber.HasValue
            ? $"PR #{prNumber} created"
            : "Completed";

        var success = await beadsDatabaseService.CloseIssueAsync(project.LocalPath, issueId, reason);

        if (!success)
        {
            return BeadsTransitionResult.Fail("Failed to close issue", previousStatus);
        }

        // Remove awaiting-pr label if present
        await beadsDatabaseService.RemoveLabelAsync(project.LocalPath, issueId, "awaiting-pr");

        logger.LogInformation(
            "Issue '{IssueId}' transitioned from {PreviousStatus} to Closed (PR #{PrNumber})",
            issueId, previousStatus, prNumber);

        await BroadcastStatusChangeAsync(projectId, issueId, BeadsIssueStatus.Closed);

        return BeadsTransitionResult.Ok(previousStatus, BeadsIssueStatus.Closed, prNumber);
    }
    
    public async Task<BeadsTransitionResult> HandleAgentFailureAsync(string projectId, string issueId, string error)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return BeadsTransitionResult.Fail($"Project '{projectId}' not found");
        }

        var issue = beadsDatabaseService.GetIssue(project.LocalPath, issueId);
        if (issue == null)
        {
            return BeadsTransitionResult.Fail($"Issue '{issueId}' not found");
        }

        // Can't revert a closed issue
        if (issue.Status == BeadsIssueStatus.Closed)
        {
            return BeadsTransitionResult.Fail("Cannot revert a closed issue", issue.Status);
        }

        var previousStatus = issue.Status;

        // If already open, nothing to do
        if (issue.Status == BeadsIssueStatus.Open)
        {
            logger.LogWarning(
                "Agent failure handled for issue '{IssueId}' but it was already Open. Error: {Error}",
                issueId, error);
            return BeadsTransitionResult.Ok(previousStatus, BeadsIssueStatus.Open);
        }

        var success = await beadsDatabaseService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            new BeadsUpdateOptions { Status = BeadsIssueStatus.Open });

        if (!success)
        {
            return BeadsTransitionResult.Fail("Failed to revert issue status", previousStatus);
        }

        // Remove awaiting-pr label if present
        await beadsDatabaseService.RemoveLabelAsync(project.LocalPath, issueId, "awaiting-pr");

        logger.LogWarning(
            "Issue '{IssueId}' reverted from {PreviousStatus} to Open due to agent failure: {Error}",
            issueId, previousStatus, error);

        await BroadcastStatusChangeAsync(projectId, issueId, BeadsIssueStatus.Open);

        return BeadsTransitionResult.Ok(previousStatus, BeadsIssueStatus.Open);
    }

    public Task<BeadsIssueStatus?> GetStatusAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return Task.FromResult<BeadsIssueStatus?>(null);
        }

        var issue = beadsDatabaseService.GetIssue(project.LocalPath, issueId);
        return Task.FromResult(issue?.Status);
    }
    
    private async Task BroadcastStatusChangeAsync(string projectId, string issueId, BeadsIssueStatus newStatus)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(
                "BeadsIssueStatusChanged",
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
