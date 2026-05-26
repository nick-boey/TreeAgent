using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.PullRequests;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Fleece.Controllers;

/// <summary>
/// API endpoints for managing Fleece issues.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class IssuesController(
    IProjectFleeceService fleeceService,
    IProjectService projectService,
    IDataStore dataStore,
    IHubContext<NotificationHub> notificationHub,
    IIssueBranchResolverService branchResolverService,
    IClaudeSessionService sessionService,
    IGitCloneService cloneService,
    IBranchIdBackgroundService branchIdBackgroundService,
    IFleeceChangeApplicationService changeApplicationService,
    IFleeceIssuesSyncService fleeceIssuesSyncService,
    IAgentStartBackgroundService agentStartBackgroundService,
    IAgentStartupTracker agentStartupTracker,
    IIssueAncestorTraversalService ancestorTraversal,
    IModelCatalogService modelCatalog,
    IPhaseDispatchGuard phaseDispatchGuard,
    IIssueUndoRedoService undoRedoService,
    ILogger<IssuesController> logger) : ControllerBase
{
    private static readonly IssueStatus[] OpenStatuses =
    {
        IssueStatus.Draft, IssueStatus.Open, IssueStatus.Progress, IssueStatus.Review
    };

    /// <summary>
    /// Get the visible issue set for a project: every open issue plus every
    /// ancestor of an open issue, plus any issues named in <paramref name="include"/>
    /// (with their ancestors), plus issues linked to open PRs when
    /// <paramref name="includeOpenPrLinked"/> is true. Pass <c>includeAll=true</c>
    /// to bypass visibility filtering and return the raw list.
    /// </summary>
    [HttpGet("projects/{projectId}/issues")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetByProject(
        string projectId,
        [FromQuery] IssueStatus? status = null,
        [FromQuery] IssueType? type = null,
        [FromQuery] int? priority = null,
        [FromQuery] string? include = null,
        [FromQuery] bool includeOpenPrLinked = false,
        [FromQuery] bool includeAll = false)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Always pull the unfiltered list — visibility and post-filters apply here.
        var allIssues = await fleeceService.ListIssuesAsync(project.LocalPath, includeAll: true);

        IEnumerable<Issue> filtered;
        if (includeAll)
        {
            filtered = allIssues;
        }
        else
        {
            var seedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in allIssues)
            {
                if (OpenStatuses.Contains(issue.Status))
                {
                    seedIds.Add(issue.Id);
                }
            }

            if (!string.IsNullOrWhiteSpace(include))
            {
                foreach (var id in include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    seedIds.Add(id);
                }
            }

            if (includeOpenPrLinked)
            {
                // Every PR in the data store is, by construction, open: merged/closed PRs are
                // removed via dataStore.RemovePullRequestAsync.
                foreach (var pr in dataStore.GetPullRequestsByProject(projectId))
                {
                    if (!string.IsNullOrEmpty(pr.FleeceIssueId))
                    {
                        seedIds.Add(pr.FleeceIssueId);
                    }
                }
            }

            filtered = ancestorTraversal.CollectVisible(allIssues, seedIds);
        }

        if (status.HasValue) filtered = filtered.Where(i => i.Status == status.Value);
        if (type.HasValue) filtered = filtered.Where(i => i.Type == type.Value);
        if (priority.HasValue) filtered = filtered.Where(i => i.Priority == priority.Value);

        var response = filtered.ToList().ToResponseList();
        logger.LogDebug("Returning {Count} issues for project {ProjectId} (includeAll={IncludeAll})", response.Count, projectId, includeAll);
        return Ok(response);
    }

    /// <summary>
    /// Get ready issues for a project (issues with no blocking dependencies).
    /// </summary>
    [HttpGet("projects/{projectId}/issues/ready")]
    [ProducesResponseType<List<IssueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<IssueResponse>>> GetReadyIssues(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issues = await fleeceService.GetReadyIssuesAsync(project.LocalPath);
        var response = issues.ToResponseList();
        logger.LogDebug("Returning {Count} ready issues for project {ProjectId}", response.Count, projectId);
        return Ok(response);
    }

    /// <summary>
    /// Get unique assignees for a project's issues.
    /// Returns all unique email addresses found in issue assignments, plus the current user if configured.
    /// </summary>
    [HttpGet("projects/{projectId}/issues/assignees")]
    [ProducesResponseType<ProjectAssigneesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectAssigneesResponse>> GetProjectAssignees(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Get all issues (including those with terminal statuses) to collect all assignees
        var issues = await fleeceService.ListIssuesAsync(project.LocalPath);
        var assignees = issues
            .Where(i => !string.IsNullOrWhiteSpace(i.AssignedTo))
            .Select(i => i.AssignedTo!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Include current user email if configured and not already in list
        if (!string.IsNullOrWhiteSpace(dataStore.UserEmail) &&
            !assignees.Contains(dataStore.UserEmail, StringComparer.OrdinalIgnoreCase))
        {
            assignees.Insert(0, dataStore.UserEmail);
        }

        logger.LogDebug("Returning {Count} assignees for project {ProjectId}", assignees.Count, projectId);
        return Ok(new ProjectAssigneesResponse { Assignees = assignees });
    }

    /// <summary>
    /// Get an issue by ID.
    /// </summary>
    [HttpGet("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> GetById(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }
        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Get the resolved branch name for an issue.
    /// Checks linked PRs first, then existing clones with matching issue ID.
    /// Returns null if no existing branch is found.
    /// </summary>
    [HttpGet("issues/{issueId}/resolved-branch")]
    [ProducesResponseType<ResolvedBranchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResolvedBranchResponse>> GetResolvedBranch(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var branchName = await branchResolverService.ResolveIssueBranchAsync(projectId, issueId);
        return Ok(new ResolvedBranchResponse { BranchName = branchName });
    }

    /// <summary>
    /// Create a new issue.
    /// If a working branch ID is provided, it will be applied to the issue.
    /// Otherwise, the client should trigger branch ID generation on the edit page.
    /// </summary>
    [HttpPost("issues")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Create([FromBody] CreateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Group all internal writes (create + optional working-branch update + optional parent
        // link) under one undo entry so a single Ctrl+Z reverses the entire HTTP call.
        Issue issue;
        using (undoRedoService.BeginBatch(project.LocalPath))
        {
            issue = await fleeceService.CreateIssueAsync(
                project.LocalPath,
                request.Title,
                request.Type,
                request.Description,
                request.Priority,
                request.ExecutionMode,
                assignedTo: dataStore.UserEmail);

            // Apply provided working branch ID if any
            if (!string.IsNullOrWhiteSpace(request.WorkingBranchId))
            {
                issue = await fleeceService.UpdateIssueAsync(
                    project.LocalPath,
                    issue.Id,
                    workingBranchId: request.WorkingBranchId.Trim()) ?? issue;
            }

            // If a parent issue ID was provided, add this issue as a child of that parent
            if (!string.IsNullOrWhiteSpace(request.ParentIssueId))
            {
                issue = await fleeceService.AddParentAsync(
                    project.LocalPath,
                    issue.Id,
                    request.ParentIssueId.Trim(),
                    siblingIssueId: request.SiblingIssueId?.Trim(),
                    insertBefore: request.InsertBefore);
            }

            // If a child issue ID was provided, make the new issue the parent of that child
            if (!string.IsNullOrWhiteSpace(request.ChildIssueId))
            {
                await fleeceService.AddParentAsync(
                    project.LocalPath,
                    request.ChildIssueId.Trim(),
                    issue.Id);
            }
        }

        // Broadcast issue creation to connected clients
        await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Created, issue.Id, issue.ToResponse());

        // Trigger background branch ID generation if title is provided and no working branch ID
        if (!string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.WorkingBranchId))
        {
            await branchIdBackgroundService.QueueBranchIdGenerationAsync(issue.Id, request.ProjectId, request.Title);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { issueId = issue.Id, projectId = request.ProjectId },
            issue.ToResponse());
    }

    /// <summary>
    /// Update an issue.
    /// </summary>
    [HttpPut("issues/{issueId}")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> Update(string issueId, [FromBody] UpdateIssueRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Get the current issue to check for title changes
        var currentIssue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (currentIssue == null)
        {
            return NotFound("Issue not found");
        }

        // Auto-assign current user if issue has no assignee and request doesn't specify one
        var assignedTo = request.AssignedTo;
        if (string.IsNullOrWhiteSpace(currentIssue.AssignedTo) &&
            string.IsNullOrWhiteSpace(request.AssignedTo) &&
            !string.IsNullOrWhiteSpace(dataStore.UserEmail))
        {
            assignedTo = dataStore.UserEmail;
        }

        var issue = await fleeceService.UpdateIssueAsync(
            project.LocalPath,
            issueId,
            request.Title,
            request.Status,
            request.Type,
            request.Description,
            request.Priority,
            request.ExecutionMode,
            request.WorkingBranchId,
            assignedTo);

        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Single unified update event — the canonical post-mutation issue body
        // is the source of truth for the client cache.
        await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, issueId, issue.ToResponse());

        // Check if title changed and working branch ID is empty
        if (!string.IsNullOrWhiteSpace(request.Title) &&
            request.Title != currentIssue.Title &&
            string.IsNullOrWhiteSpace(issue.WorkingBranchId))
        {
            await branchIdBackgroundService.QueueBranchIdGenerationAsync(issue.Id, request.ProjectId, request.Title);
        }

        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Delete an issue.
    /// </summary>
    [HttpDelete("issues/{issueId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string issueId, [FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var deleted = await fleeceService.DeleteIssueAsync(project.LocalPath, issueId);
        if (!deleted)
        {
            return NotFound("Issue not found");
        }

        // Broadcast issue deletion to connected clients
        await notificationHub.BroadcastIssueChanged(projectId, IssueChangeType.Deleted, issueId, null);

        return NoContent();
    }

    /// <summary>
    /// Set the parent of an issue.
    /// Can either replace all existing parents or add to existing parents.
    /// </summary>
    [HttpPost("issues/{childId}/set-parent")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> SetParent(string childId, [FromBody] SetParentRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Check if the child issue exists
        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, childId);
        if (existingIssue == null)
        {
            return NotFound("Child issue not found");
        }

        try
        {
            var issue = await fleeceService.SetParentAsync(
                project.LocalPath,
                childId,
                request.ParentIssueId,
                request.AddToExisting);

            // Broadcast issue update to connected clients
            await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, childId, issue.ToResponse());

            return Ok(issue.ToResponse());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cycle"))
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Remove a specific parent from an issue.
    /// </summary>
    [HttpPost("issues/{childId}/remove-parent")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> RemoveParent(string childId, [FromBody] RemoveParentRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, childId);
        if (existingIssue == null)
        {
            return NotFound("Child issue not found");
        }

        var issue = await fleeceService.RemoveParentAsync(
            project.LocalPath,
            childId,
            request.ParentIssueId);

        await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, childId, issue.ToResponse());

        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Remove all parents from an issue.
    /// </summary>
    [HttpPost("issues/{issueId}/remove-all-parents")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> RemoveAllParents(string issueId, [FromBody] RemoveAllParentsRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (existingIssue == null)
        {
            return NotFound("Issue not found");
        }

        var issue = await fleeceService.RemoveAllParentsAsync(
            project.LocalPath,
            issueId);

        await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, issueId, issue.ToResponse());

        return Ok(issue.ToResponse());
    }

    /// <summary>
    /// Move a sibling issue up or down in the series order.
    /// </summary>
    [HttpPost("issues/{issueId}/move-sibling")]
    [ProducesResponseType<IssueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueResponse>> MoveSeriesSibling(string issueId, [FromBody] MoveSeriesSiblingRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Check if the issue exists
        var existingIssue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (existingIssue == null)
        {
            return NotFound("Issue not found");
        }

        try
        {
            var issue = await fleeceService.MoveSeriesSiblingAsync(
                project.LocalPath,
                issueId,
                request.Direction);

            // Broadcast issue update to connected clients
            await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, issueId, issue.ToResponse());

            return Ok(issue.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    #region Agent Operations

    /// <summary>
    /// Run an agent on an issue.
    /// This endpoint returns 202 Accepted immediately and handles the agent startup in the background:
    /// 1. Validates project, issue, and prompt existence
    /// 2. Queues background work to:
    ///    - Create/resolve the working branch/clone
    ///    - Render the prompt template with issue context
    ///    - Create the session and send the initial message
    /// 3. Clients receive SignalR notifications when the agent is ready or fails
    /// </summary>
    [HttpPost("issues/{issueId}/run")]
    [ProducesResponseType<RunAgentAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<AgentAlreadyRunningResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<PhaseDispatchBlockedResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunAgentAcceptedResponse>> RunAgent(string issueId, [FromBody] RunAgentRequest request)
    {
        // Fetch project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Fetch issue
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Check for existing active session on this issue
        var existingSession = sessionService.GetSessionByEntityId(issueId);
        if (existingSession != null && existingSession.Status.IsActive())
        {
            return Conflict(new AgentAlreadyRunningResponse
            {
                SessionId = existingSession.Id,
                Status = existingSession.Status,
                Message = "An agent is already running on this issue"
            });
        }

        // Try to atomically mark as starting to prevent race conditions
        if (!agentStartupTracker.TryMarkAsStarting(issueId))
        {
            return Conflict(new AgentAlreadyRunningResponse
            {
                SessionId = string.Empty,
                Status = ClaudeSessionStatus.Starting,
                Message = "Agent is already starting on this issue"
            });
        }

        // Resolve branch name - check for existing branch first, then generate from issue
        var branchName = await branchResolverService.ResolveIssueBranchAsync(request.ProjectId, issueId)
            ?? BranchNameGenerator.GenerateBranchName(issue);

        // Determine model — catalog resolves nulls + short aliases into concrete ids.
        var model = await modelCatalog.ResolveModelIdAsync(
            request.Model ?? project.DefaultModel,
            HttpContext.RequestAborted);

        // Phase pre-flight check for openspec-apply-change with a specific phase arg:
        // refuse if any prior phase in tasks.md is incomplete.
        if (string.Equals(request.SkillName, "openspec-apply-change", StringComparison.OrdinalIgnoreCase)
            && request.SkillArgs?.TryGetValue("phase", out var requestedPhase) == true
            && request.SkillArgs.TryGetValue("change", out var requestedChange) == true
            && !string.IsNullOrWhiteSpace(requestedPhase)
            && !string.IsNullOrWhiteSpace(requestedChange))
        {
            var clonePath = await cloneService.GetClonePathForBranchAsync(project.LocalPath, branchName);
            if (!string.IsNullOrEmpty(clonePath))
            {
                var blockingPhases = await phaseDispatchGuard.GetBlockingPhasesAsync(
                    clonePath, requestedChange, requestedPhase, HttpContext.RequestAborted);

                if (blockingPhases.Count > 0)
                {
                    agentStartupTracker.Clear(issueId);
                    return Conflict(new PhaseDispatchBlockedResponse
                    {
                        BlockingPhases = blockingPhases.ToList(),
                        Message = $"Phase '{requestedPhase}' cannot be dispatched: prior phases are incomplete."
                    });
                }
            }
        }

        // Queue background agent startup
        await agentStartBackgroundService.QueueAgentStartAsync(new AgentOrchestration.Services.AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = request.ProjectId,
            ProjectLocalPath = project.LocalPath,
            ProjectDefaultBranch = project.DefaultBranch,
            Issue = issue,
            BaseBranch = request.BaseBranch,
            Model = model,
            BranchName = branchName,
            UserInstructions = request.UserInstructions,
            Mode = request.Mode,
            SkillName = request.SkillName,
            SkillArgs = request.SkillArgs
        });

        logger.LogInformation(
            "Agent startup queued for issue {IssueId} with branch {BranchName}",
            issueId, branchName);

        return Accepted(new RunAgentAcceptedResponse
        {
            IssueId = issueId,
            BranchName = branchName,
            Message = "Agent is starting"
        });
    }

    /// <summary>
    /// Apply agent changes from a session back to the main branch.
    /// </summary>
    [HttpPost("issues/{issueId}/apply-agent-changes")]
    [ProducesResponseType<ApplyAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplyAgentChangesResponse>> ApplyAgentChanges(
        string issueId,
        [FromBody] ApplyAgentChangesRequest request)
    {
        // Validate project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Validate issue exists
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            return NotFound("Issue not found");
        }

        // Apply changes
        var result = await changeApplicationService.ApplyChangesAsync(
            request.ProjectId,
            request.SessionId,
            request.ConflictStrategy,
            request.DryRun);

        if (!request.DryRun && result.Success)
        {
            // Bulk event — multiple issues touched, no single canonical body.
            await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, null, null);
        }

        return Ok(result);
    }

    /// <summary>
    /// Resolve specific conflicts from a previous apply attempt.
    /// </summary>
    [HttpPost("issues/{issueId}/resolve-conflicts")]
    [ProducesResponseType<ApplyAgentChangesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplyAgentChangesResponse>> ResolveConflicts(
        string issueId,
        [FromBody] ResolveConflictsRequest request)
    {
        // Validate project
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        // Apply resolutions
        var result = await changeApplicationService.ResolveConflictsAsync(
            request.ProjectId,
            request.SessionId,
            request.Resolutions);

        if (result.Success)
        {
            // Bulk event — conflict resolution may touch multiple issues.
            await notificationHub.BroadcastIssueChanged(request.ProjectId, IssueChangeType.Updated, null, null);
        }

        return Ok(result);
    }

    #endregion

    #region History Operations

    /// <summary>
    /// Returns the current undo/redo stack pointers for the given project.
    /// </summary>
    [HttpGet("projects/{projectId}/issues/history/state")]
    [ProducesResponseType<IssueHistoryState>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryState>> GetHistoryState(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var state = await undoRedoService.GetStateAsync(project.LocalPath);
        return Ok(state);
    }

    /// <summary>
    /// Pops the top undo entry and appends its inverse events to the active
    /// change file. Returns <c>{ success: false, errorMessage: "Nothing to undo" }</c>
    /// when the stack is empty.
    /// </summary>
    [HttpPost("projects/{projectId}/issues/history/undo")]
    [ProducesResponseType<IssueHistoryOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryOperationResponse>> Undo(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var success = await undoRedoService.UndoAsync(project.LocalPath, HttpContext.RequestAborted);
        var state = await undoRedoService.GetStateAsync(project.LocalPath);

        if (success)
        {
            await notificationHub.BroadcastIssueChanged(projectId, IssueChangeType.Updated, null, null);
        }

        return Ok(new IssueHistoryOperationResponse
        {
            Success = success,
            ErrorMessage = success ? null : "Nothing to undo",
            State = state,
        });
    }

    /// <summary>
    /// Re-applies the top redo entry. Mirror of <see cref="Undo"/>.
    /// </summary>
    [HttpPost("projects/{projectId}/issues/history/redo")]
    [ProducesResponseType<IssueHistoryOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueHistoryOperationResponse>> Redo(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var success = await undoRedoService.RedoAsync(project.LocalPath, HttpContext.RequestAborted);
        var state = await undoRedoService.GetStateAsync(project.LocalPath);

        if (success)
        {
            await notificationHub.BroadcastIssueChanged(projectId, IssueChangeType.Updated, null, null);
        }

        return Ok(new IssueHistoryOperationResponse
        {
            Success = success,
            ErrorMessage = success ? null : "Nothing to redo",
            State = state,
        });
    }

    #endregion
}
