using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// API endpoints for managing Claude Code sessions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SessionsController(
    IClaudeSessionService sessionService,
    ProjectService projectService) : ControllerBase
{
    /// <summary>
    /// Get all active sessions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<SessionSummary>>(StatusCodes.Status200OK)]
    public ActionResult<List<SessionSummary>> GetAll()
    {
        var sessions = sessionService.GetAllSessions();
        var summaries = sessions.Select(MapToSummary).ToList();
        return Ok(summaries);
    }

    /// <summary>
    /// Get a session by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ClaudeSession> GetById(string id)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }
        return Ok(session);
    }

    /// <summary>
    /// Get sessions for a project.
    /// </summary>
    [HttpGet("project/{projectId}")]
    [ProducesResponseType<List<SessionSummary>>(StatusCodes.Status200OK)]
    public ActionResult<List<SessionSummary>> GetByProject(string projectId)
    {
        var sessions = sessionService.GetSessionsForProject(projectId);
        var summaries = sessions.Select(MapToSummary).ToList();
        return Ok(summaries);
    }

    /// <summary>
    /// Start a new Claude Code session.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ClaudeSession>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaudeSession>> Create([FromBody] CreateSessionRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var workingDirectory = request.WorkingDirectory ?? project.LocalPath;
        var model = request.Model ?? project.DefaultModel ?? "sonnet";

        try
        {
            var session = await sessionService.StartSessionAsync(
                request.EntityId,
                request.ProjectId,
                workingDirectory,
                request.Mode,
                model,
                request.SystemPrompt);

            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to start session: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a message to an existing session.
    /// </summary>
    [HttpPost("{id}/messages")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }

        try
        {
            await sessionService.SendMessageAsync(id, request.Message);
            return Accepted();
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop an existing session.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stop(string id)
    {
        var session = sessionService.GetSession(id);
        if (session == null)
        {
            return NotFound();
        }

        try
        {
            await sessionService.StopSessionAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to stop session: {ex.Message}");
        }
    }

    private static SessionSummary MapToSummary(ClaudeSession session) => new()
    {
        Id = session.Id,
        EntityId = session.EntityId,
        ProjectId = session.ProjectId,
        Model = session.Model,
        Mode = session.Mode,
        Status = session.Status,
        CreatedAt = session.CreatedAt,
        LastActivityAt = session.LastActivityAt,
        MessageCount = session.Messages.Count,
        TotalCostUsd = session.TotalCostUsd
    };
}

/// <summary>
/// Summary of a session for listing.
/// </summary>
public class SessionSummary
{
    public required string Id { get; init; }
    public required string EntityId { get; init; }
    public required string ProjectId { get; init; }
    public required string Model { get; init; }
    public required SessionMode Mode { get; init; }
    public ClaudeSessionStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public int MessageCount { get; init; }
    public decimal TotalCostUsd { get; init; }
}

/// <summary>
/// Request model for creating a session.
/// </summary>
public class CreateSessionRequest
{
    /// <summary>
    /// The entity ID (e.g., issue ID, PR ID).
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session mode.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Plan;

    /// <summary>
    /// The Claude model to use (defaults to project's default model).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Working directory (defaults to project local path).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Request model for sending a message.
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// The message to send.
    /// </summary>
    public required string Message { get; set; }
}
