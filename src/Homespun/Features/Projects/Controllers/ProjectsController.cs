using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.Projects.Controllers;

/// <summary>
/// API endpoints for managing projects.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProjectsController(IProjectService projectService) : ControllerBase
{
    /// <summary>
    /// Get all projects.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<Project>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Project>>> GetAll()
    {
        var projects = await projectService.GetAllAsync();
        return Ok(projects);
    }

    /// <summary>
    /// Get a project by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType<Project>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Project>> GetById(string id)
    {
        var project = await projectService.GetByIdAsync(id);
        if (project == null)
        {
            return NotFound();
        }
        return Ok(project);
    }

    /// <summary>
    /// Create a project from a GitHub repository.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<Project>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Project>> Create([FromBody] CreateProjectRequest request)
    {
        CreateProjectResult result;

        if (!string.IsNullOrEmpty(request.OwnerRepo))
        {
            result = await projectService.CreateAsync(request.OwnerRepo);
        }
        else if (!string.IsNullOrEmpty(request.Name))
        {
            result = await projectService.CreateLocalAsync(request.Name, request.DefaultBranch ?? "main");
        }
        else
        {
            return BadRequest("Either ownerRepo or name must be provided.");
        }

        if (!result.Success)
        {
            return BadRequest(result.ErrorMessage);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Project!.Id }, result.Project);
    }

    /// <summary>
    /// Update a project.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType<Project>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Project>> Update(string id, [FromBody] UpdateProjectRequest request)
    {
        var project = await projectService.UpdateAsync(id, request.DefaultModel);
        if (project == null)
        {
            return NotFound();
        }
        return Ok(project);
    }

    /// <summary>
    /// Delete a project.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await projectService.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound();
        }
        return NoContent();
    }
}

/// <summary>
/// Request model for creating a project.
/// </summary>
public class CreateProjectRequest
{
    /// <summary>
    /// GitHub owner/repository (e.g., "owner/repo") for cloning from GitHub.
    /// </summary>
    public string? OwnerRepo { get; set; }

    /// <summary>
    /// Project name for creating a local-only project.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Default branch name (defaults to "main").
    /// </summary>
    public string? DefaultBranch { get; set; }
}

/// <summary>
/// Request model for updating a project.
/// </summary>
public class UpdateProjectRequest
{
    /// <summary>
    /// Default model for agent sessions.
    /// </summary>
    public string? DefaultModel { get; set; }
}
