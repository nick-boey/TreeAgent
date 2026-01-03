using Microsoft.EntityFrameworkCore;
using Octokit;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

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

    public async Task<List<GitHubPullRequest>> GetOpenPullRequestsAsync(string projectId)
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
            return prs.Select(MapPullRequest).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch open PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    public async Task<List<GitHubPullRequest>> GetClosedPullRequestsAsync(string projectId)
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
            return prs.Select(MapPullRequest).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch closed PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    public async Task<GitHubPullRequest?> GetPullRequestAsync(string projectId, int prNumber)
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
            return MapPullRequest(pr);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            return null;
        }
    }

    public async Task<GitHubPullRequest?> CreatePullRequestAsync(string projectId, string featureId)
    {
        var feature = await db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null)
        {
            logger.LogWarning("Cannot create PR: feature {FeatureId} not found", featureId);
            return null;
        }

        var project = feature.Project;
        if (string.IsNullOrEmpty(project.GitHubOwner) ||
            string.IsNullOrEmpty(project.GitHubRepo) ||
            string.IsNullOrEmpty(feature.BranchName))
        {
            logger.LogWarning("Cannot create PR: GitHub not configured or branch not set for feature {FeatureId}", featureId);
            return null;
        }

        // First push the branch
        logger.LogInformation("Pushing branch {Branch} to {Owner}/{Repo}", feature.BranchName, project.GitHubOwner, project.GitHubRepo);
        var pushed = await PushBranchAsync(projectId, feature.BranchName);
        if (!pushed)
        {
            logger.LogError("Failed to push branch {Branch}", feature.BranchName);
            return null;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Creating PR for branch {Branch} in {Owner}/{Repo}", feature.BranchName, project.GitHubOwner, project.GitHubRepo);
            var newPr = new NewPullRequest(
                feature.Title,
                feature.BranchName,
                project.DefaultBranch)
            {
                Body = feature.Description
            };

            var pr = await githubClient.CreatePullRequestAsync(project.GitHubOwner, project.GitHubRepo, newPr);
            logger.LogInformation("Created PR #{PrNumber}: {Title}", pr.Number, pr.Title);

            // Update feature with PR number
            feature.GitHubPRNumber = pr.Number;
            feature.Status = FeatureStatus.ReadyForReview;
            feature.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return MapPullRequest(pr);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PR for branch {Branch} in {Owner}/{Repo}", feature.BranchName, project.GitHubOwner, project.GitHubRepo);
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

        // Fetch all PRs
        var openPrs = await GetOpenPullRequestsAsync(projectId);
        var closedPrs = await GetClosedPullRequestsAsync(projectId);
        var allPrs = openPrs.Concat(closedPrs).ToList();

        // Get existing features with PR numbers
        var existingFeatures = await db.Features
            .Where(f => f.ProjectId == projectId && f.GitHubPRNumber != null)
            .ToListAsync();

        var existingPrNumbers = existingFeatures
            .Where(f => f.GitHubPRNumber.HasValue)
            .Select(f => f.GitHubPRNumber!.Value)
            .ToHashSet();

        foreach (var pr in allPrs)
        {
            try
            {
                if (existingPrNumbers.Contains(pr.Number))
                {
                    // Update existing feature
                    var feature = existingFeatures.First(f => f.GitHubPRNumber == pr.Number);
                    var newStatus = MapPrStateToFeatureStatus(pr);

                    if (feature.Status != newStatus)
                    {
                        feature.Status = newStatus;
                        feature.UpdatedAt = DateTime.UtcNow;
                        result.Updated++;
                    }
                }
                else
                {
                    // Import as new feature
                    var feature = new Feature
                    {
                        ProjectId = projectId,
                        Title = pr.Title,
                        Description = pr.Body,
                        BranchName = pr.BranchName,
                        GitHubPRNumber = pr.Number,
                        Status = MapPrStateToFeatureStatus(pr),
                        CreatedAt = pr.CreatedAt
                    };

                    db.Features.Add(feature);
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
        logger.LogInformation("PR sync completed: {Imported} imported, {Updated} updated, {Errors} errors",
            result.Imported, result.Updated, result.Errors.Count);
        return result;
    }

    public async Task<bool> LinkPullRequestAsync(string featureId, int prNumber)
    {
        var feature = await db.Features.FindAsync(featureId);
        if (feature == null)
        {
            logger.LogWarning("Cannot link PR: feature {FeatureId} not found", featureId);
            return false;
        }

        logger.LogInformation("Linking PR #{PrNumber} to feature {FeatureId}", prNumber, featureId);
        feature.GitHubPRNumber = prNumber;
        feature.UpdatedAt = DateTime.UtcNow;
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

    private static GitHubPullRequest MapPullRequest(PullRequest pr)
    {
        return new GitHubPullRequest
        {
            Number = pr.Number,
            Title = pr.Title,
            Body = pr.Body,
            State = pr.State.StringValue,
            Merged = pr.Merged,
            BranchName = pr.Head.Ref,
            HtmlUrl = pr.HtmlUrl,
            CreatedAt = pr.CreatedAt.UtcDateTime,
            MergedAt = pr.MergedAt?.UtcDateTime,
            ClosedAt = pr.ClosedAt?.UtcDateTime
        };
    }

    private static FeatureStatus MapPrStateToFeatureStatus(GitHubPullRequest pr)
    {
        if (pr.Merged)
        {
            return FeatureStatus.Merged;
        }

        if (pr.State == "closed")
        {
            return FeatureStatus.Cancelled;
        }

        return FeatureStatus.ReadyForReview;
    }
}
