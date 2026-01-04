using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Octokit;
using Project = Homespun.Features.PullRequests.Data.Entities.Project;
using TrackedPullRequest = Homespun.Features.PullRequests.Data.Entities.PullRequest;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class GitHubServiceTests
{
    private TestDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private Mock<ILogger<GitHubService>> _mockLogger = null!;
    private GitHubService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _mockRunner = new Mock<ICommandRunner>();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();
        _mockLogger = new Mock<ILogger<GitHubService>>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _service = new GitHubService(_dataStore, _mockRunner.Object, _mockConfig.Object, _mockGitHubClient.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
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

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    private async Task<TrackedPullRequest> CreateTestPullRequest(string projectId, string? branchName = "feature/test", int? prNumber = null)
    {
        var pullRequest = new TrackedPullRequest
        {
            ProjectId = projectId,
            Title = "Test Pull Request",
            Description = "Test Description",
            BranchName = branchName,
            GitHubPRNumber = prNumber,
            Status = OpenPullRequestStatus.InDevelopment
        };

        await _dataStore.AddPullRequestAsync(pullRequest);
        return pullRequest;
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
        var service = new GitHubService(_dataStore, _mockRunner.Object, noTokenConfig.Object, _mockGitHubClient.Object, _mockLogger.Object);

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

        var mockPrs = new List<Octokit.PullRequest>
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

        var mockPrs = new List<Octokit.PullRequest>
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
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.Merged));
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
        var feature = await CreateTestPullRequest(project.Id);

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
        var updatedFeature = _dataStore.GetPullRequest(feature.Id);
        Assert.That(updatedFeature!.GitHubPRNumber, Is.EqualTo(123));
        Assert.That(updatedFeature.Status, Is.EqualTo(OpenPullRequestStatus.ReadyForReview));
    }

    [Test]
    public async Task CreatePullRequest_PushFails_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestPullRequest(project.Id);

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
        var feature = await CreateTestPullRequest(project.Id, branchName: null);

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

        var openPrs = new List<Octokit.PullRequest>
        {
            CreateMockPullRequest(1, "New Feature", ItemState.Open, "feature/new")
        };
        var closedPrs = new List<Octokit.PullRequest>();

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

        var features = _dataStore.GetPullRequestsByProject(project.Id);
        Assert.That(features, Has.Count.EqualTo(1));
        Assert.That(features[0].Title, Is.EqualTo("New Feature"));
        Assert.That(features[0].GitHubPRNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncPullRequests_UpdatesExistingOpenPullRequests()
    {
        // Arrange
        var project = await CreateTestProject();
        var existingPullRequest = await CreateTestPullRequest(project.Id, "feature/existing", prNumber: 1);
        existingPullRequest.Title = "Old Title";
        await _dataStore.UpdatePullRequestAsync(existingPullRequest);

        var openPrs = new List<Octokit.PullRequest>
        {
            CreateMockPullRequest(1, "Updated Title", ItemState.Open, "feature/existing")
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Imported, Is.EqualTo(0));
        Assert.That(result.Updated, Is.EqualTo(1));

        var updatedPullRequest = _dataStore.GetPullRequest(existingPullRequest.Id);
        Assert.That(updatedPullRequest!.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task SyncPullRequests_RemovesClosedPullRequests()
    {
        // Arrange
        var project = await CreateTestProject();
        var existingPullRequest = await CreateTestPullRequest(project.Id, "feature/existing", prNumber: 1);
        existingPullRequest.Status = OpenPullRequestStatus.ReadyForReview;
        await _dataStore.UpdatePullRequestAsync(existingPullRequest);

        // PR is no longer in open PRs list (it was closed/merged on GitHub)
        var openPrs = new List<Octokit.PullRequest>();

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Removed, Is.EqualTo(1));
        Assert.That(result.Updated, Is.EqualTo(0));

        var removedPullRequest = _dataStore.GetPullRequest(existingPullRequest.Id);
        Assert.That(removedPullRequest, Is.Null);
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
        var feature = await CreateTestPullRequest(project.Id);

        // Act
        var result = await _service.LinkPullRequestAsync(feature.Id, 42);

        // Assert
        Assert.That(result, Is.True);
        var updatedFeature = _dataStore.GetPullRequest(feature.Id);
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
    public async Task SyncPullRequests_OnlyImportsOpenPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        var openPrs = new List<Octokit.PullRequest>
        {
            CreateMockPullRequest(1, "Open PR 1", ItemState.Open, "feature/open1"),
            CreateMockPullRequest(2, "Open PR 2", ItemState.Open, "feature/open2")
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(openPrs);

        // Act
        var result = await _service.SyncPullRequestsAsync(project.Id);

        // Assert
        Assert.That(result.Imported, Is.EqualTo(2));
        Assert.That(result.Updated, Is.EqualTo(0));
        Assert.That(result.Errors, Is.Empty);

        var pullRequests = _dataStore.GetPullRequestsByProject(project.Id);
        Assert.That(pullRequests, Has.Count.EqualTo(2));
        Assert.That(pullRequests, Has.All.Matches<TrackedPullRequest>(pr => pr.Status == OpenPullRequestStatus.ReadyForReview));
    }

    // Helper to create mock Octokit PullRequest objects
    private static Octokit.PullRequest CreateMockPullRequest(int number, string title, ItemState state, string branchName, bool merged = false)
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

        return new Octokit.PullRequest(
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
