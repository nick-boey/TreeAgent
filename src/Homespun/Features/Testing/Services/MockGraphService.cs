using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGraphService that builds graph data from MockDataStore.
/// </summary>
public class MockGraphService : IGraphService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockGraphService> _logger;
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    public MockGraphService(
        IDataStore dataStore,
        ILogger<MockGraphService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraph for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project not found: {ProjectId}", projectId);
            return Task.FromResult(new Graph([], new Dictionary<string, GraphBranch>()));
        }

        // Convert stored PullRequests to PullRequestInfo (these are open PRs)
        var pullRequests = _dataStore.GetPullRequestsByProject(projectId);
        var openPrInfos = pullRequests.Select(ConvertToPullRequestInfo).ToList();

        // Add fake merged PR history to form the main trunk
        var mergedPrHistory = GetMergedPrHistory();
        var allPrInfos = mergedPrHistory.Concat(openPrInfos).ToList();

        // Add fake issues to test full timeline scope
        var fakeIssues = GetFakeIssues();

        _logger.LogDebug("[Mock] Building graph with {PrCount} PRs ({MergedCount} merged, {OpenCount} open) and {IssueCount} issues",
            allPrInfos.Count, mergedPrHistory.Count, openPrInfos.Count, fakeIssues.Count);

        // Use the existing GraphBuilder to construct the graph
        var graph = _graphBuilder.Build(allPrInfos, fakeIssues, maxPastPRs);
        return Task.FromResult(graph);
    }

    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraphJson for project {ProjectId}", projectId);

        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        return _mapper.ToJson(graph);
    }

    private static PullRequestInfo ConvertToPullRequestInfo(PullRequest pr)
    {
        return new PullRequestInfo
        {
            Number = pr.GitHubPRNumber ?? 0,
            Title = pr.Title ?? "Untitled",
            Body = pr.Description,
            Status = ConvertStatus(pr.Status),
            BranchName = pr.BranchName,
            HtmlUrl = pr.GitHubPRNumber != null
                ? $"https://github.com/mock-org/mock-repo/pull/{pr.GitHubPRNumber}"
                : null,
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
            MergedAt = null, // Open PRs are not merged
            ChecksPassing = true,
            IsApproved = pr.Status == OpenPullRequestStatus.Approved
        };
    }

    private static PullRequestStatus ConvertStatus(OpenPullRequestStatus status)
    {
        return status switch
        {
            OpenPullRequestStatus.InDevelopment => PullRequestStatus.InProgress,
            OpenPullRequestStatus.ReadyForReview => PullRequestStatus.ReadyForReview,
            OpenPullRequestStatus.HasReviewComments => PullRequestStatus.InProgress,
            OpenPullRequestStatus.Approved => PullRequestStatus.ReadyForMerging,
            _ => PullRequestStatus.InProgress
        };
    }

    /// <summary>
    /// Returns a list of fake merged PRs to form the main trunk of the timeline.
    /// Based on recent commits in the repository history.
    /// </summary>
    private static List<PullRequestInfo> GetMergedPrHistory()
    {
        var now = DateTime.UtcNow;
        return
        [
            new PullRequestInfo
            {
                Number = 97,
                Title = "Remove models page navigation entry",
                Body = "Cleanup: remove unused models page link from navigation",
                Status = PullRequestStatus.Merged,
                BranchName = "chore/remove-models-nav",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/97",
                CreatedAt = now.AddDays(-32),
                UpdatedAt = now.AddDays(-30),
                MergedAt = now.AddDays(-30),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 98,
                Title = "Fix Test Agent button and SignalR debug panel",
                Body = "Bug fix: Test Agent button now works correctly, SignalR debug panel styling improved",
                Status = PullRequestStatus.Merged,
                BranchName = "fix/test-agent-button",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/98",
                CreatedAt = now.AddDays(-27),
                UpdatedAt = now.AddDays(-25),
                MergedAt = now.AddDays(-25),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 90,
                Title = "Allow agent sessions to be resumed",
                Body = "Feature: Agent sessions can now be resumed after being stopped",
                Status = PullRequestStatus.Merged,
                BranchName = "feature/session-resume",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/90",
                CreatedAt = now.AddDays(-22),
                UpdatedAt = now.AddDays(-20),
                MergedAt = now.AddDays(-20),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 100,
                Title = "Add mock services for testing without production data",
                Body = "Feature: Mock services allow testing the UI without connecting to production APIs",
                Status = PullRequestStatus.Merged,
                BranchName = "feature/mock-services",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/100",
                CreatedAt = now.AddDays(-17),
                UpdatedAt = now.AddDays(-15),
                MergedAt = now.AddDays(-15),
                ChecksPassing = true,
                IsApproved = true
            },
            new PullRequestInfo
            {
                Number = 101,
                Title = "Fix Node.js Docker build stage for Tailwind CSS compilation",
                Body = "Bug fix: Docker build now correctly includes Node.js for Tailwind CSS processing",
                Status = PullRequestStatus.Merged,
                BranchName = "fix/docker-nodejs",
                HtmlUrl = "https://github.com/mock-org/mock-repo/pull/101",
                CreatedAt = now.AddDays(-12),
                UpdatedAt = now.AddDays(-10),
                MergedAt = now.AddDays(-10),
                ChecksPassing = true,
                IsApproved = true
            }
        ];
    }

    /// <summary>
    /// Returns a list of fake issues to populate the timeline.
    /// Includes orphan issues (grouped and ungrouped) and issues with dependencies.
    /// </summary>
    private static List<Issue> GetFakeIssues()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            // Orphan issues - grouped under "UI"
            new Issue
            {
                Id = "ISSUE-001",
                Title = "Add dark mode support",
                Description = "Implement a dark mode theme option for better accessibility and user preference",
                Type = IssueType.Feature,
                Status = IssueStatus.Next,
                Priority = 2,
                Group = "UI",
                CreatedAt = now.AddDays(-14),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-002",
                Title = "Improve mobile responsiveness",
                Description = "Ensure all pages display correctly on mobile devices and tablets",
                Type = IssueType.Task,
                Status = IssueStatus.Next,
                Priority = 3,
                Group = "UI",
                CreatedAt = now.AddDays(-12),
                LastUpdate = now.AddDays(-1)
            },

            // Orphan issue - ungrouped
            new Issue
            {
                Id = "ISSUE-003",
                Title = "Fix login timeout bug",
                Description = "Users are being logged out unexpectedly after 5 minutes of inactivity",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                Group = "",
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddHours(-6)
            },

            // Issues with dependencies - forms a chain: ISSUE-004 -> ISSUE-005 -> ISSUE-006
            new Issue
            {
                Id = "ISSUE-004",
                Title = "Design API schema",
                Description = "Define the REST API schema for the new feature endpoints",
                Type = IssueType.Task,
                Status = IssueStatus.Spec,
                Priority = 2,
                Group = "API",
                ParentIssues = [], // Root of the dependency chain
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-3)
            },
            new Issue
            {
                Id = "ISSUE-005",
                Title = "Implement API endpoints",
                Description = "Build the REST API endpoints based on the approved schema",
                Type = IssueType.Task,
                Status = IssueStatus.Next,
                Priority = 2,
                Group = "API",
                ParentIssues = ["ISSUE-004"], // Depends on ISSUE-004
                CreatedAt = now.AddDays(-9),
                LastUpdate = now.AddDays(-2)
            },
            new Issue
            {
                Id = "ISSUE-006",
                Title = "Write API documentation",
                Description = "Document all new API endpoints with examples and usage guidelines",
                Type = IssueType.Chore,
                Status = IssueStatus.Idea,
                Priority = 3,
                Group = "API",
                ParentIssues = ["ISSUE-005"], // Depends on ISSUE-005
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-1)
            }
        ];
    }
}
