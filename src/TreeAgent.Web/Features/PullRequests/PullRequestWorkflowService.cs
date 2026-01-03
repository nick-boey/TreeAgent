using Microsoft.EntityFrameworkCore;
using Octokit;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using Project = TreeAgent.Web.Features.PullRequests.Data.Entities.Project;

namespace TreeAgent.Web.Features.PullRequests;

/// <summary>
/// Service for managing PR workflow including status tracking, time calculation, and rebasing.
/// </summary>
public class PullRequestWorkflowService(
    TreeAgentDbContext db,
    ICommandRunner commandRunner,
    IConfiguration configuration,
    IGitHubClientWrapper githubClient)
{
    private string? GetGitHubToken()
    {
        return configuration["GITHUB_TOKEN"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    private void ConfigureClient()
    {
        var token = GetGitHubToken();
        if (githubClient is GitHubClientWrapper wrapper)
        {
            wrapper.SetToken(token);
        }
    }

    #region 2.1 Past PR Synchronization

    /// <summary>
    /// Gets all merged PRs with their calculated time values.
    /// Time is calculated based on merge order: most recent = 0, older = negative.
    /// </summary>
    public async Task<List<PullRequestWithTime>> GetMergedPullRequestsWithTimeAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest { State = ItemStateFilter.Closed };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            // Filter to only merged PRs and map to PullRequestInfo
            var mergedPRs = prs
                .Where(pr => pr.Merged)
                .Select(MapToPullRequestInfo)
                .ToList();

            // Calculate time values
            var times = PullRequestTimeCalculator.CalculateTimesForMergedPRs(mergedPRs);

            // Return ordered by merge time (most recent first)
            return mergedPRs
                .OrderByDescending(pr => pr.MergedAt)
                .Select(pr => new PullRequestWithTime(pr, times.GetValueOrDefault(pr.Number, int.MinValue)))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Gets all closed (not merged) PRs with their calculated time values.
    /// </summary>
    public async Task<List<PullRequestWithTime>> GetClosedPullRequestsWithTimeAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest { State = ItemStateFilter.Closed };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            // Separate merged and closed PRs
            var allPRs = prs.Select(MapToPullRequestInfo).ToList();
            var mergedPRs = allPRs.Where(pr => pr.Status == PullRequestStatus.Merged).ToList();
            var closedPRs = allPRs.Where(pr => pr.Status == PullRequestStatus.Closed).ToList();

            // Calculate time for closed PRs relative to merged PRs
            var result = new List<PullRequestWithTime>();
            foreach (var closedPR in closedPRs.OrderByDescending(pr => pr.ClosedAt))
            {
                var time = PullRequestTimeCalculator.CalculateTimeForClosedPR(closedPR, mergedPRs);
                result.Add(new PullRequestWithTime(closedPR, time));
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    #endregion

    #region 2.2 Current PR Status Tracking

    /// <summary>
    /// Gets all open PRs with their calculated status based on review state and CI checks.
    /// All open PRs have time = 1.
    /// </summary>
    public async Task<List<PullRequestWithStatus>> GetOpenPullRequestsWithStatusAsync(string projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest { State = ItemStateFilter.Open };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            var result = new List<PullRequestWithStatus>();

            foreach (var pr in prs)
            {
                var prInfo = MapToPullRequestInfo(pr);
                var status = await DetermineStatusAsync(project.GitHubOwner, project.GitHubRepo, pr);
                result.Add(new PullRequestWithStatus(prInfo, status, 1)); // All open PRs have t=1
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Determines the status of a PR based on its state, reviews, and CI checks.
    /// </summary>
    private async Task<PullRequestStatus> DetermineStatusAsync(string owner, string repo, PullRequest pr)
    {
        // Draft PRs are always InProgress
        if (pr.Draft)
        {
            return PullRequestStatus.InProgress;
        }

        // Get CI check status
        CombinedCommitStatus? commitStatus = null;
        try
        {
            commitStatus = await githubClient.GetCombinedCommitStatusAsync(owner, repo, pr.Head.Sha);
        }
        catch
        {
            // Ignore errors getting commit status
        }

        // Check if CI is failing
        if (commitStatus?.State == CommitState.Failure || commitStatus?.State == CommitState.Error)
        {
            return PullRequestStatus.ChecksFailing;
        }

        // Get reviews
        IReadOnlyList<PullRequestReview>? reviews = null;
        try
        {
            reviews = await githubClient.GetPullRequestReviewsAsync(owner, repo, pr.Number);
        }
        catch
        {
            // Ignore errors getting reviews
        }

        // Check review state
        if (reviews != null && reviews.Count > 0)
        {
            // Get the most recent review from each reviewer
            var latestReviews = reviews
                .GroupBy(r => r.User?.Id)
                .Select(g => g.OrderByDescending(r => r.SubmittedAt).First())
                .ToList();

            // Check if any reviewer requested changes
            if (latestReviews.Any(r => r.State.Value == PullRequestReviewState.ChangesRequested))
            {
                return PullRequestStatus.InProgress;
            }

            // Check if approved
            if (latestReviews.Any(r => r.State.Value == PullRequestReviewState.Approved))
            {
                // Only ready for merging if CI passes
                if (commitStatus?.State == CommitState.Success)
                {
                    return PullRequestStatus.ReadyForMerging;
                }
            }
        }

        // If CI passes and no blocking reviews, ready for review
        if (commitStatus?.State == CommitState.Success)
        {
            return PullRequestStatus.ReadyForReview;
        }

        // Default to InProgress
        return PullRequestStatus.InProgress;
    }

    #endregion

    #region 2.3 Automatic Rebasing

    /// <summary>
    /// Rebases all open PR branches onto the latest main branch.
    /// </summary>
    public async Task<RebaseResult> RebaseAllOpenPRsAsync(string projectId)
    {
        var result = new RebaseResult();

        var project = await db.Projects.FindAsync(projectId);
        if (project == null)
        {
            result.Errors.Add("Project not found");
            return result;
        }

        // Get all features with open PRs
        var features = await db.Features
            .Where(f => f.ProjectId == projectId &&
                       f.BranchName != null &&
                       (f.Status == FeatureStatus.InDevelopment || f.Status == FeatureStatus.ReadyForReview))
            .ToListAsync();

        // Fetch latest from origin
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", project.LocalPath);
        if (!fetchResult.Success)
        {
            result.Errors.Add($"Failed to fetch from origin: {fetchResult.Error}");
            return result;
        }

        foreach (var feature in features)
        {
            if (string.IsNullOrEmpty(feature.BranchName))
                continue;

            var rebaseSuccess = await RebaseBranchAsync(project, feature, result);
            if (rebaseSuccess)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
            }
        }

        return result;
    }

    private async Task<bool> RebaseBranchAsync(Project project, Feature feature, RebaseResult result)
    {
        var workingDir = feature.WorktreePath ?? project.LocalPath;
        var baseBranch = project.DefaultBranch ?? "main";

        // Perform rebase
        var rebaseResult = await commandRunner.RunAsync(
            "git",
            $"rebase origin/{baseBranch}",
            workingDir);

        if (!rebaseResult.Success)
        {
            // Abort the failed rebase
            await commandRunner.RunAsync("git", "rebase --abort", workingDir);

            result.Conflicts.Add(new RebaseConflict(
                feature.BranchName!,
                feature.Id,
                rebaseResult.Error ?? "Rebase failed"));

            return false;
        }

        // Push the rebased branch with force-with-lease for safety
        var pushResult = await commandRunner.RunAsync(
            "git",
            $"push --force-with-lease origin {feature.BranchName}",
            workingDir);

        if (!pushResult.Success)
        {
            result.Errors.Add($"Failed to push {feature.BranchName}: {pushResult.Error}");
            return false;
        }

        return true;
    }

    #endregion

    #region Helpers

    private static PullRequestInfo MapToPullRequestInfo(PullRequest pr)
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
            // Will be refined by DetermineStatusAsync for open PRs
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

    #endregion
}
