using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Octokit;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.PullRequests;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using Project = TreeAgent.Web.Features.PullRequests.Data.Entities.Project;
using PullRequestStatus = TreeAgent.Web.Features.PullRequests.PullRequestStatus;

namespace TreeAgent.Web.Tests.Services;

[TestFixture]
public class PullRequestWorkflowTests
{
    private TreeAgentDbContext _db = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IConfiguration> _mockConfig = null!;
    private Mock<IGitHubClientWrapper> _mockGitHubClient = null!;
    private PullRequestWorkflowService _service = null!;

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

        _mockConfig.Setup(c => c["GITHUB_TOKEN"]).Returns("test-token");

        _service = new PullRequestWorkflowService(_db, _mockRunner.Object, _mockConfig.Object, _mockGitHubClient.Object);
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

    #region 2.1 Past PR Synchronization

    [Test]
    public async Task GitHubSync_MergedPRs_OrderedByMergeTime()
    {
        // Arrange
        var project = await CreateTestProject();
        var now = DateTime.UtcNow;

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "First Merge", ItemState.Closed, "feature/first", merged: true, mergedAt: now.AddDays(-3)),
            CreateMockPullRequest(2, "Second Merge", ItemState.Closed, "feature/second", merged: true, mergedAt: now.AddDays(-1)),
            CreateMockPullRequest(3, "Third Merge", ItemState.Closed, "feature/third", merged: true, mergedAt: now)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetMergedPullRequestsWithTimeAsync(project.Id);

        // Assert - Ordered by merge time descending (most recent first)
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].PullRequest.Number, Is.EqualTo(3)); // Most recent
        Assert.That(result[1].PullRequest.Number, Is.EqualTo(2));
        Assert.That(result[2].PullRequest.Number, Is.EqualTo(1)); // Oldest
    }

    [Test]
    public async Task GitHubSync_CalculatesTimeFromMergeOrder()
    {
        // Arrange
        var project = await CreateTestProject();
        var now = DateTime.UtcNow;

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "First", ItemState.Closed, "feature/first", merged: true, mergedAt: now.AddDays(-2)),
            CreateMockPullRequest(2, "Second", ItemState.Closed, "feature/second", merged: true, mergedAt: now.AddDays(-1)),
            CreateMockPullRequest(3, "Third", ItemState.Closed, "feature/third", merged: true, mergedAt: now)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetMergedPullRequestsWithTimeAsync(project.Id);

        // Assert - Time values calculated from merge order
        Assert.That(result.First(r => r.PullRequest.Number == 3).Time, Is.EqualTo(0));  // Most recent
        Assert.That(result.First(r => r.PullRequest.Number == 2).Time, Is.EqualTo(-1));
        Assert.That(result.First(r => r.PullRequest.Number == 1).Time, Is.EqualTo(-2)); // Oldest
    }

    [Test]
    public async Task GitHubSync_MostRecentMerge_HasTimeZero()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "Only Merge", ItemState.Closed, "feature/only", merged: true, mergedAt: DateTime.UtcNow)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetMergedPullRequestsWithTimeAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Time, Is.EqualTo(0));
    }

    [Test]
    public async Task GitHubSync_ClosedPRs_OrderedByCloseTime()
    {
        // Arrange
        var project = await CreateTestProject();
        var now = DateTime.UtcNow;

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "Closed First", ItemState.Closed, "feature/first", merged: false, closedAt: now.AddDays(-2)),
            CreateMockPullRequest(2, "Closed Second", ItemState.Closed, "feature/second", merged: false, closedAt: now)
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Closed)))
            .ReturnsAsync(mockPrs);

        // Act
        var result = await _service.GetClosedPullRequestsWithTimeAsync(project.Id);

        // Assert - Ordered by close time descending
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].PullRequest.Number, Is.EqualTo(2)); // Most recently closed
        Assert.That(result[1].PullRequest.Number, Is.EqualTo(1)); // Oldest
    }

    #endregion

    #region 2.2 Current PR Status Tracking

    [Test]
    public async Task CurrentPR_NewPR_HasInProgressStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPr = CreateMockPullRequest(1, "New PR", ItemState.Open, "feature/new", draft: true);
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // No reviews, no checks
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            1))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Pending));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.InProgress));
    }

    [Test]
    public async Task CurrentPR_ReadyForReview_HasReadyForReviewStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPr = CreateMockPullRequest(1, "Ready PR", ItemState.Open, "feature/ready", draft: false);
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // No review comments (awaiting review)
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            1))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.ReadyForReview));
    }

    [Test]
    public async Task CurrentPR_ReviewComments_ReturnsToInProgress()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPr = CreateMockPullRequest(1, "PR with Comments", ItemState.Open, "feature/comments");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has review with changes requested
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            1))
            .ReturnsAsync([CreateMockReview(PullRequestReviewState.ChangesRequested)]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.InProgress));
    }

    [Test]
    public async Task CurrentPR_ChecksFailing_HasChecksFailingStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPr = CreateMockPullRequest(1, "Failing PR", ItemState.Open, "feature/failing");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            1))
            .ReturnsAsync([]);

        // Checks are failing
        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Failure));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.ChecksFailing));
    }

    [Test]
    public async Task CurrentPR_Approved_HasReadyForMergingStatus()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPr = CreateMockPullRequest(1, "Approved PR", ItemState.Open, "feature/approved");
        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync([mockPr]);

        // Has approval
        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            1))
            .ReturnsAsync([CreateMockReview(PullRequestReviewState.Approved)]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(PullRequestStatus.ReadyForMerging));
    }

    [Test]
    public async Task CurrentPR_AllOpenPRs_HaveTimeOne()
    {
        // Arrange
        var project = await CreateTestProject();

        var mockPrs = new List<PullRequest>
        {
            CreateMockPullRequest(1, "PR 1", ItemState.Open, "feature/one"),
            CreateMockPullRequest(2, "PR 2", ItemState.Open, "feature/two"),
            CreateMockPullRequest(3, "PR 3", ItemState.Open, "feature/three")
        };

        _mockGitHubClient.Setup(c => c.GetPullRequestsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.Is<PullRequestRequest>(r => r.State == ItemStateFilter.Open)))
            .ReturnsAsync(mockPrs);

        _mockGitHubClient.Setup(c => c.GetPullRequestReviewsAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<int>()))
            .ReturnsAsync([]);

        _mockGitHubClient.Setup(c => c.GetCombinedCommitStatusAsync(
            project.GitHubOwner!,
            project.GitHubRepo!,
            It.IsAny<string>()))
            .ReturnsAsync(CreateMockCombinedStatus(CommitState.Success));

        // Act
        var result = await _service.GetOpenPullRequestsWithStatusAsync(project.Id);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.All(r => r.Time == 1), Is.True, "All open PRs should have t=1");
    }

    #endregion

    #region 2.3 Automatic Rebasing

    [Test]
    public async Task AutoRebase_OnMerge_RebasesAllOpenPRs()
    {
        // Arrange
        var project = await CreateTestProject();

        // Create features for open PRs
        var feature1 = new Feature { ProjectId = project.Id, Title = "Feature 1", BranchName = "feature/one", Status = FeatureStatus.InDevelopment };
        var feature2 = new Feature { ProjectId = project.Id, Title = "Feature 2", BranchName = "feature/two", Status = FeatureStatus.InDevelopment };
        _db.Features.AddRange(feature1, feature2);
        await _db.SaveChangesAsync();

        // Mock successful rebases
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("fetch")), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("rebase")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("push")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RebaseAllOpenPRsAsync(project.Id);

        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(2));
        Assert.That(result.FailureCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AutoRebase_ConflictDetected_ReportsConflict()
    {
        // Arrange
        var project = await CreateTestProject();

        var feature = new Feature { ProjectId = project.Id, Title = "Conflicting Feature", BranchName = "feature/conflict", Status = FeatureStatus.InDevelopment };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync();

        // Mock fetch success but rebase conflict
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("fetch")), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("rebase")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = false, Error = "CONFLICT (content): Merge conflict" });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("rebase --abort")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RebaseAllOpenPRsAsync(project.Id);

        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(0));
        Assert.That(result.FailureCount, Is.EqualTo(1));
        Assert.That(result.Conflicts, Has.Count.EqualTo(1));
        Assert.That(result.Conflicts[0].BranchName, Is.EqualTo("feature/conflict"));
    }

    [Test]
    public async Task AutoRebase_Success_UpdatesAllBranches()
    {
        // Arrange
        var project = await CreateTestProject();

        var feature = new Feature { ProjectId = project.Id, Title = "Feature", BranchName = "feature/test", Status = FeatureStatus.InDevelopment };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync();

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("fetch")), project.LocalPath))
            .ReturnsAsync(new CommandResult { Success = true });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("rebase")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("push")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RebaseAllOpenPRsAsync(project.Id);

        // Assert - Verify push was called to update the remote branch
        _mockRunner.Verify(r => r.RunAsync("git", It.Is<string>(s => s.Contains("push") && s.Contains("--force-with-lease")), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static PullRequest CreateMockPullRequest(
        int number,
        string title,
        ItemState state,
        string branchName,
        bool merged = false,
        bool draft = false,
        DateTime? mergedAt = null,
        DateTime? closedAt = null)
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
            createdAt: DateTimeOffset.UtcNow.AddDays(-7),
            updatedAt: DateTimeOffset.UtcNow,
            closedAt: closedAt.HasValue ? new DateTimeOffset(closedAt.Value) : (state == ItemState.Closed ? DateTimeOffset.UtcNow : null),
            mergedAt: mergedAt.HasValue ? new DateTimeOffset(mergedAt.Value) : (merged ? DateTimeOffset.UtcNow : null),
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
