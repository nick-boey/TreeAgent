using Homespun.Features.Beads.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for GraphService refresh functionality.
/// </summary>
[TestFixture]
public class GraphServiceTests
{
    private GraphService _graphService;
    private IProjectService _mockProjectService;
    private IDataStore _mockDataStore;
    private IBeadsDatabaseService _mockBeadsDatabaseService;
    private IGitHubService _mockGitHubService;
    private ILogger<GraphService> _mockLogger;

    [SetUp]
    public void SetUp()
    {
        _mockProjectService = Substitute.For<IProjectService>();
        _mockDataStore = Substitute.For<IDataStore>();
        _mockBeadsDatabaseService = Substitute.For<IBeadsDatabaseService>();
        _mockGitHubService = Substitute.For<IGitHubService>();
        _mockLogger = Substitute.For<ILogger<GraphService>>();

        _graphService = new GraphService(
            _mockProjectService,
            _mockDataStore,
            _mockBeadsDatabaseService,
            _mockGitHubService,
            _mockLogger);
    }

    [Test]
    public async Task BuildGraphJsonAsync_RefreshesDataFromAllSources()
    {
        // Arrange
        var projectId = "test-project";
        var project = new Project
        {
            Id = projectId,
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        _mockProjectService.GetByIdAsync(projectId).Returns(project);
        _mockDataStore.GetPullRequestsByProject(projectId).Returns(new List<PullRequest>());
        _mockGitHubService.GetOpenPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));
        _mockGitHubService.GetClosedPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));

        // Return empty lists for beads issues
        _mockBeadsDatabaseService.ListIssues(project.LocalPath, Arg.Any<BeadsListOptions>())
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.GetDependencies(project.LocalPath)
            .Returns(new List<Features.Beads.Data.BeadsDependency>());

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        await _mockProjectService.Received(1).GetByIdAsync(projectId);
        await _mockGitHubService.Received(1).GetOpenPullRequestsAsync(projectId);
        await _mockGitHubService.Received(1).GetClosedPullRequestsAsync(projectId);
        _mockBeadsDatabaseService.Received(2).ListIssues(project.LocalPath, Arg.Any<BeadsListOptions>());
        _mockBeadsDatabaseService.Received(1).GetDependencies(project.LocalPath);
    }

    [Test]
    public async Task BuildGraphJsonAsync_IncludesOnlyRecentClosedPRs()
    {
        // Arrange
        var projectId = "test-project";
        var maxPastPRs = 3;
        var project = new Project
        {
            Id = projectId,
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        _mockProjectService.GetByIdAsync(projectId).Returns(project);
        _mockDataStore.GetPullRequestsByProject(projectId).Returns(new List<PullRequest>());

        // Create 5 closed PRs with different merge dates
        var closedPRs = new List<PullRequestInfo>
        {
            new() { Number = 1, Title = "PR 1", MergedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Number = 2, Title = "PR 2", MergedAt = DateTime.UtcNow.AddDays(-4) },
            new() { Number = 3, Title = "PR 3", MergedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Number = 4, Title = "PR 4", MergedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Number = 5, Title = "PR 5", MergedAt = DateTime.UtcNow.AddDays(-1) }
        };

        _mockGitHubService.GetOpenPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));
        _mockGitHubService.GetClosedPullRequestsAsync(projectId).Returns(Task.FromResult(closedPRs));

        _mockBeadsDatabaseService.ListIssues(project.LocalPath, Arg.Any<BeadsListOptions>())
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.GetDependencies(project.LocalPath)
            .Returns(new List<Features.Beads.Data.BeadsDependency>());

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, maxPastPRs);

        // Assert
        Assert.That(result, Is.Not.Null);
        // The graph builder should only include the 3 most recent PRs
        Assert.That(result, Does.Contain("\"5\""));  // Most recent
        Assert.That(result, Does.Contain("\"4\""));
        Assert.That(result, Does.Contain("\"3\""));
        Assert.That(result, Does.Not.Contain("\"2\""));  // Should be excluded
        Assert.That(result, Does.Not.Contain("\"1\""));  // Should be excluded
    }

    [Test]
    public async Task BuildGraphJsonAsync_CombinesLocalAndGitHubData()
    {
        // Arrange
        var projectId = "test-project";
        var project = new Project
        {
            Id = projectId,
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        var localPR = new PullRequest
        {
            Id = "local-pr-1",
            GitHubPRNumber = 10,
            Title = "Local PR"
        };

        var githubPR = new PullRequestInfo
        {
            Number = 20,
            Title = "GitHub PR"
        };

        _mockProjectService.GetByIdAsync(projectId).Returns(project);
        _mockDataStore.GetPullRequestsByProject(projectId).Returns(new List<PullRequest> { localPR });
        _mockGitHubService.GetOpenPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo> { githubPR }));
        _mockGitHubService.GetClosedPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));

        _mockBeadsDatabaseService.ListIssues(project.LocalPath, Arg.Any<BeadsListOptions>())
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.GetDependencies(project.LocalPath)
            .Returns(new List<Features.Beads.Data.BeadsDependency>());

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("\"10\""));  // Local PR number
        Assert.That(result, Does.Contain("\"20\""));  // GitHub PR number
    }

    [Test]
    public async Task BuildGraphJsonAsync_HandlesProjectNotFound()
    {
        // Arrange
        var projectId = "non-existent";
        _mockProjectService.GetByIdAsync(projectId).Returns((Project?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _graphService.BuildGraphJsonAsync(projectId, 5));

        Assert.That(ex.Message, Does.Contain($"Project {projectId} not found"));
    }

    [Test]
    public async Task BuildGraphJsonAsync_HandlesBeadsServiceErrors()
    {
        // Arrange
        var projectId = "test-project";
        var project = new Project
        {
            Id = projectId,
            LocalPath = "/test/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo"
        };

        _mockProjectService.GetByIdAsync(projectId).Returns(project);
        _mockDataStore.GetPullRequestsByProject(projectId).Returns(new List<PullRequest>());
        _mockGitHubService.GetOpenPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));
        _mockGitHubService.GetClosedPullRequestsAsync(projectId).Returns(Task.FromResult(new List<PullRequestInfo>()));

        // Simulate beads service throwing an exception
        _mockBeadsDatabaseService.ListIssues(project.LocalPath, Arg.Any<BeadsListOptions>())
            .Returns(x => throw new Exception("Beads service error"));

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, 5);

        // Assert - Should still return a valid graph JSON even if beads service fails
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("gitGraph"));
    }
}