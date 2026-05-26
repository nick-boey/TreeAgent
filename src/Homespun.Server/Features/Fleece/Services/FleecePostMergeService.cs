using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for post-merge processing of agent changes.
/// Assigns unassigned issues to the active user and triggers branch ID generation.
/// </summary>
public interface IFleecePostMergeService
{
    /// <summary>
    /// Processes changes after they've been merged from an agent session.
    /// </summary>
    Task PostMergeProcessAsync(
        string projectPath,
        string projectId,
        List<IssueChangeDto> changes,
        string? userEmail,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of post-merge processing service.
/// </summary>
public class FleecePostMergeService(
    IProjectFleeceService fleeceService,
    IBranchIdBackgroundService branchIdBackgroundService,
    ILogger<FleecePostMergeService> logger) : IFleecePostMergeService
{
    public async Task PostMergeProcessAsync(
        string projectPath,
        string projectId,
        List<IssueChangeDto> changes,
        string? userEmail,
        CancellationToken ct = default)
    {
        foreach (var change in changes)
        {
            if (change.ChangeType == ChangeType.Deleted)
                continue;

            try
            {
                var issue = await fleeceService.GetIssueAsync(projectPath, change.IssueId, ct);
                if (issue == null)
                {
                    logger.LogWarning("Post-merge: issue {IssueId} not found after merge", change.IssueId);
                    continue;
                }

                // Assign unassigned issues to the active user
                if (string.IsNullOrWhiteSpace(issue.AssignedTo) && !string.IsNullOrWhiteSpace(userEmail))
                {
                    // Post-merge bookkeeping — not a user undo step.
                    await fleeceService.UpdateIssueAsync(
                        projectPath, change.IssueId,
                        assignedTo: userEmail, recordUndo: false, ct: ct);

                    logger.LogInformation("Post-merge: assigned issue {IssueId} to {UserEmail}",
                        change.IssueId, userEmail);
                }

                // Trigger branch ID generation for new issues without one
                if (change.ChangeType == ChangeType.Created &&
                    string.IsNullOrWhiteSpace(issue.WorkingBranchId))
                {
                    await branchIdBackgroundService.QueueBranchIdGenerationAsync(
                        change.IssueId, projectId, issue.Title);

                    logger.LogInformation("Post-merge: queued branch ID generation for issue {IssueId}",
                        change.IssueId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during post-merge processing of issue {IssueId}", change.IssueId);
            }
        }
    }
}
