using System.Collections.Concurrent;
using Homespun.Features.Projects;
using Homespun.Features.Fleece.Services;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Background service for handling asynchronous branch ID generation.
/// </summary>
public class BranchIdBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<BranchIdBackgroundService> logger)
    : IBranchIdBackgroundService
{
    private readonly ConcurrentDictionary<string, DateTime> _pendingGenerations = new();
    private readonly TimeSpan _generationTimeout = TimeSpan.FromSeconds(10);

    public Task QueueBranchIdGenerationAsync(string issueId, string projectId, string title)
    {
        // Check if already generating for this issue
        if (_pendingGenerations.ContainsKey(issueId))
        {
            logger.LogDebug("Branch ID generation already pending for issue {IssueId}", issueId);
            return Task.CompletedTask;
        }

        // Mark as pending
        _pendingGenerations[issueId] = DateTime.UtcNow;

        // Fire and forget with proper error handling
        _ = Task.Run(async () =>
        {
            try
            {
                await GenerateBranchIdAsync(issueId, projectId, title);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in branch ID generation for issue {IssueId}", issueId);
            }
            finally
            {
                _pendingGenerations.TryRemove(issueId, out _);
            }
        });

        return Task.CompletedTask;
    }

    private async Task GenerateBranchIdAsync(string issueId, string projectId, string title)
    {
        using var scope = serviceProvider.CreateScope();
        var branchIdGenerator = scope.ServiceProvider.GetRequiredService<IBranchIdGeneratorService>();
        var fleeceService = scope.ServiceProvider.GetRequiredService<IProjectFleeceService>();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var hubContext =
            scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

        try
        {
            // Set timeout for generation
            using var cts = new CancellationTokenSource(_generationTimeout);

            logger.LogInformation("Starting branch ID generation for issue {IssueId} with title: {Title}", issueId,
                title);

            // Generate branch ID
            var result = await branchIdGenerator.GenerateAsync(title, cts.Token);

            if (!result.Success)
            {
                logger.LogWarning("Branch ID generation failed for issue {IssueId}: {Error}", issueId, result.Error);
                await hubContext.Clients.All.SendAsync("BranchIdGenerationFailed", issueId, projectId,
                    result.Error ?? "Generation failed");
                return;
            }

            // Check if issue still exists and doesn't have a branch ID
            var project = await projectService.GetByIdAsync(projectId);
            if (project == null)
            {
                logger.LogWarning("Project {ProjectId} not found during branch ID generation", projectId);
                return;
            }

            var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
            if (issue == null)
            {
                logger.LogWarning("Issue {IssueId} not found during branch ID generation", issueId);
                return;
            }

            // Only update if branch ID is still empty
            if (!string.IsNullOrWhiteSpace(issue.WorkingBranchId))
            {
                logger.LogInformation("Issue {IssueId} already has branch ID, skipping auto-generation", issueId);
                return;
            }

            // Update issue with generated branch ID — background bookkeeping, not a user undo step.
            var updatedIssue = await fleeceService.UpdateIssueAsync(
                project.LocalPath,
                issueId,
                workingBranchId: result.BranchId,
                recordUndo: false,
                ct: CancellationToken.None);

            if (updatedIssue != null)
            {
                logger.LogInformation(
                    "Successfully generated branch ID '{BranchId}' for issue {IssueId} (AI: {WasAiGenerated})",
                    result.BranchId, issueId, result.WasAiGenerated);

                await hubContext.Clients.All.SendAsync("BranchIdGenerated", issueId, projectId, result.BranchId!,
                    result.WasAiGenerated);
            }
            else
            {
                logger.LogWarning("Failed to update issue {IssueId} with generated branch ID", issueId);
                await hubContext.Clients.All.SendAsync("BranchIdGenerationFailed", issueId, projectId,
                    "Failed to update issue");
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Branch ID generation timed out for issue {IssueId}", issueId);
            await hubContext.Clients.All.SendAsync("BranchIdGenerationFailed", issueId, projectId,
                "Generation timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating branch ID for issue {IssueId}", issueId);
            await hubContext.Clients.All.SendAsync("BranchIdGenerationFailed", issueId, projectId,
                "Internal error during generation");
        }
    }
}
