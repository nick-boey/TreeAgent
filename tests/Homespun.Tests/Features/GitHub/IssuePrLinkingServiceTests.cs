using Homespun.Features.Beads.Services;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class IssuePrLinkingServiceTests
{
    private TestDataStore _dataStore = null!;
    private Mock<IBeadsDatabaseService> _mockBeadsDatabaseService = null!;
    private Mock<ILogger<IssuePrLinkingService>> _mockLogger = null!;
    private IssuePrLinkingService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _mockBeadsDatabaseService = new Mock<IBeadsDatabaseService>();
        _mockLogger = new Mock<ILogger<IssuePrLinkingService>>();
        _service = new IssuePrLinkingService(_dataStore, _mockBeadsDatabaseService.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "test-repo",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);
        return project;
    }

    private async Task<PullRequest> CreateTestPullRequest(string projectId, string? branchName = "feature/test", int? prNumber = null)
    {
        var pullRequest = new PullRequest
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

    #region LinkPullRequestToIssueAsync Tests

    [Test]
    public async Task LinkPullRequestToIssueAsync_WithValidInputs_SetsBeadsIssueIdAndAddsLabel()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);

        _mockBeadsDatabaseService
            .Setup(b => b.AddLabelAsync(project.LocalPath, "hsp-123", "hsp:pr-42"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.LinkPullRequestToIssueAsync(project.Id, pr.Id, "hsp-123", 42);

        // Assert
        Assert.That(result, Is.True);

        var updatedPr = _dataStore.GetPullRequest(pr.Id);
        Assert.That(updatedPr!.BeadsIssueId, Is.EqualTo("hsp-123"));

        _mockBeadsDatabaseService.Verify(b => b.AddLabelAsync(project.LocalPath, "hsp-123", "hsp:pr-42"), Times.Once);
    }

    [Test]
    public async Task LinkPullRequestToIssueAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Act
        var result = await _service.LinkPullRequestToIssueAsync("nonexistent", "pr-id", "hsp-123", 42);

        // Assert
        Assert.That(result, Is.False);
        _mockBeadsDatabaseService.Verify(b => b.AddLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LinkPullRequestToIssueAsync_WithNonExistentPullRequest_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.LinkPullRequestToIssueAsync(project.Id, "nonexistent", "hsp-123", 42);

        // Assert
        Assert.That(result, Is.False);
        _mockBeadsDatabaseService.Verify(b => b.AddLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LinkPullRequestToIssueAsync_WhenAlreadyLinked_DoesNotAddLabelAgain()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);
        pr.BeadsIssueId = "hsp-123"; // Already linked
        await _dataStore.UpdatePullRequestAsync(pr);

        // Act
        var result = await _service.LinkPullRequestToIssueAsync(project.Id, pr.Id, "hsp-123", 42);

        // Assert
        Assert.That(result, Is.True);
        _mockBeadsDatabaseService.Verify(b => b.AddLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LinkPullRequestToIssueAsync_WhenLabelAddFails_StillLinksLocally()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);

        _mockBeadsDatabaseService
            .Setup(b => b.AddLabelAsync(project.LocalPath, "hsp-123", "hsp:pr-42"))
            .ReturnsAsync(false);

        // Act
        var result = await _service.LinkPullRequestToIssueAsync(project.Id, pr.Id, "hsp-123", 42);

        // Assert - should still succeed locally even if label add fails
        Assert.That(result, Is.True);

        var updatedPr = _dataStore.GetPullRequest(pr.Id);
        Assert.That(updatedPr!.BeadsIssueId, Is.EqualTo("hsp-123"));
    }

    #endregion

    #region TryLinkByBranchNameAsync Tests

    [Test]
    public async Task TryLinkByBranchNameAsync_WithIssueIdInBranch_LinksAndReturnsIssueId()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);

        _mockBeadsDatabaseService
            .Setup(b => b.AddLabelAsync(project.LocalPath, "hsp-123", "hsp:pr-42"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.TryLinkByBranchNameAsync(project.Id, pr.Id);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-123"));

        var updatedPr = _dataStore.GetPullRequest(pr.Id);
        Assert.That(updatedPr!.BeadsIssueId, Is.EqualTo("hsp-123"));
    }

    [Test]
    public async Task TryLinkByBranchNameAsync_WithNoIssueIdInBranch_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "feature/no-issue", 42);

        // Act
        var result = await _service.TryLinkByBranchNameAsync(project.Id, pr.Id);

        // Assert
        Assert.That(result, Is.Null);

        var updatedPr = _dataStore.GetPullRequest(pr.Id);
        Assert.That(updatedPr!.BeadsIssueId, Is.Null);
    }

    [Test]
    public async Task TryLinkByBranchNameAsync_WhenAlreadyLinked_ReturnsExistingIssueId()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);
        pr.BeadsIssueId = "hsp-123";
        await _dataStore.UpdatePullRequestAsync(pr);

        // Act
        var result = await _service.TryLinkByBranchNameAsync(project.Id, pr.Id);

        // Assert
        Assert.That(result, Is.EqualTo("hsp-123"));
        _mockBeadsDatabaseService.Verify(b => b.AddLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TryLinkByBranchNameAsync_WithNoPrNumber_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", prNumber: null);

        // Act
        var result = await _service.TryLinkByBranchNameAsync(project.Id, pr.Id);

        // Assert - can't link without a PR number for the label
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryLinkByBranchNameAsync_WithNullBranch_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, branchName: null, prNumber: 42);

        // Act
        var result = await _service.TryLinkByBranchNameAsync(project.Id, pr.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region CloseLinkedIssueAsync Tests

    [Test]
    public async Task CloseLinkedIssueAsync_WithLinkedIssue_ClosesIssue()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "issues/feature/test+hsp-123", 42);
        pr.BeadsIssueId = "hsp-123";
        await _dataStore.UpdatePullRequestAsync(pr);

        _mockBeadsDatabaseService
            .Setup(b => b.CloseIssueAsync(project.LocalPath, "hsp-123", It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CloseLinkedIssueAsync(project.Id, pr.Id, "PR #42 merged");

        // Assert
        Assert.That(result, Is.True);
        _mockBeadsDatabaseService.Verify(b => b.CloseIssueAsync(project.LocalPath, "hsp-123", "PR #42 merged"), Times.Once);
    }

    [Test]
    public async Task CloseLinkedIssueAsync_WithNoLinkedIssue_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();
        var pr = await CreateTestPullRequest(project.Id, "feature/test", 42);

        // Act
        var result = await _service.CloseLinkedIssueAsync(project.Id, pr.Id, "PR #42 merged");

        // Assert
        Assert.That(result, Is.False);
        _mockBeadsDatabaseService.Verify(b => b.CloseIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CloseLinkedIssueAsync_WithNonExistentPullRequest_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.CloseLinkedIssueAsync(project.Id, "nonexistent", "reason");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
