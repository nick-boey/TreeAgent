using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.PullRequests;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class CloneEnrichmentServiceTests
{
    private Mock<IGitCloneService> _mockGitCloneService = null!;
    private Mock<IProjectFleeceService> _mockFleeceService = null!;
    private Mock<IGraphCacheService> _mockGraphCacheService = null!;
    private Mock<ILogger<CloneEnrichmentService>> _mockLogger = null!;
    private CloneEnrichmentService _service = null!;

    private const string ProjectId = "test-project";
    private const string ProjectLocalPath = "/path/to/project";

    [SetUp]
    public void Setup()
    {
        _mockGitCloneService = new Mock<IGitCloneService>();
        _mockFleeceService = new Mock<IProjectFleeceService>();
        _mockGraphCacheService = new Mock<IGraphCacheService>();
        _mockLogger = new Mock<ILogger<CloneEnrichmentService>>();

        _service = new CloneEnrichmentService(
            _mockGitCloneService.Object,
            _mockFleeceService.Object,
            _mockGraphCacheService.Object,
            _mockLogger.Object);

        // Default: no PRs cached
        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns((CachedPRData?)null);
    }

    [Test]
    public async Task EnrichClones_WithIssueIdInBranchName_ReturnsLinkedIssue()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var issue = CreateIssue("abc123", "Add login feature", IssueStatus.Progress, IssueType.Feature);

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedIssueId, Is.EqualTo("abc123"));
        Assert.That(result[0].LinkedIssue, Is.Not.Null);
        Assert.That(result[0].LinkedIssue!.Id, Is.EqualTo("abc123"));
        Assert.That(result[0].LinkedIssue.Title, Is.EqualTo("Add login feature"));
        Assert.That(result[0].LinkedIssue.Status, Is.EqualTo("Progress"));
        Assert.That(result[0].LinkedIssue.Type, Is.EqualTo("Feature"));
    }

    [Test]
    public async Task EnrichClones_WithMatchingPR_ReturnsLinkedPr()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.ReadyForReview,
            BranchName = "feature/add-login+abc123",
            HtmlUrl = "https://github.com/owner/repo/pull/42"
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [pr],
                ClosedPrs = []
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedPr, Is.Not.Null);
        Assert.That(result[0].LinkedPr!.Number, Is.EqualTo(42));
        Assert.That(result[0].LinkedPr.Title, Is.EqualTo("Add login feature PR"));
        Assert.That(result[0].LinkedPr.Status, Is.EqualTo(PullRequestStatus.ReadyForReview));
        Assert.That(result[0].LinkedPr.HtmlUrl, Is.EqualTo("https://github.com/owner/repo/pull/42"));
    }

    [Test]
    public async Task EnrichClones_IssuesAgentBranch_SetsIsIssuesAgentClone()
    {
        // Arrange
        var clone = CreateClone("issues-agent-abc123");

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsIssuesAgentClone, Is.True);
    }

    [Test]
    public async Task EnrichClones_MergedPR_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.Merged,
            BranchName = "feature/add-login+abc123"
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [],
                ClosedPrs = [pr]
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("merged"));
    }

    [Test]
    public async Task EnrichClones_ClosedPR_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.Closed,
            BranchName = "feature/add-login+abc123"
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [],
                ClosedPrs = [pr]
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("closed"));
    }

    [Test]
    public async Task EnrichClones_CompleteIssue_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var issue = CreateIssue("abc123", "Add login feature", IssueStatus.Complete, IssueType.Feature);

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("complete"));
    }

    [Test]
    public async Task EnrichClones_ArchivedIssue_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var issue = CreateIssue("abc123", "Add login feature", IssueStatus.Archived, IssueType.Feature);

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("archived"));
    }

    [Test]
    public async Task EnrichClones_ClosedIssue_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var issue = CreateIssue("abc123", "Add login feature", IssueStatus.Closed, IssueType.Feature);

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("closed"));
    }

    [Test]
    public async Task EnrichClones_DeletedIssue_SetsDeletableWithReason()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null); // Issue not found (deleted)

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedIssueId, Is.EqualTo("abc123"));
        Assert.That(result[0].LinkedIssue, Is.Null);
        Assert.That(result[0].IsDeletable, Is.True);
        Assert.That(result[0].DeletionReason, Does.Contain("deleted").Or.Contain("not found"));
    }

    [Test]
    public async Task EnrichClones_OpenIssueAndPR_NotDeletable()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var issue = CreateIssue("abc123", "Add login feature", IssueStatus.Progress, IssueType.Feature);
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.ReadyForReview,
            BranchName = "feature/add-login+abc123"
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [pr],
                ClosedPrs = []
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsDeletable, Is.False);
        Assert.That(result[0].DeletionReason, Is.Null);
    }

    [Test]
    public async Task EnrichClones_NoLinkedIssueOrPR_NotDeletable()
    {
        // Arrange
        var clone = CreateClone("feature/some-branch"); // No issue ID in branch name

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedIssueId, Is.Null);
        Assert.That(result[0].LinkedIssue, Is.Null);
        Assert.That(result[0].LinkedPr, Is.Null);
        Assert.That(result[0].IsDeletable, Is.False);
        Assert.That(result[0].DeletionReason, Is.Null);
    }

    [Test]
    public async Task EnrichClones_BranchWithRefsHeadsPrefix_ExtractsCorrectBranchName()
    {
        // Arrange
        var clone = CreateClone("refs/heads/feature/add-login+abc123", "refs/heads/feature/add-login+abc123");
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.ReadyForReview,
            BranchName = "feature/add-login+abc123" // PR branch name doesn't have refs/heads prefix
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [pr],
                ClosedPrs = []
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedPr, Is.Not.Null);
        Assert.That(result[0].LinkedPr!.Number, Is.EqualTo(42));
    }

    [Test]
    public async Task EnrichClones_MultipleClones_EnrichesAll()
    {
        // Arrange
        var clone1 = CreateClone("feature/add-login+abc123");
        var clone2 = CreateClone("bugfix/fix-crash+def456");
        var clone3 = CreateClone("issues-agent-xyz789");

        var issue1 = CreateIssue("abc123", "Add login feature", IssueStatus.Progress, IssueType.Feature);
        var issue2 = CreateIssue("def456", "Fix crash", IssueStatus.Complete, IssueType.Bug);

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone1, clone2, clone3]);

        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue1);
        _mockFleeceService
            .Setup(s => s.GetIssueAsync(ProjectLocalPath, "def456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue2);

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));

        var enriched1 = result.First(r => r.LinkedIssueId == "abc123");
        Assert.That(enriched1.IsDeletable, Is.False); // Open issue
        Assert.That(enriched1.IsIssuesAgentClone, Is.False);

        var enriched2 = result.First(r => r.LinkedIssueId == "def456");
        Assert.That(enriched2.IsDeletable, Is.True); // Complete issue
        Assert.That(enriched2.IsIssuesAgentClone, Is.False);

        var enriched3 = result.First(r => r.IsIssuesAgentClone);
        Assert.That(enriched3.Clone.Branch, Is.EqualTo("issues-agent-xyz789"));
    }

    [Test]
    public async Task EnrichClones_MatchesPRFromClosedList()
    {
        // Arrange
        var clone = CreateClone("feature/add-login+abc123");
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Add login feature PR",
            Status = PullRequestStatus.Merged,
            BranchName = "feature/add-login+abc123"
        };

        _mockGitCloneService
            .Setup(s => s.ListClonesAsync(ProjectLocalPath))
            .ReturnsAsync([clone]);

        _mockGraphCacheService
            .Setup(s => s.GetCachedPRData(ProjectId))
            .Returns(new CachedPRData
            {
                OpenPrs = [],
                ClosedPrs = [pr]
            });

        // Act
        var result = await _service.EnrichClonesAsync(ProjectId, ProjectLocalPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LinkedPr, Is.Not.Null);
        Assert.That(result[0].LinkedPr!.Number, Is.EqualTo(42));
        Assert.That(result[0].LinkedPr.Status, Is.EqualTo(PullRequestStatus.Merged));
    }

    private static CloneInfo CreateClone(string branchName, string? rawBranch = null)
    {
        return new CloneInfo
        {
            Path = $"/path/to/.clones/{branchName.Replace("/", "+")}",
            Branch = rawBranch ?? branchName,
            HeadCommit = "abc123def456"
        };
    }

    private static Issue CreateIssue(string id, string title, IssueStatus status, IssueType type)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = status,
            Type = type,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }
}
