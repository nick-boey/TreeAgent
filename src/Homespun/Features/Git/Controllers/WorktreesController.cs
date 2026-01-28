using Homespun.Features.Projects;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Git.Controllers;

/// <summary>
/// API endpoints for managing Git worktrees.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class WorktreesController(
    IGitWorktreeService worktreeService,
    IProjectService projectService) : ControllerBase
{
    /// <summary>
    /// List worktrees for a project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<WorktreeInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<WorktreeInfo>>> List([FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var worktrees = await worktreeService.ListWorktreesAsync(project.LocalPath);
        return Ok(worktrees);
    }

    /// <summary>
    /// Create a new worktree.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateWorktreeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateWorktreeResponse>> Create([FromBody] CreateWorktreeRequest request)
    {
        var project = await projectService.GetByIdAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            request.BranchName,
            request.CreateBranch,
            request.BaseBranch);

        if (worktreePath == null)
        {
            return BadRequest("Failed to create worktree");
        }

        return Created(
            string.Empty,
            new CreateWorktreeResponse { Path = worktreePath, BranchName = request.BranchName });
    }

    /// <summary>
    /// Delete a worktree.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromQuery] string projectId, [FromQuery] string worktreePath)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var removed = await worktreeService.RemoveWorktreeAsync(project.LocalPath, worktreePath);
        if (!removed)
        {
            return BadRequest("Failed to remove worktree");
        }

        return NoContent();
    }

    /// <summary>
    /// Check if a worktree exists for a branch.
    /// </summary>
    [HttpGet("exists")]
    [ProducesResponseType<WorktreeExistsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorktreeExistsResponse>> Exists([FromQuery] string projectId, [FromQuery] string branchName)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        var exists = await worktreeService.WorktreeExistsAsync(project.LocalPath, branchName);
        return Ok(new WorktreeExistsResponse { Exists = exists });
    }

    /// <summary>
    /// Prune worktrees (remove stale entries).
    /// </summary>
    [HttpPost("prune")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Prune([FromQuery] string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        await worktreeService.PruneWorktreesAsync(project.LocalPath);
        return NoContent();
    }

    /// <summary>
    /// Pull latest changes for a worktree.
    /// </summary>
    [HttpPost("pull")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Pull([FromQuery] string worktreePath)
    {
        var success = await worktreeService.PullLatestAsync(worktreePath);
        if (!success)
        {
            return BadRequest("Failed to pull latest");
        }
        return NoContent();
    }
}

/// <summary>
/// Request model for creating a worktree.
/// </summary>
public class CreateWorktreeRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// Whether to create a new branch.
    /// </summary>
    public bool CreateBranch { get; set; }

    /// <summary>
    /// Base branch for the new branch (if creating).
    /// </summary>
    public string? BaseBranch { get; set; }
}

/// <summary>
/// Response model for creating a worktree.
/// </summary>
public class CreateWorktreeResponse
{
    /// <summary>
    /// The path to the created worktree.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public required string BranchName { get; set; }
}

/// <summary>
/// Response model for checking worktree existence.
/// </summary>
public class WorktreeExistsResponse
{
    /// <summary>
    /// Whether the worktree exists.
    /// </summary>
    public bool Exists { get; set; }
}
