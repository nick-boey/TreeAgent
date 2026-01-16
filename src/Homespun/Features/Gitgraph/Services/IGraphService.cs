using Homespun.Features.Gitgraph.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from BeadsIssues and PullRequests.
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Builds a complete graph for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5);

    /// <summary>
    /// Builds graph JSON data for a project, ready for Gitgraph.js visualization.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5);
}
