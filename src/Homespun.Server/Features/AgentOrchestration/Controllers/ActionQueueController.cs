using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using ActionQueueHistoryEntry = Homespun.Shared.Requests.ActionQueueHistoryEntry;

namespace Homespun.Features.AgentOrchestration.Controllers;

/// <summary>
/// API endpoints for action queue status and control.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/action-queue")]
[Produces("application/json")]
public class ActionQueueController(
    IActionQueueCoordinator actionQueueCoordinator,
    IProjectService projectService,
    ILogger<ActionQueueController> logger) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    /// <summary>
    /// Start action queue execution on a root issue.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType<ActionQueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionQueueStatusResponse>> Start(
        string projectId,
        [FromBody] StartActionQueueRequest request,
        CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        if (string.IsNullOrWhiteSpace(request.IssueId))
            return BadRequest("IssueId is required");

        try
        {
            await actionQueueCoordinator.StartExecution(
                projectId,
                request.IssueId,
                project.LocalPath,
                project.DefaultBranch,
                cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }

        logger.LogInformation("Started action queue execution for project {ProjectId} from issue {IssueId}", projectId, request.IssueId);

        var status = BuildStatusResponse(projectId);
        return Ok(status);
    }

    /// <summary>
    /// Get current action queue coordinator state for a project. Queues are paginated
    /// via the optional <paramref name="limit"/> (default 50, max 200) and
    /// <paramref name="offset"/> (default 0) query parameters.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<ActionQueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionQueueStatusResponse>> GetStatus(
        string projectId,
        [FromQuery] int? limit = null,
        [FromQuery] int? offset = null)
    {
        if (limit is < 1 or > MaxLimit)
            return BadRequest($"limit must be between 1 and {MaxLimit}");
        if (offset is < 0)
            return BadRequest("offset must be >= 0");

        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        var state = actionQueueCoordinator.GetStatus(projectId);
        if (state == null)
            return NotFound("No active execution for this project");

        var response = BuildStatusResponse(projectId, limit ?? DefaultLimit, offset ?? 0);
        return Ok(response);
    }

    /// <summary>
    /// Cancel all active queues for a project.
    /// </summary>
    [HttpPost("cancel")]
    [ProducesResponseType<ActionQueueStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActionQueueStatusResponse>> Cancel(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
            return NotFound("Project not found");

        var state = actionQueueCoordinator.GetStatus(projectId);
        if (state == null)
            return NotFound("No active execution for this project");

        actionQueueCoordinator.CancelAll(projectId);

        logger.LogInformation("Cancelled action queue execution for project {ProjectId}", projectId);

        var response = BuildStatusResponse(projectId);
        return Ok(response);
    }

    private ActionQueueStatusResponse? BuildStatusResponse(
        string projectId,
        int limit = DefaultLimit,
        int offset = 0)
    {
        var state = actionQueueCoordinator.GetStatus(projectId);
        if (state == null)
            return null;

        var pageQueues = state.ActiveQueues
            .Skip(offset)
            .Take(limit)
            .Select(q => new ActionQueueDetail
            {
                Id = q.Id,
                State = q.State.ToString(),
                CurrentIssueId = q.CurrentRequest?.IssueId,
                PendingCount = q.PendingRequests.Count,
                History = q.History.Select(h => new ActionQueueHistoryEntry
                {
                    IssueId = h.IssueId,
                    Success = h.Success,
                    Error = h.Error,
                    StartedAt = h.StartedAt,
                    CompletedAt = h.CompletedAt
                }).ToList()
            })
            .ToList();

        var allHistory = state.ActiveQueues.SelectMany(q => q.History).ToList();
        var currentCount = state.ActiveQueues.Count(q => q.CurrentRequest != null);
        var pendingCount = state.ActiveQueues.Sum(q => q.PendingRequests.Count);

        return new ActionQueueStatusResponse
        {
            ProjectId = state.ProjectId,
            Status = state.Status.ToString(),
            RootIssueId = state.RootIssueId,
            MaxConcurrency = state.MaxConcurrency,
            RunningQueueCount = state.RunningQueueCount,
            Queues = pageQueues,
            TotalQueueCount = state.ActiveQueues.Count,
            Limit = limit,
            Offset = offset,
            Progress = new ActionQueueProgress
            {
                TotalIssues = allHistory.Count + currentCount + pendingCount,
                Completed = allHistory.Count(h => h.Success),
                Failed = allHistory.Count(h => !h.Success),
                Remaining = currentCount + pendingCount
            }
        };
    }
}
