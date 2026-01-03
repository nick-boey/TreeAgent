using Microsoft.EntityFrameworkCore;
using Octokit;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.PullRequests;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using TrackedPullRequest = TreeAgent.Web.Features.PullRequests.Data.Entities.PullRequest;

namespace TreeAgent.Web.Features.GitHub;

public class GitHubService(
    TreeAgentDbContext db,
    ICommandRunner commandRunner,
    IConfiguration configuration,
    IGitHubClientWrapper githubClient,
    ILogger<GitHubService> logger)
    : IGitHubService
{
    private string? GetGitHubToken()
    {
        return configuration["GITHUB_TOKEN"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    public async Task<bool> IsConfiguredAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null)
        {
            logger.LogDebug("GitHub not configured: project {ProjectId} not found", projectId);
            return false;
        }

        if (string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogDebug("GitHub not configured for project {ProjectId}: owner or repo not set", projectId);
            return false;
        }

        var token = GetGitHubToken();
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("GitHub not configured for project {ProjectId}: GITHUB_TOKEN not found in configuration or environment", projectId);
            return false;
        }

        return true;
    }

    public async Task<List<PullRequestInfo>> GetOpenPullRequestsAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogWarning("Cannot fetch open PRs: project {ProjectId} not found or GitHub not configured", projectId);
            return [];
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching open PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            var request = new PullRequestRequest
            {
                State = ItemStateFilter.Open
            };

            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);
            logger.LogInformation("Retrieved {Count} open PRs from {Owner}/{Repo}", prs.Count, project.GitHubOwner, project.GitHubRepo);
            return prs.Select(MapToPullRequestInfo).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch open PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    public async Task<List<PullRequestInfo>> GetClosedPullRequestsAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogWarning("Cannot fetch closed PRs: project {ProjectId} not found or GitHub not configured", projectId);
            return [];
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching closed PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            var request = new PullRequestRequest
            {
                State = ItemStateFilter.Closed
            };

            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);
            logger.LogInformation("Retrieved {Count} closed PRs from {Owner}/{Repo}", prs.Count, project.GitHubOwner, project.GitHubRepo);
            return prs.Select(MapToPullRequestInfo).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch closed PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    public async Task<PullRequestInfo?> GetPullRequestAsync(string projectId, int prNumber)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogWarning("Cannot fetch PR #{PrNumber}: project {ProjectId} not found or GitHub not configured", prNumber, projectId);
            return null;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            var pr = await githubClient.GetPullRequestAsync(project.GitHubOwner, project.GitHubRepo, prNumber);
            logger.LogDebug("Retrieved PR #{PrNumber}: {Title}", prNumber, pr.Title);
            return MapToPullRequestInfo(pr);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            return null;
        }
    }

    public async Task<PullRequestInfo?> CreatePullRequestAsync(string projectId, string pullRequestId)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == pullRequestId);

        if (pullRequest == null)
        {
            logger.LogWarning("Cannot create PR: pull request {PullRequestId} not found", pullRequestId);
            return null;
        }

        var project = pullRequest.Project;
        if (string.IsNullOrEmpty(project.GitHubOwner) ||
            string.IsNullOrEmpty(project.GitHubRepo) ||
            string.IsNullOrEmpty(pullRequest.BranchName))
        {
            logger.LogWarning("Cannot create PR: GitHub not configured or branch not set for pull request {PullRequestId}", pullRequestId);
            return null;
        }

        // First push the branch
        logger.LogInformation("Pushing branch {Branch} to {Owner}/{Repo}", pullRequest.BranchName, project.GitHubOwner, project.GitHubRepo);
        var pushed = await PushBranchAsync(projectId, pullRequest.BranchName);
        if (!pushed)
        {
            logger.LogError("Failed to push branch {Branch}", pullRequest.BranchName);
            return null;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Creating PR for branch {Branch} in {Owner}/{Repo}", pullRequest.BranchName, project.GitHubOwner, project.GitHubRepo);
            var newPr = new NewPullRequest(
                pullRequest.Title,
                pullRequest.BranchName,
                project.DefaultBranch)
            {
                Body = pullRequest.Description
            };

            var pr = await githubClient.CreatePullRequestAsync(project.GitHubOwner, project.GitHubRepo, newPr);
            logger.LogInformation("Created PR #{PrNumber}: {Title}", pr.Number, pr.Title);

            // Update pull request with PR number
            pullRequest.GitHubPRNumber = pr.Number;
            pullRequest.Status = OpenPullRequestStatus.ReadyForReview;
            pullRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return MapToPullRequestInfo(pr);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PR for branch {Branch} in {Owner}/{Repo}", pullRequest.BranchName, project.GitHubOwner, project.GitHubRepo);
            return null;
        }
    }

    public async Task<bool> PushBranchAsync(string projectId, string branchName)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot push branch: project {ProjectId} not found", projectId);
            return false;
        }

        var workingDir = project.LocalPath;

        // Push the branch to origin
        logger.LogInformation("Pushing branch {Branch} to origin", branchName);
        var result = await commandRunner.RunAsync("git", $"push -u origin \"{branchName}\"", workingDir);

        if (result.Success)
        {
            logger.LogInformation("Successfully pushed branch {Branch}", branchName);
        }
        else
        {
            logger.LogError("Failed to push branch {Branch}: {Error}", branchName, result.Error);
        }

        return result.Success;
    }

    /// <summary>
    /// Syncs only open pull requests from GitHub. Closed/merged PRs are removed from local tracking.
    /// </summary>
    public async Task<SyncResult> SyncPullRequestsAsync(string projectId)
    {
        var result = new SyncResult();

        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogWarning("Cannot sync PRs: project {ProjectId} not found or GitHub not configured", projectId);
            result.Errors.Add("Project not found or GitHub not configured");
            return result;
        }

        logger.LogInformation("Starting PR sync for {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);

        // Only fetch open PRs - closed/merged PRs should be retrieved from GitHub when needed
        var openPrs = await GetOpenPullRequestsAsync(projectId);
        var openPrNumbers = openPrs.Select(pr => pr.Number).ToHashSet();

        // Get existing tracked pull requests
        var existingPullRequests = await db.PullRequests
            .Where(pr => pr.ProjectId == projectId && pr.GitHubPRNumber != null)
            .ToListAsync();

        // Remove PRs that are no longer open on GitHub
        foreach (var pr in existingPullRequests)
        {
            if (pr.GitHubPRNumber.HasValue && !openPrNumbers.Contains(pr.GitHubPRNumber.Value))
            {
                logger.LogInformation("Removing closed/merged PR #{PrNumber} from local tracking", pr.GitHubPRNumber);
                db.PullRequests.Remove(pr);
                result.Removed++;
            }
        }

        var existingPrNumbers = existingPullRequests
            .Where(pr => pr.GitHubPRNumber.HasValue && openPrNumbers.Contains(pr.GitHubPRNumber.Value))
            .Select(pr => pr.GitHubPRNumber!.Value)
            .ToHashSet();

        foreach (var pr in openPrs)
        {
            try
            {
                if (existingPrNumbers.Contains(pr.Number))
                {
                    // Update existing pull request
                    var pullRequest = existingPullRequests.First(p => p.GitHubPRNumber == pr.Number);
                    pullRequest.Title = pr.Title;
                    pullRequest.Description = pr.Body;
                    pullRequest.UpdatedAt = DateTime.UtcNow;
                    result.Updated++;
                }
                else
                {
                    // Import as new tracked pull request
                    var pullRequest = new TrackedPullRequest
                    {
                        ProjectId = projectId,
                        Title = pr.Title,
                        Description = pr.Body,
                        BranchName = pr.BranchName,
                        GitHubPRNumber = pr.Number,
                        Status = OpenPullRequestStatus.ReadyForReview,
                        CreatedAt = pr.CreatedAt
                    };

                    db.PullRequests.Add(pullRequest);
                    result.Imported++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync PR #{PrNumber}", pr.Number);
                result.Errors.Add($"PR #{pr.Number}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("PR sync completed: {Imported} imported, {Updated} updated, {Removed} removed, {Errors} errors",
            result.Imported, result.Updated, result.Removed, result.Errors.Count);
        return result;
    }

    public async Task<bool> LinkPullRequestAsync(string pullRequestId, int prNumber)
    {
        var pullRequest = await db.PullRequests.FindAsync(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot link PR: pull request {PullRequestId} not found", pullRequestId);
            return false;
        }

        logger.LogInformation("Linking PR #{PrNumber} to pull request {PullRequestId}", prNumber, pullRequestId);
        pullRequest.GitHubPRNumber = prNumber;
        pullRequest.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return true;
    }

    private void ConfigureClient()
    {
        var token = GetGitHubToken();
        if (githubClient is GitHubClientWrapper wrapper)
        {
            var hasToken = !string.IsNullOrEmpty(token);
            logger.LogDebug("Configuring GitHub client (token present: {HasToken})", hasToken);
            wrapper.SetToken(token);
        }
    }

    private static PullRequestInfo MapToPullRequestInfo(Octokit.PullRequest pr)
    {
        PullRequestStatus status;

        if (pr.Merged)
        {
            status = PullRequestStatus.Merged;
        }
        else if (pr.State.Value == ItemState.Closed)
        {
            status = PullRequestStatus.Closed;
        }
        else
        {
            // Open PRs default to InProgress; refined status requires additional API calls
            status = PullRequestStatus.InProgress;
        }

        return new PullRequestInfo
        {
            Number = pr.Number,
            Title = pr.Title,
            Body = pr.Body,
            Status = status,
            BranchName = pr.Head.Ref,
            HtmlUrl = pr.HtmlUrl,
            CreatedAt = pr.CreatedAt.UtcDateTime,
            MergedAt = pr.MergedAt?.UtcDateTime,
            ClosedAt = pr.ClosedAt?.UtcDateTime,
            UpdatedAt = pr.UpdatedAt.UtcDateTime
        };
    }
}
