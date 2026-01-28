using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceIssueTransitionService.
/// </summary>
public class MockFleeceIssueTransitionService : IFleeceIssueTransitionService
{
    private readonly MockFleeceService _fleeceService;
    private readonly ILogger<MockFleeceIssueTransitionService> _logger;

    public MockFleeceIssueTransitionService(
        MockFleeceService fleeceService,
        ILogger<MockFleeceIssueTransitionService> logger)
    {
        _fleeceService = fleeceService;
        _logger = logger;
    }

    public async Task<FleeceTransitionResult> TransitionToInProgressAsync(string projectId, string issueId)
    {
        _logger.LogDebug("[Mock] TransitionToInProgress {IssueId} in project {ProjectId}", issueId, projectId);

        var issue = await _fleeceService.GetIssueAsync(projectId, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue {issueId} not found");
        }

        var previousStatus = issue.Status;
        await _fleeceService.UpdateIssueAsync(projectId, issueId, status: IssueStatus.Progress);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Progress);
    }

    public async Task<FleeceTransitionResult> TransitionToAwaitingPRAsync(string projectId, string issueId)
    {
        _logger.LogDebug("[Mock] TransitionToAwaitingPR {IssueId} in project {ProjectId}", issueId, projectId);

        var issue = await _fleeceService.GetIssueAsync(projectId, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue {issueId} not found");
        }

        var previousStatus = issue.Status;
        // Transition to Review status (awaiting PR review)
        await _fleeceService.UpdateIssueAsync(projectId, issueId, status: IssueStatus.Review);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Review);
    }

    public async Task<FleeceTransitionResult> TransitionToCompleteAsync(
        string projectId,
        string issueId,
        int? prNumber = null)
    {
        _logger.LogDebug("[Mock] TransitionToComplete {IssueId} in project {ProjectId}, PR: {PrNumber}",
            issueId, projectId, prNumber);

        var issue = await _fleeceService.GetIssueAsync(projectId, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue {issueId} not found");
        }

        var previousStatus = issue.Status;
        await _fleeceService.UpdateIssueAsync(projectId, issueId, status: IssueStatus.Complete);

        return FleeceTransitionResult.Ok(previousStatus, IssueStatus.Complete, prNumber);
    }

    public async Task<FleeceTransitionResult> HandleAgentFailureAsync(
        string projectId,
        string issueId,
        string error)
    {
        _logger.LogDebug("[Mock] HandleAgentFailure {IssueId} in project {ProjectId}: {Error}",
            issueId, projectId, error);

        var issue = await _fleeceService.GetIssueAsync(projectId, issueId);
        if (issue == null)
        {
            return FleeceTransitionResult.Fail($"Issue {issueId} not found");
        }

        // Keep the issue in its current status on failure
        return FleeceTransitionResult.Fail(error, issue.Status);
    }

    public async Task<IssueStatus?> GetStatusAsync(string projectId, string issueId)
    {
        _logger.LogDebug("[Mock] GetStatus {IssueId} in project {ProjectId}", issueId, projectId);

        var issue = await _fleeceService.GetIssueAsync(projectId, issueId);
        return issue?.Status;
    }
}
