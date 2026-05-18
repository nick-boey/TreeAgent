using Homespun.Features.Fleece.Services;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Features.OpenSpec.Controllers;

/// <summary>
/// Per-decoration endpoints that surface OpenSpec state to the task-graph view
/// without bundling everything into a single response.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class OpenSpecDecorationsController(
    IDataStore dataStore,
    IProjectFleeceService fleeceService,
    IIssueGraphOpenSpecEnricher enricher) : ControllerBase
{
    /// <summary>
    /// Returns the per-issue OpenSpec state map. The optional <paramref name="issues"/>
    /// query param scopes the scan to a subset (defaults to every visible issue);
    /// the frontend supplies the visible-set ids it just fetched.
    /// </summary>
    [HttpGet("projects/{projectId}/openspec-states")]
    [ProducesResponseType<Dictionary<string, IssueOpenSpecState>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Dictionary<string, IssueOpenSpecState>>> GetOpenSpecStates(
        string projectId,
        [FromQuery] string? issues = null,
        CancellationToken ct = default)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            return NotFound("Project not found");
        }

        IReadOnlyCollection<string> issueIds;
        if (!string.IsNullOrWhiteSpace(issues))
        {
            issueIds = issues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else if (!string.IsNullOrEmpty(project.LocalPath))
        {
            var allIssues = await fleeceService.ListIssuesAsync(project.LocalPath, includeAll: true, ct: ct);
            issueIds = allIssues.Select(i => i.Id).ToList();
        }
        else
        {
            issueIds = Array.Empty<string>();
        }

        var map = await enricher.GetOpenSpecStatesAsync(projectId, issueIds, branchContext: null, ct);
        return Ok(map);
    }
}
