using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Issues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleecePostMergeServiceTests
{
    private Mock<IProjectFleeceService> _fleeceServiceMock = null!;
    private Mock<IBranchIdBackgroundService> _branchIdBackgroundServiceMock = null!;
    private FleecePostMergeService _service = null!;

    private const string ProjectPath = "/path/to/project";
    private const string ProjectId = "project-123";
    private const string UserEmail = "user@example.com";

    [SetUp]
    public void SetUp()
    {
        _fleeceServiceMock = new Mock<IProjectFleeceService>();
        _branchIdBackgroundServiceMock = new Mock<IBranchIdBackgroundService>();

        _service = new FleecePostMergeService(
            _fleeceServiceMock.Object,
            _branchIdBackgroundServiceMock.Object,
            NullLogger<FleecePostMergeService>.Instance);
    }

    [Test]
    public async Task AssignsUnassignedIssuesToActiveUser()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Created, Title = "New issue" },
            new() { IssueId = "def456", ChangeType = ChangeType.Updated, Title = "Updated issue" }
        };

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("abc123", "New issue", assignedTo: null, workingBranchId: "some-branch"));

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "def456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("def456", "Updated issue", assignedTo: null, workingBranchId: "other-branch"));

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, UserEmail);

        // Assert
        _fleeceServiceMock.Verify(x => x.UpdateIssueAsync(
            ProjectPath, "abc123", null, null, null, null, null, null, null, UserEmail, It.IsAny<CancellationToken>()),
            Times.Once);

        _fleeceServiceMock.Verify(x => x.UpdateIssueAsync(
            ProjectPath, "def456", null, null, null, null, null, null, null, UserEmail, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DoesNotOverwriteExistingAssignment()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Created, Title = "Assigned issue" }
        };

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("abc123", "Assigned issue", assignedTo: "other@example.com", workingBranchId: "branch"));

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, UserEmail);

        // Assert - no UpdateIssueAsync call for assignment
        _fleeceServiceMock.Verify(x => x.UpdateIssueAsync(
            ProjectPath, "abc123", null, null, null, null, null, null, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task HandlesNullUserEmail()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Created, Title = "New issue" }
        };

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("abc123", "New issue", assignedTo: null, workingBranchId: "branch"));

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, null);

        // Assert - no assignment update
        _fleeceServiceMock.Verify(x => x.UpdateIssueAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IssueStatus?>(),
            It.IsAny<IssueType?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<ExecutionMode?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task SkipsDeletedChanges()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Deleted, Title = "Deleted issue" }
        };

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, UserEmail);

        // Assert - no GetIssueAsync call
        _fleeceServiceMock.Verify(x => x.GetIssueAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task TriggersBranchIdGenerationForNewIssuesWithoutBranchId()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Created, Title = "New issue" }
        };

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("abc123", "New issue", assignedTo: UserEmail, workingBranchId: null));

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, UserEmail);

        // Assert
        _branchIdBackgroundServiceMock.Verify(x => x.QueueBranchIdGenerationAsync(
            "abc123", ProjectId, "New issue"),
            Times.Once);
    }

    [Test]
    public async Task DoesNotTriggerBranchIdForIssuesWithExistingBranchId()
    {
        // Arrange
        var changes = new List<IssueChangeDto>
        {
            new() { IssueId = "abc123", ChangeType = ChangeType.Created, Title = "New issue" }
        };

        _fleeceServiceMock.Setup(x => x.GetIssueAsync(ProjectPath, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIssue("abc123", "New issue", assignedTo: UserEmail, workingBranchId: "existing-branch"));

        // Act
        await _service.PostMergeProcessAsync(ProjectPath, ProjectId, changes, UserEmail);

        // Assert
        _branchIdBackgroundServiceMock.Verify(x => x.QueueBranchIdGenerationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    private static Issue CreateIssue(string id, string title, string? assignedTo, string? workingBranchId) => new()
    {
        Id = id,
        Title = title,
        Type = IssueType.Task,
        Status = IssueStatus.Open,
        AssignedTo = assignedTo,
        WorkingBranchId = workingBranchId,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };
}
