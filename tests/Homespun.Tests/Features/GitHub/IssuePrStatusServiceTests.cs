using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;
using Project = Homespun.Features.PullRequests.Data.Entities.Project;
using TrackedPullRequest = Homespun.Features.PullRequests.Data.Entities.PullRequest;
using PullRequestStatus = Homespun.Features.PullRequests.PullRequestStatus;

namespace Homespun.Tests.Features.GitHub;

[TestFixture]
public class IssuePrStatusServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private PullRequestWorkflowService _workflowService = null!;
    private IssuePrStatusService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockConfig = new Mock<IConfiguration>();
        _mockGitHubClient = new Mock<IGitHubClientWrapper>();
        _mockCommandRunner = new Mock<ICommandRunner>();

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _workflowService = new PullRequestWorkflowService(
            _dataStore,
            _mockCommandRunner.Object,
            _mockConfig.Object,
            _mockGitHubClient.Object,
            new NullLogger<PullRequestWorkflowService>());

        _service = new IssuePrStatusService(
            _dataStore,
            _workflowService,
            new NullLogger<IssuePrStatusService>());
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

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_NoLinkedPr_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_LinkedPr_ReturnsStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create a tracked PR linked to an issue
        var trackedPr = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Test PR",
            BranchName = "issues/feature/test+issue-123",
            GitHubPRNumber = 42,
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Mock GitHub PR data
        var mockPr = CreateMockPullRequest(42, "Test PR", ItemState.Open, "issues/feature/test+issue-123");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            42))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PrNumber, Is.EqualTo(42));
        Assert.That(result.Status, Is.EqualTo(PullRequestStatus.ReadyForReview));
        Assert.That(result.PrUrl, Does.Contain("/pull/42"));
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_ChecksFailing_ReturnsChecksFailingStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Failing PR",
            BranchName = "issues/feature/failing+issue-456",
            GitHubPRNumber = 99,
            BeadsIssueId = "issue-456",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(99, "Failing PR", ItemState.Open, "issues/feature/failing+issue-456");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            99))
            .ReturnsAsync([]);

        // Checks are failing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Failure));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-456");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(PullRequestStatus.ChecksFailing));
        Assert.That(result.ChecksFailing, Is.True);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_Approved_ReturnsReadyForMerging()
    {
        // Arrange
        var project = await CreateTestProject();

        var trackedPr = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Approved PR",
            BranchName = "issues/feature/approved+issue-789",
            GitHubPRNumber = 77,
            BeadsIssueId = "issue-789",
            Status = OpenPullRequestStatus.Approved
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var mockPr = CreateMockPullRequest(77, "Approved PR", ItemState.Open, "issues/feature/approved+issue-789");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has approval
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            77))
            .ReturnsAsync([CreateMockReview(PullRequestReviewState.Approved)]);

        // Checks passing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-789");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(PullRequestStatus.ReadyForMerging));
        Assert.That(result.IsMergeable, Is.True);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_MissingGitHubConfig_ReturnsNull()
    {
        // Arrange
        var project = new Project
        {
            Name = "no-github-repo",
            LocalPath = "/test/path",
            DefaultBranch = "main"
            // No GitHubOwner or GitHubRepo
        };
        await _dataStore.AddProjectAsync(project);

        var trackedPr = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Test PR",
            BranchName = "issues/feature/test+issue-123",
            GitHubPRNumber = 42,
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.ReadyForReview
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPullRequestStatusForIssueAsync_PrWithoutGitHubNumber_ReturnsNull()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create a tracked PR without GitHub PR number
        var trackedPr = new TrackedPullRequest
        {
            ProjectId = project.Id,
            Title = "Local PR",
            BranchName = "issues/feature/local+issue-123",
            BeadsIssueId = "issue-123",
            Status = OpenPullRequestStatus.InDevelopment
            // No GitHubPRNumber
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        // Act
        var result = await _service.GetPullRequestStatusForIssueAsync(project.Id, "issue-123");

        // Assert
        Assert.That(result, Is.Null);
    }

    #region Helper Methods

    private static Octokit.PullRequest CreateMockPullRequest(
        int number,
        string title,
        ItemState state,
        string branchName,
        bool merged = false,
        bool draft = false)
    {
        var headRef = new GitReference(
            nodeId: "node1",
            url: "url",
            label: "label",
            @ref: branchName,
            sha: "abc123",
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
            createdAt: DateTimeOffset.UtcNow.AddDays(-7),
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: state == ItemState.Closed ? DateTimeOffset.UtcNow : null,
            mergedAt: merged ? DateTimeOffset.UtcNow : null,
            head: headRef,
            @base: headRef,
            user: null,
            assignee: null,
            assignees: null,
            draft: draft,
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

    private static CombinedCommitStatus CreateMockCombinedStatus(CommitState state)
    {
        return new CombinedCommitStatus(
            state: state,
            sha: "abc123",
            totalCount: 1,
            statuses: [],
            repository: null
        );
    }

    private static PullRequestReview CreateMockReview(PullRequestReviewState state)
    {
        return new PullRequestReview(
            id: 1,
            nodeId: "review-1",
            commitId: "abc123",
            user: null,
            body: "Review comment",
            htmlUrl: "https://github.com/owner/repo/pull/1#pullrequestreview-1",
            pullRequestUrl: "https://api.github.com/repos/owner/repo/pulls/1",
            state: state,
            authorAssociation: AuthorAssociation.Contributor,
            submittedAt: DateTimeOffset.UtcNow
        );
    }

    #endregion
}
