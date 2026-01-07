using Homespun.Features.Beads.Services;
using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Homespun.Features.GitHub;

/// <summary>
/// Background service that polls GitHub for PR sync and reviews.
/// This service:
/// 1. Syncs PRs from GitHub (detects new PRs, links to issues)
/// 2. Polls for review status updates
/// 3. Closes linked issues when PRs are merged/closed
/// </summary>
public class GitHubSyncPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly GitHubSyncPollingOptions _options;
    private readonly ILogger<GitHubSyncPollingService> _logger;

    public GitHubSyncPollingService(
        IServiceScopeFactory scopeFactory,
        IHubContext<AgentHub> hubContext,
        IOptions<GitHubSyncPollingOptions> options,
        ILogger<GitHubSyncPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GitHub sync polling service started with {IntervalSeconds}s interval", _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during GitHub sync polling");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("GitHub sync polling service stopped");
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataStore = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var gitHubService = scope.ServiceProvider.GetRequiredService<IGitHubService>();
        var linkingService = scope.ServiceProvider.GetRequiredService<IIssuePrLinkingService>();

        var projects = dataStore.Projects;

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // First, sync PRs from GitHub (this will detect new PRs and link to issues)
                await SyncProjectPullRequestsAsync(project, gitHubService, linkingService, ct);

                // Then, poll for review updates
                await PollProjectReviewsAsync(project, dataStore, gitHubService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GitHub sync for project {ProjectId}", project.Id);
            }
        }
    }

    private async Task SyncProjectPullRequestsAsync(
        Project project,
        IGitHubService gitHubService,
        IIssuePrLinkingService linkingService,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        // Check if GitHub is configured for this project
        if (string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return;
        }

        var isConfigured = await gitHubService.IsConfiguredAsync(project.Id);
        if (!isConfigured)
        {
            return;
        }

        _logger.LogDebug("Syncing PRs for project {ProjectId} ({Owner}/{Repo})", project.Id, project.GitHubOwner, project.GitHubRepo);

        // SyncPullRequestsAsync now includes issue linking logic and returns info about closed PRs
        var syncResult = await gitHubService.SyncPullRequestsAsync(project.Id);

        if (syncResult.Imported > 0 || syncResult.Updated > 0 || syncResult.Removed > 0)
        {
            _logger.LogInformation(
                "PR sync for {Owner}/{Repo}: {Imported} imported, {Updated} updated, {Removed} removed",
                project.GitHubOwner, project.GitHubRepo, syncResult.Imported, syncResult.Updated, syncResult.Removed);

            // Close linked beads issues for removed (merged/closed) PRs
            // We use BeadsService directly since the PR has already been removed from the data store
            using var closeScope = _scopeFactory.CreateScope();
            var beadsService = closeScope.ServiceProvider.GetRequiredService<IBeadsService>();
            
            foreach (var removedPr in syncResult.RemovedPrs)
            {
                if (!string.IsNullOrEmpty(removedPr.BeadsIssueId))
                {
                    try
                    {
                        var reason = removedPr.GitHubPrNumber.HasValue
                            ? $"PR #{removedPr.GitHubPrNumber} merged/closed"
                            : "PR merged/closed";
                        
                        var closed = await beadsService.CloseIssueAsync(project.LocalPath, removedPr.BeadsIssueId, reason);
                        
                        if (closed)
                        {
                            _logger.LogInformation(
                                "Closed beads issue {IssueId} linked to merged/closed PR #{PrNumber}",
                                removedPr.BeadsIssueId, removedPr.GitHubPrNumber);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Failed to close beads issue {IssueId} linked to merged/closed PR #{PrNumber}",
                                removedPr.BeadsIssueId, removedPr.GitHubPrNumber);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error closing beads issue {IssueId} linked to merged/closed PR #{PrNumber}",
                            removedPr.BeadsIssueId, removedPr.GitHubPrNumber);
                    }
                }
            }

            // Broadcast sync completed event
            await _hubContext.Clients.All.SendAsync("PullRequestsSynced", project.Id, syncResult, ct);
        }
    }

    private async Task PollProjectReviewsAsync(
        Project project,
        IDataStore dataStore,
        IGitHubService gitHubService,
        CancellationToken ct)
    {
        // Only poll PRs that are in progress or awaiting review
        var pullRequests = dataStore.GetPullRequestsByProject(project.Id)
            .Where(pr => pr.GitHubPRNumber.HasValue &&
                         (pr.Status == OpenPullRequestStatus.InDevelopment ||
                          pr.Status == OpenPullRequestStatus.ReadyForReview ||
                          pr.Status == OpenPullRequestStatus.HasReviewComments))
            .ToList();

        foreach (var pr in pullRequests)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var reviews = await gitHubService.GetPullRequestReviewsAsync(project.Id, pr.GitHubPRNumber!.Value);

                // Check if there are new reviews that need attention
                var previousStatus = pr.Status;
                var needsUpdate = false;

                if (reviews.NeedsAction && pr.Status != OpenPullRequestStatus.HasReviewComments)
                {
                    pr.Status = OpenPullRequestStatus.HasReviewComments;
                    needsUpdate = true;
                }
                else if (reviews.IsApproved && pr.Status == OpenPullRequestStatus.ReadyForReview)
                {
                    // PR is approved - could auto-merge or notify
                    _logger.LogInformation("PR #{PrNumber} is approved", pr.GitHubPRNumber);
                }

                if (needsUpdate)
                {
                    pr.UpdatedAt = DateTime.UtcNow;
                    await dataStore.UpdatePullRequestAsync(pr);

                    // Broadcast status change
                    await _hubContext.Clients.All.SendAsync(
                        "PullRequestReviewsUpdated",
                        project.Id,
                        pr.Id,
                        pr.GitHubPRNumber,
                        reviews,
                        ct);

                    _logger.LogInformation(
                        "PR #{PrNumber} status changed from {PreviousStatus} to {NewStatus} due to reviews",
                        pr.GitHubPRNumber, previousStatus, pr.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling reviews for PR #{PrNumber}", pr.GitHubPRNumber);
            }
        }
    }
}

/// <summary>
/// Configuration options for GitHub sync polling.
/// </summary>
public class GitHubSyncPollingOptions
{
    public const string SectionName = "GitHubSyncPolling";

    /// <summary>
    /// Interval between polls in seconds. Default: 60.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to enable automatic response to review comments via agent.
    /// </summary>
    public bool AutoRespondEnabled { get; set; } = false;
}
