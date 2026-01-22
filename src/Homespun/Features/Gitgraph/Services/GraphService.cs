using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from Fleece Issues and PullRequests.
/// Uses IFleeceService for fast issue access.
/// </summary>
public class GraphService(
    ProjectService projectService,
    IGitHubService gitHubService,
    IFleeceService fleeceService,
    ILogger<GraphService> logger) : IGraphService
{
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    /// <inheritdoc />
    public async Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project not found: {ProjectId}", projectId);
            return new Graph([], new Dictionary<string, GraphBranch>());
        }

        // Fetch PRs from GitHub
        var openPrs = await GetOpenPullRequestsSafe(projectId);
        var closedPrs = await GetClosedPullRequestsSafe(projectId);
        var allPrs = closedPrs.Concat(openPrs).ToList();

        // Fetch issues from Fleece
        var issues = await GetIssuesAsync(project.LocalPath);

        logger.LogDebug(
            "Building graph for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {IssueCount} issues, maxPastPRs: {MaxPastPRs}",
            projectId, openPrs.Count, closedPrs.Count, issues.Count, maxPastPRs);

        return _graphBuilder.Build(allPrs, issues, maxPastPRs);
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5)
    {
        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        return _mapper.ToJson(graph);
    }

    private async Task<List<PullRequestInfo>> GetOpenPullRequestsSafe(string projectId)
    {
        try
        {
            return await gitHubService.GetOpenPullRequestsAsync(projectId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch open PRs for project {ProjectId}", projectId);
            return [];
        }
    }

    private async Task<List<PullRequestInfo>> GetClosedPullRequestsSafe(string projectId)
    {
        try
        {
            return await gitHubService.GetClosedPullRequestsAsync(projectId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch closed PRs for project {ProjectId}", projectId);
            return [];
        }
    }

    /// <summary>
    /// Gets issues from Fleece.
    /// Returns open issues only (no Complete, Closed, Archived, or Deleted).
    /// </summary>
    private async Task<List<Issue>> GetIssuesAsync(string workingDirectory)
    {
        try
        {
            // Check if .fleece directory exists
            var fleeceDir = Path.Combine(workingDirectory, ".fleece");
            if (!Directory.Exists(fleeceDir))
            {
                logger.LogDebug("Fleece not initialized for {WorkingDirectory}", workingDirectory);
                return [];
            }

            // Get open issues only (all non-completed statuses)
            var issues = await fleeceService.ListIssuesAsync(workingDirectory);
            return issues
                .Where(i => i.Status is IssueStatus.Idea or IssueStatus.Spec or IssueStatus.Next or IssueStatus.Progress or IssueStatus.Review)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get issues for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }
}
