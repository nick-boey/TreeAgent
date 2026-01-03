using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Octokit;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using Project = TreeAgent.Web.Features.PullRequests.Data.Entities.Project;

namespace TreeAgent.Web.Tests.Features.PullRequests.Services;

[TestFixture]
public class GitHubServiceTests
{
    private TreeAgentDbContext _db = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private Mock<ILogger<GitHubService>> _mockLogger = null!;
    private GitHubService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);
        _mockRunner = new Mock<ICommandRunner>();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();
        _mockLogger = new Mock<ILogger<GitHubService>>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _service = new GitHubService(_db, _mockRunner.Object, _mockConfig.Object, _mockGitHubClient.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    private async Task<Project> CreateTestProject(bool withGitHub = true)
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = "/test/path",
            GitHubOwner = withGitHub ? "test-owner" : null,
            GitHubRepo = withGitHub ? "test-repo" : null,
            DefaultBranch = "main"
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<Feature> CreateTestFeature(string projectId, string? branchName = "feature/test", int? prNumber = null)
    {
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = "Test Feature",
            Description = "Test Description",
            BranchName = branchName,
            GitHubPRNumber = prNumber,
            Status = FeatureStatus.InDevelopment
        };

        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return feature;
    }

    [Test]
    public async Task IsConfigured_WithGitHubSettings_ReturnsTrue()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.IsConfiguredAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsConfigured_WithoutGitHubSettings_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject(withGitHub: false);

        // Act
        var result = await _service.IsConfiguredAsync(project.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsConfigured_WithoutToken_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create a new mock config that returns null for token
        var noTokenConfig = new Mock<IConfiguration>();
        noTokenConfig.Setup(c => c["GITHUB_TOKEN"]).Returns((string?)null);

        // Create a new service with the no-token config
        var service = new GitHubService(_db, _mockRunner.Object, noTokenConfig.Object, _mockGitHubClient.Object, _mockLogger.Object);

        // Clear environment variable for this test (save and restore)
        var originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        try
        {
            // Act
            var result = await service.IsConfiguredAsync(project.Id);

            // Assert
            Assert.That(result, Is.False);
        }
        finally
        {
            // Restore environment variable
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalToken);
        }
    }

    [Test]
    public async Task IsConfigured_ProjectNotFound_ReturnsFalse()
    {
        // Act
        var result = await _service.IsConfiguredAsync("nonexistent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetOpenPullRequests_ReturnsOpenPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "PR 1", ItemState.Open, "feature/one"),
            CreateMockPullRequest(2, "PR 2", ItemState.Open, "feature/two")
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetOpenPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Title, Is.EqualTo("PR 1"));
        Assert.That(result[1].Title, Is.EqualTo("PR 2"));
    }

    [Test]
    public async Task GetOpenPullRequests_ProjectNotConfigured_ReturnsEmpty()
    {
        // Arrange
        var project = await CreateTestProject(withGitHub: false);

        // Act
        var result = await _service.GetOpenPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetClosedPullRequests_ReturnsClosedPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "PR 1", ItemState.Closed, "feature/one", merged: true)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetClosedPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Merged, Is.True);
    }

    [Test]
    public async Task GetPullRequest_ReturnsSpecificPR()
    {
        // Arrange
        var project = await CreateTestProject();
        var mockPr = CreateMockPullRequest(42, "Specific PR", ItemState.Open, "feature/specific");

        _mockGitHubClient.Setup(c => c.GetPullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            42))
            .ReturnsAsync(mockPr);

        // Act
        var result = await _service.GetPullRequestAsync(project.Id, 42);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo(42));
        Assert.That(result.Title, Is.EqualTo("Specific PR"));
    }

    [Test]
    public async Task GetPullRequest_NotFound_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockGitHubClient.Setup(c => c.GetPullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            999))
            .ThrowsAsync(new NotFoundException("Not found", System.Net.HttpStatusCode.NotFound));

        // Act
        var result = await _service.GetPullRequestAsync(project.Id, 999);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task PushBranch_Success_ReturnsTrue()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockRunner.Setup(r => r.RunAsync("git", "push -u origin \"feature/test\"", project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.PushBranchAsync(project.Id, "feature/test");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task PushBranch_Failure_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "Push failed" });

        // Act
        var result = await _service.PushBranchAsync(project.Id, "feature/test");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreatePullRequest_Success_CreatesPRAndUpdatesFeature()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });

        var mockPr = CreateMockPullRequest(123, feature.Title, ItemState.Open, feature.BranchName!);
        _mockGitHubClient.Setup(c => c.CreatePullRequestAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<NewPullRequest>()))
            .ReturnsAsync(mockPr);

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo(123));

        // Verify feature was updated
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.GitHubPRNumber, Is.EqualTo(123));
        Assert.That(updatedFeature.Status, Is.EqualTo(FeatureStatus.ReadyForReview));
    }

    [Test]
    public async Task CreatePullRequest_PushFails_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreatePullRequest_NoBranch_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id, branchName: null);

        // Act
        var result = await _service.CreatePullRequestAsync(project.Id, feature.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SyncPullRequests_ImportsNewPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var openPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "New Feature", ItemState.Open, "feature/new")
        };
        var closedPrs = new List<PullRequest>();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(closedPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Imported, Is.EqualTo(1));
        Assert.That(result.Updated, Is.EqualTo(0));
        Assert.That(result.Errors, Is.Empty);

        var features = await _db.Features.Where(f => f.ProjectId == project.Id).ToListAsync();
        Assert.That(features, Has.Count.EqualTo(1));
        Assert.That(features[0].Title, Is.EqualTo("New Feature"));
        Assert.That(features[0].GitHubPRNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncPullRequests_UpdatesExistingFeatures()
    {
        // Arrange
        var project = await CreateTestProject();
        var existingFeature = await CreateTestFeature(project.Id, "feature/existing", prNumber: 1);
        existingFeature.Status = FeatureStatus.ReadyForReview;
        await _db.SaveChangesAsync();

        var openPrs = new List<PullRequest>();
        var closedPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "Existing Feature", ItemState.Closed, "feature/existing", merged: true)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(closedPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Imported, Is.EqualTo(0));
        Assert.That(result.Updated, Is.EqualTo(1));

        var updatedFeature = await _db.Features.FindAsync(existingFeature.Id);
        Assert.That(updatedFeature!.Status, Is.EqualTo(FeatureStatus.Merged));
    }

    [Test]
    public async Task SyncPullRequests_ProjectNotConfigured_ReturnsError()
    {
        // Act
        var result = await _service.SyncPullRequestsAsync("nonexistent");

        // Assert
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].ToLower(), Does.Contain("not found"));
    }

    [Test]
    public async Task LinkPullRequest_Success_UpdatesFeature()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);

        // Act
        var result = await _service.LinkPullRequestAsync(feature.Id, 42);

        // Assert
        Assert.That(result, Is.True);
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.GitHubPRNumber, Is.EqualTo(42));
    }

    [Test]
    public async Task LinkPullRequest_FeatureNotFound_ReturnsFalse()
    {
        // Act
        var result = await _service.LinkPullRequestAsync("nonexistent", 42);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SyncPullRequests_GitHubApiThrows_ReturnsEmptyButNoException()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<PullRequestRequest>()))
            .ThrowsAsync(new ApiException("Rate limit exceeded", System.Net.HttpStatusCode.Forbidden));

        // Act - should not throw
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert - returns empty result since no PRs could be fetched
        Assert.That(result.Imported, Is.EqualTo(0));
        Assert.That(result.Updated, Is.EqualTo(0));
    }

    [Test]
    public async Task GetOpenPullRequests_GitHubApiThrows_ReturnsEmptyList()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<PullRequestRequest>()))
            .ThrowsAsync(new ApiException("Unauthorized", System.Net.HttpStatusCode.Unauthorized));

        // Act
        var result = await _service.GetOpenPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetClosedPullRequests_GitHubApiThrows_ReturnsEmptyList()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<PullRequestRequest>()))
            .ThrowsAsync(new ApiException("Network error", System.Net.HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await _service.GetClosedPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task SyncPullRequests_MixedOpenAndClosed_ImportsAll()
    {
        // Arrange
        var project = await CreateTestProject();

        var openPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "Open PR", ItemState.Open, "feature/open")
        };
        var closedPrs = new List<PullRequest>
        {
            CreateMockPullRequest(2, "Merged PR", ItemState.Closed, "feature/merged", merged: true),
            CreateMockPullRequest(3, "Closed PR", ItemState.Closed, "feature/closed", merged: false)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(closedPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Imported, Is.EqualTo(3));
        Assert.That(result.Updated, Is.EqualTo(0));
        Assert.That(result.Errors, Is.Empty);

        var features = await _db.Features.Where(f => f.ProjectId == project.Id).ToListAsync();
        Assert.That(features, Has.Count.EqualTo(3));

        var openFeature = features.First(f => f.GitHubPRNumber == 1);
        Assert.That(openFeature.Status, Is.EqualTo(FeatureStatus.ReadyForReview));

        var mergedFeature = features.First(f => f.GitHubPRNumber == 2);
        Assert.That(mergedFeature.Status, Is.EqualTo(FeatureStatus.Merged));

        var closedFeature = features.First(f => f.GitHubPRNumber == 3);
        Assert.That(closedFeature.Status, Is.EqualTo(FeatureStatus.Cancelled));
    }

    // Helper to create mock PullRequest objects
    private static PullRequest CreateMockPullRequest(int number, string title, ItemState state, string branchName, bool merged = false)
    {
        // Using reflection to create PullRequest since it has no public constructor
        var headRef = new GitReference(
            nodeId: "node1",
            url: "url",
            label: "label",
            @ref: branchName,
            sha: "sha",
            user: null,
            repository: null
        );

        return new PullRequest(
            id: number,
            nodeId: $"node-{number}",
            url: $"https://api.github.com/repos/owner/repo/pulls/{number}",
            htmlUrl: $"https://github.com/owner/repo/pull/{number}",
            diffUrl: $"https://github.com/owner/repo/pull/{number}.diff",
            patchUrl: $"https://github.com/owner/repo/pull/{number}.patch",
            issueUrl: $"https://api.github.com/repos/owner/repo/issues/{number}",
            statusesUrl: $"https://api.github.com/repos/owner/repo/statuses/sha",
            number: number,
            state: state,
            title: title,
            body: "Description",
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: state == ItemState.Closed ? DateTimeOffset.UtcNow : null,
            mergedAt: merged ? DateTimeOffset.UtcNow : null,
            head: headRef,
            @base: headRef,
            user: null,
            assignee: null,
            assignees: null,
            draft: false,
            mergeable: true,
            mergeableState: null,
            mergedBy: null,
            mergeCommitSha: null,
            comments: 0,
            commits: 1,
            additions: 10,
            deletions: 5,
            changedFiles: 2,
            milestone: null,
            locked: false,
            maintainerCanModify: null,
            requestedReviewers: null,
            requestedTeams: null,
            labels: null,
            activeLockReason: null
        );
    }
}