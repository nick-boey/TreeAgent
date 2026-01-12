using Homespun.Features.Beads.Data;
using Homespun.Features.Beads.Services;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from BeadsIssues and PullRequests.
/// </summary>
public class GraphService(
    ProjectService projectService,
    IGitHubService gitHubService,
    IBeadsService beadsService,
    ILogger<GraphService> logger) : IGraphService
{
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    /// <inheritdoc />
    public async Task<Graph> BuildGraphAsync(string projectId)
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

        // Fetch issues from beads
        var issues = await GetIssuesSafe(project.LocalPath);

        // Fetch dependencies for issues that have them
        var dependencies = await GetDependenciesSafe(project.LocalPath, issues);

        logger.LogDebug(
            "Building graph for project {ProjectId}: {OpenPrCount} open PRs, {ClosedPrCount} closed PRs, {IssueCount} issues, {DepCount} dependencies",
            projectId, openPrs.Count, closedPrs.Count, issues.Count, dependencies.Count);

        return _graphBuilder.Build(allPrs, issues, dependencies);
    }

    /// <inheritdoc />
    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId)
    {
        var graph = await BuildGraphAsync(projectId);
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

    private async Task<List<BeadsIssue>> GetIssuesSafe(string workingDirectory)
    {
        try
        {
            var isInitialized = await beadsService.IsInitializedAsync(workingDirectory);
            if (!isInitialized)
            {
                return [];
            }

            // Get all open issues (for future work)
            var openIssues = await beadsService.ListIssuesAsync(workingDirectory, new BeadsListOptions
            {
                Status = "open"
            });

            // Get in-progress issues (current work)
            var inProgressIssues = await beadsService.ListIssuesAsync(workingDirectory, new BeadsListOptions
            {
                Status = "in_progress"
            });

            // Combine and dedupe
            var allIssues = openIssues
                .Concat(inProgressIssues)
                .DistinctBy(i => i.Id)
                .ToList();

            return allIssues;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch issues from beads for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }

    private async Task<List<BeadsDependency>> GetDependenciesSafe(string workingDirectory, List<BeadsIssue> issues)
    {
        var allDependencies = new List<BeadsDependency>();

        try
        {
            // Get dependencies for each issue that might have them
            foreach (var issue in issues)
            {
                try
                {
                    var deps = await beadsService.GetDependencyTreeAsync(workingDirectory, issue.Id);
                    allDependencies.AddRange(deps);
                }
                catch
                {
                    // Ignore failures for individual issues
                }
            }

            // Dedupe dependencies
            return allDependencies
                .DistinctBy(d => (d.FromIssueId, d.ToIssueId, d.Type))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch dependencies from beads for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }
}
