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
    Task<Graph> BuildGraphAsync(string projectId);

    /// <summary>
    /// Builds graph JSON data for a project, ready for Gitgraph.js visualization.
    /// </summary>
    Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId);
}
