using Homespun.Features.Beads.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for GraphService refresh functionality.
/// </summary>
[TestFixture]
public class GraphServiceTests
{
    private GraphService _graphService;
    private Mock<ProjectService> _mockProjectService;
    private Mock<IDataStore> _mockDataStore;
    private Mock<IBeadsDatabaseService> _mockBeadsDatabaseService;
    private Mock<IGitHubService> _mockGitHubService;
    private Mock<ILogger<GraphService>> _mockLogger;

    [SetUp]
    public void SetUp()
    {
        _mockProjectService = new Mock<ProjectService>();
        _mockDataStore = new Mock<IDataStore>();
        _mockBeadsDatabaseService = new Mock<IBeadsDatabaseService>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockLogger = new Mock<ILogger<GraphService>>();

        _graphService = new GraphService(
            _mockProjectService.Object,
            _mockDataStore.Object,
            _mockBeadsDatabaseService.Object,
            _mockGitHubService.Object,
            _mockLogger.Object);
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

        _mockProjectService.Setup(p => p.GetByIdAsync(projectId)).ReturnsAsync(project);
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(projectId)).Returns(new List<PullRequest>());
        _mockGitHubService.Setup(g => g.GetOpenPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(g => g.GetClosedPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());

        // Return empty lists for beads issues
        _mockBeadsDatabaseService.Setup(b => b.ListIssues(project.LocalPath, It.IsAny<BeadsListOptions>()))
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.Setup(b => b.GetDependencies(project.LocalPath))
            .Returns(new List<Features.Beads.Data.BeadsDependency>());

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        _mockProjectService.Verify(p => p.GetByIdAsync(projectId), Times.Once);
        _mockGitHubService.Verify(g => g.GetOpenPullRequestsAsync(projectId), Times.Once);
        _mockGitHubService.Verify(g => g.GetClosedPullRequestsAsync(projectId), Times.Once);
        _mockBeadsDatabaseService.Verify(b => b.ListIssues(project.LocalPath, It.IsAny<BeadsListOptions>()), Times.AtLeastOnce);
        _mockBeadsDatabaseService.Verify(b => b.GetDependencies(project.LocalPath), Times.Once);
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

        _mockProjectService.Setup(p => p.GetByIdAsync(projectId)).ReturnsAsync(project);
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(projectId)).Returns(new List<PullRequest>());

        // Create 5 closed PRs with different merge dates
        var closedPRs = new List<PullRequestInfo>
        {
            new() { Number = 1, Title = "PR 1", MergedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Number = 2, Title = "PR 2", MergedAt = DateTime.UtcNow.AddDays(-4) },
            new() { Number = 3, Title = "PR 3", MergedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Number = 4, Title = "PR 4", MergedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Number = 5, Title = "PR 5", MergedAt = DateTime.UtcNow.AddDays(-1) }
        };

        _mockGitHubService.Setup(g => g.GetOpenPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(g => g.GetClosedPullRequestsAsync(projectId)).ReturnsAsync(closedPRs);

        _mockBeadsDatabaseService.Setup(b => b.ListIssues(project.LocalPath, It.IsAny<BeadsListOptions>()))
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.Setup(b => b.GetDependencies(project.LocalPath))
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

        _mockProjectService.Setup(p => p.GetByIdAsync(projectId)).ReturnsAsync(project);
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(projectId)).Returns(new List<PullRequest> { localPR });
        _mockGitHubService.Setup(g => g.GetOpenPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo> { githubPR });
        _mockGitHubService.Setup(g => g.GetClosedPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());

        _mockBeadsDatabaseService.Setup(b => b.ListIssues(project.LocalPath, It.IsAny<BeadsListOptions>()))
            .Returns(new List<Features.Beads.Data.BeadsIssue>());
        _mockBeadsDatabaseService.Setup(b => b.GetDependencies(project.LocalPath))
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
        _mockProjectService.Setup(p => p.GetByIdAsync(projectId)).ReturnsAsync((Project?)null);

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

        _mockProjectService.Setup(p => p.GetByIdAsync(projectId)).ReturnsAsync(project);
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(projectId)).Returns(new List<PullRequest>());
        _mockGitHubService.Setup(g => g.GetOpenPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(g => g.GetClosedPullRequestsAsync(projectId)).ReturnsAsync(new List<PullRequestInfo>());

        // Simulate beads service throwing an exception
        _mockBeadsDatabaseService.Setup(b => b.ListIssues(project.LocalPath, It.IsAny<BeadsListOptions>()))
            .Throws(new Exception("Beads service error"));

        // Act
        var result = await _graphService.BuildGraphJsonAsync(projectId, 5);

        // Assert - Should still return a valid graph JSON even if beads service fails
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("gitGraph"));
    }
}
