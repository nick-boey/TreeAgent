using Homespun.Features.Commands;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Octokit;
using TrackedPullRequest = Homespun.Features.PullRequests.Data.Entities.PullRequest;

namespace Homespun.Features.GitHub;

public class GitHubService(
    IDataStore dataStore,
    ICommandRunner commandRunner,
    IConfiguration configuration,
    IGitHubClientWrapper githubClient,
    IIssuePrLinkingService issuePrLinkingService,
    ILogger<GitHubService> logger)
    : IGitHubService
{
    private string? GetGitHubToken()
    {
        // Priority: 1. User secrets (GitHub:Token), 2. Config/env var (GITHUB_TOKEN), 3. Direct env var
        return configuration["GitHub:Token"]
            ?? configuration["GITHUB_TOKEN"]
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    public async Task<string?> GetDefaultBranchAsync(string owner, string repo)
    {
        var token = GetGitHubToken();
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Cannot get default branch: GITHUB_TOKEN not found");
            return null;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching repository info for {Owner}/{Repo}", owner, repo);
            var repository = await githubClient.GetRepositoryAsync(owner, repo);
            logger.LogDebug("Default branch for {Owner}/{Repo} is {DefaultBranch}", owner, repo, repository.DefaultBranch);
            return repository.DefaultBranch;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get repository info for {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    public Task<bool> IsConfiguredAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogDebug("GitHub not configured: project {ProjectId} not found", projectId);
            return Task.FromResult(false);
        }

        var token = GetGitHubToken();
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("GitHub not configured for project {ProjectId}: GITHUB_TOKEN not found in configuration or environment", projectId);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task<List<PullRequestInfo>> GetOpenPullRequestsAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot fetch open PRs: project {ProjectId} not found", projectId);
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
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot fetch closed PRs: project {ProjectId} not found", projectId);
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
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot fetch PR #{PrNumber}: project {ProjectId} not found", prNumber, projectId);
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
        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot create PR: pull request {PullRequestId} not found", pullRequestId);
            return null;
        }

        var project = dataStore.GetProject(pullRequest.ProjectId);
        if (project == null)
        {
            logger.LogWarning("Cannot create PR: project {ProjectId} not found", pullRequest.ProjectId);
            return null;
        }

        if (string.IsNullOrEmpty(pullRequest.BranchName))
        {
            logger.LogWarning("Cannot create PR: branch not set for pull request {PullRequestId}", pullRequestId);
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
            await dataStore.UpdatePullRequestAsync(pullRequest);

            // Try to link to beads issue by branch name
            var linkedIssueId = await issuePrLinkingService.TryLinkByBranchNameAsync(projectId, pullRequestId);
            if (!string.IsNullOrEmpty(linkedIssueId))
            {
                logger.LogInformation("Linked PR #{PrNumber} to beads issue {IssueId}", pr.Number, linkedIssueId);
            }

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
        var project = dataStore.GetProject(projectId);
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

        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot sync PRs: project {ProjectId} not found", projectId);
            result.Errors.Add("Project not found");
            return result;
        }

        logger.LogInformation("Starting PR sync for {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);

        // Only fetch open PRs - closed/merged PRs should be retrieved from GitHub when needed
        var openPrs = await GetOpenPullRequestsAsync(projectId);
        var openPrNumbers = openPrs.Select(pr => pr.Number).ToHashSet();

        // Get existing tracked pull requests
        var existingPullRequests = dataStore.GetPullRequestsByProject(projectId)
            .Where(pr => pr.GitHubPRNumber != null)
            .ToList();

        // Remove PRs that are no longer open on GitHub
        foreach (var pr in existingPullRequests)
        {
            if (pr.GitHubPRNumber.HasValue && !openPrNumbers.Contains(pr.GitHubPRNumber.Value))
            {
                logger.LogInformation("Removing closed/merged PR #{PrNumber} from local tracking", pr.GitHubPRNumber);
                
                // Track removed PR info for closing linked issues
                result.RemovedPrs.Add(new RemovedPrInfo
                {
                    PullRequestId = pr.Id,
                    BeadsIssueId = pr.BeadsIssueId,
                    GitHubPrNumber = pr.GitHubPRNumber
                });
                
                await dataStore.RemovePullRequestAsync(pr.Id);
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
                    await dataStore.UpdatePullRequestAsync(pullRequest);
                    
                    // Try to link to beads issue if not already linked (backfill)
                    if (string.IsNullOrEmpty(pullRequest.BeadsIssueId))
                    {
                        await issuePrLinkingService.TryLinkByBranchNameAsync(projectId, pullRequest.Id);
                    }
                    
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

                    await dataStore.AddPullRequestAsync(pullRequest);
                    
                    // Try to link to beads issue by branch name
                    await issuePrLinkingService.TryLinkByBranchNameAsync(projectId, pullRequest.Id);
                    
                    result.Imported++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync PR #{PrNumber}", pr.Number);
                result.Errors.Add($"PR #{pr.Number}: {ex.Message}");
            }
        }

        logger.LogInformation("PR sync completed: {Imported} imported, {Updated} updated, {Removed} removed, {Errors} errors",
            result.Imported, result.Updated, result.Removed, result.Errors.Count);
        return result;
    }

    public async Task<bool> LinkPullRequestAsync(string pullRequestId, int prNumber)
    {
        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null)
        {
            logger.LogWarning("Cannot link PR: pull request {PullRequestId} not found", pullRequestId);
            return false;
        }

        logger.LogInformation("Linking PR #{PrNumber} to pull request {PullRequestId}", prNumber, pullRequestId);
        pullRequest.GitHubPRNumber = prNumber;
        pullRequest.UpdatedAt = DateTime.UtcNow;
        await dataStore.UpdatePullRequestAsync(pullRequest);

        return true;
    }

    public async Task<ReviewSummary> GetPullRequestReviewsAsync(string projectId, int prNumber)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot fetch reviews: project {ProjectId} not found", projectId);
            return new ReviewSummary();
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching reviews for PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            
            var reviews = await githubClient.GetPullRequestReviewsAsync(project.GitHubOwner, project.GitHubRepo, prNumber);
            var reviewComments = await githubClient.GetPullRequestReviewCommentsAsync(project.GitHubOwner, project.GitHubRepo, prNumber);
            
            var summary = new ReviewSummary
            {
                TotalReviews = reviews.Count,
                Reviews = reviews.Select(r => new PullRequestReviewInfo
                {
                    Id = r.Id,
                    User = r.User.Login,
                    State = r.State.StringValue,
                    Body = r.Body,
                    SubmittedAt = r.SubmittedAt.UtcDateTime
                }).ToList()
            };

            summary.Approvals = summary.Reviews.Count(r => r.IsApproval);
            summary.ChangesRequested = summary.Reviews.Count(r => r.IsChangesRequested);
            summary.Comments = reviewComments.Count;
            summary.LastReviewAt = summary.Reviews.MaxBy(r => r.SubmittedAt)?.SubmittedAt;

            logger.LogInformation(
                "PR #{PrNumber} has {TotalReviews} reviews: {Approvals} approvals, {ChangesRequested} changes requested, {Comments} comments",
                prNumber, summary.TotalReviews, summary.Approvals, summary.ChangesRequested, summary.Comments);

            return summary;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch reviews for PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            return new ReviewSummary();
        }
    }

    public async Task<bool> MergePullRequestAsync(string projectId, int prNumber, string? commitMessage = null)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot merge PR: project {ProjectId} not found", projectId);
            return false;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Merging PR #{PrNumber} in {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            
            var merge = new MergePullRequest
            {
                CommitMessage = commitMessage,
                MergeMethod = PullRequestMergeMethod.Squash
            };

            var result = await githubClient.MergePullRequestAsync(project.GitHubOwner, project.GitHubRepo, prNumber, merge);
            
            if (result.Merged)
            {
                logger.LogInformation("Successfully merged PR #{PrNumber} with SHA {Sha}", prNumber, result.Sha);
                return true;
            }
            else
            {
                logger.LogWarning("PR #{PrNumber} was not merged: {Message}", prNumber, result.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to merge PR #{PrNumber} in {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            return false;
        }
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
