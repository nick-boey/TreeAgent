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
/// Uses direct SQLite access via IBeadsDatabaseService for performance.
/// </summary>
public class GraphService(
    ProjectService projectService,
    IGitHubService gitHubService,
    IBeadsService beadsService,
    IBeadsDatabaseService beadsDatabaseService,
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

        // Load beads data from SQLite into cache (single database read)
        await LoadBeadsDataAsync(project.LocalPath);

        // Fetch issues from cache
        var issues = GetIssuesFromCache(project.LocalPath);

        // Fetch dependencies from cache
        var dependencies = GetDependenciesFromCache(project.LocalPath, issues);

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

    /// <summary>
    /// Loads beads data from SQLite into the in-memory cache.
    /// This is a single database read that replaces multiple CLI calls.
    /// </summary>
    private async Task LoadBeadsDataAsync(string workingDirectory)
    {
        try
        {
            // Check if beads is initialized using CLI (fast check)
            var isInitialized = await beadsService.IsInitializedAsync(workingDirectory);
            if (!isInitialized)
            {
                logger.LogDebug("Beads not initialized for {WorkingDirectory}", workingDirectory);
                return;
            }

            // Load all data from SQLite into cache (single read operation)
            await beadsDatabaseService.RefreshFromDatabaseAsync(workingDirectory);
            logger.LogDebug("Loaded beads data from SQLite for {WorkingDirectory}", workingDirectory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load beads data for {WorkingDirectory}", workingDirectory);
        }
    }

    /// <summary>
    /// Gets issues from the in-memory cache (fast, no CLI calls).
    /// </summary>
    private List<BeadsIssue> GetIssuesFromCache(string workingDirectory)
    {
        try
        {
            if (!beadsDatabaseService.IsProjectLoaded(workingDirectory))
            {
                return [];
            }

            // Get open and in-progress issues from cache
            var openIssues = beadsDatabaseService.ListIssues(workingDirectory, new BeadsListOptions
            {
                Status = "open"
            });

            var inProgressIssues = beadsDatabaseService.ListIssues(workingDirectory, new BeadsListOptions
            {
                Status = "in_progress"
            });

            // Combine and dedupe
            return openIssues
                .Concat(inProgressIssues)
                .DistinctBy(i => i.Id)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get issues from cache for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }

    /// <summary>
    /// Gets dependencies from the in-memory cache (fast, no CLI calls).
    /// </summary>
    private List<BeadsDependency> GetDependenciesFromCache(string workingDirectory, List<BeadsIssue> issues)
    {
        var allDependencies = new List<BeadsDependency>();

        try
        {
            // Get dependencies for each issue from cache (instant, no CLI calls)
            foreach (var issue in issues)
            {
                var deps = beadsDatabaseService.GetDependencies(workingDirectory, issue.Id);
                allDependencies.AddRange(deps);
            }

            // Dedupe dependencies
            return allDependencies
                .DistinctBy(d => (d.FromIssueId, d.ToIssueId, d.Type))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get dependencies from cache for {WorkingDirectory}", workingDirectory);
            return [];
        }
    }
}
