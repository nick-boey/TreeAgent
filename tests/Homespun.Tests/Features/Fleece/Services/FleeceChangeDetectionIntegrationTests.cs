using Fleece.Core.Models;
using Fleece.Core.Services.Interfaces;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Fleece.Services;

/// <summary>
/// Integration tests for FleeceChangeDetectionService that test against real git repositories
/// with actual fleece issues stored on disk. These tests verify the change detection algorithm
/// produces correct results when clones are created with identical issue data.
/// </summary>
[TestFixture]
[Category("Integration")]
public class FleeceChangeDetectionIntegrationTests
{
    private TempGitRepositoryFixture _fixture = null!;
    private GitCloneService _cloneService = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private ProjectFleeceService _fleeceService = null!;
    private FleeceChangeDetectionService _changeDetectionService = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TempGitRepositoryFixture();
        _cloneService = new GitCloneService();

        _projectServiceMock = new Mock<IProjectService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();

        // Create a real ProjectFleeceService with actual disk operations
        var serializationQueueMock = new Mock<Homespun.Features.Fleece.Services.IIssueSerializationQueue>();
        _fleeceService = new ProjectFleeceService(
            serializationQueueMock.Object,
            new Mock<global::Fleece.Core.Services.Interfaces.IIssueLayoutService>().Object,
            NullLogger<ProjectFleeceService>.Instance);

        // Create the change detection service with real dependencies
        _changeDetectionService = new FleeceChangeDetectionService(
            _projectServiceMock.Object,
            _cloneService,
            _sessionServiceMock.Object,
            _fleeceService,
            NullLogger<FleeceChangeDetectionService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _fleeceService.Dispose();
        _fixture.Dispose();
    }

    /// <summary>
    /// Creates a lean v3.1 Issue snapshot record. The per-field `*LastUpdate` /
    /// `*ModifiedBy` metadata is gone — field-level LWW is replayed from change
    /// events, not stored on the snapshot.
    /// </summary>
    private static Issue CreateIssueWithTimestamps(string id, string title, IssueStatus status, IssueType type, DateTimeOffset timestamp)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = status,
            Type = type,
            Priority = 3,
            LastUpdate = timestamp,
            CreatedAt = timestamp,
            CreatedBy = "test"
        };
    }

    private static readonly System.Text.Json.JsonSerializerOptions IssueSnapshotOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes the lean v3.1 snapshot directly to `.fleece/issues.jsonl`. An empty
    /// event log (`.fleece/changes/`) is a valid starting state — replay over no
    /// events is a no-op.
    /// </summary>
    private static async Task SaveIssuesAsync(string repoPath, List<Issue> issues)
    {
        var fleeceDir = Path.Combine(repoPath, ".fleece");
        Directory.CreateDirectory(fleeceDir);
        var snapshotPath = Path.Combine(fleeceDir, "issues.jsonl");
        var lines = issues.Select(i => System.Text.Json.JsonSerializer.Serialize(i, IssueSnapshotOptions));
        await File.WriteAllLinesAsync(snapshotPath, lines);
    }

    /// <summary>
    /// Commits the .fleece directory to git.
    /// </summary>
    private void CommitFleeceDirectory(string commitMessage)
    {
        _fixture.RunGit("add .fleece");
        _fixture.RunGit($"commit -m \"{commitMessage}\"");
    }

    /// <summary>
    /// Sets up the project and session mocks for testing.
    /// </summary>
    private void SetupProjectAndSession(string projectId, string sessionId, string mainPath, string clonePath)
    {
        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(new Project { Id = projectId, Name = "Test Project", LocalPath = mainPath, DefaultBranch = "main" });

        _sessionServiceMock.Setup(x => x.GetSession(sessionId))
            .Returns(new ClaudeSession
            {
                Id = sessionId,
                ProjectId = projectId,
                WorkingDirectory = clonePath,
                EntityId = "test-entity",
                Model = "sonnet",
                Mode = SessionMode.Build
            });
    }

    #region Test Cases

    /// <summary>
    /// Test 1: Verifies that when a clone is created with identical open issues,
    /// no spurious changes are detected.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCloneCreation_WithIdenticalOpenIssues_ReturnsNoChanges()
    {
        // Arrange - Create open issues in main repo
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("open1", "Open Task 1", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("open2", "Open Task 2", IssueStatus.Open, IssueType.Feature, timestamp),
            CreateIssueWithTimestamps("prog1", "In Progress Task", IssueStatus.Progress, IssueType.Bug, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add fleece issues");

        // Create a branch and clone
        var branchName = "feature/test-identical-issues";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - No changes should be detected since issues are identical
        Assert.That(changes, Is.Empty, "No changes should be detected for identical open issues");
    }

    /// <summary>
    /// Test 2: Verifies that when a clone is created with issues including some with Complete status,
    /// no spurious changes are detected. This test exposes the bug where completed issues
    /// incorrectly appear as "Created" because they are filtered out from main but loaded from clone.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCloneCreation_WithCompletedIssues_ReturnsNoChanges()
    {
        // Arrange - Create issues including completed ones
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("open1", "Open Task", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("comp1", "Completed Task 1", IssueStatus.Complete, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("comp2", "Completed Feature", IssueStatus.Complete, IssueType.Feature, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add fleece issues with completed items");

        // Create a branch and clone
        var branchName = "feature/test-completed-issues";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - No changes should be detected since issues are identical
        // BUG: This test will FAIL before the fix because completed issues are filtered from main
        // but loaded from clone, causing them to appear as "Created"
        Assert.That(changes, Is.Empty,
            "No changes should be detected - completed issues exist in both main and clone");
    }

    /// <summary>
    /// Test 3: Verifies that when a clone is created with issues including some with Deleted status,
    /// no spurious changes are detected. Similar to Test 2, this exposes the filtering inconsistency bug.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCloneCreation_WithDeletedIssues_ReturnsNoChanges()
    {
        // Arrange - Create issues including deleted ones
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("open1", "Open Task", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("del1", "Deleted Task", IssueStatus.Deleted, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("arch1", "Archived Feature", IssueStatus.Archived, IssueType.Feature, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add fleece issues with deleted items");

        // Create a branch and clone
        var branchName = "feature/test-deleted-issues";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - No changes should be detected since issues are identical
        // BUG: This test will FAIL before the fix because deleted/archived issues are filtered from main
        // but loaded from clone, causing them to appear as "Created"
        Assert.That(changes, Is.Empty,
            "No changes should be detected - deleted/archived issues exist in both main and clone");
    }

    /// <summary>
    /// Test 4: Verifies that when a clone is created with issues including some with Closed status,
    /// no spurious changes are detected.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCloneCreation_WithClosedIssues_ReturnsNoChanges()
    {
        // Arrange - Create issues including closed ones
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("open1", "Open Task", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("closed1", "Closed Task", IssueStatus.Closed, IssueType.Bug, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add fleece issues with closed items");

        // Create a branch and clone
        var branchName = "feature/test-closed-issues";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - No changes should be detected since issues are identical
        Assert.That(changes, Is.Empty,
            "No changes should be detected - closed issues exist in both main and clone");
    }

    /// <summary>
    /// Test 5: Verifies that mixed statuses all work correctly - no spurious changes
    /// when clone contains issues with all terminal statuses.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCloneCreation_WithMixedStatuses_ReturnsNoChanges()
    {
        // Arrange - Create issues with all possible statuses
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("open1", "Open Task", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("prog1", "In Progress", IssueStatus.Progress, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("rev1", "In Review", IssueStatus.Review, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("comp1", "Completed", IssueStatus.Complete, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("arch1", "Archived", IssueStatus.Archived, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("closed1", "Closed", IssueStatus.Closed, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("del1", "Deleted", IssueStatus.Deleted, IssueType.Task, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add fleece issues with all statuses");

        // Create a branch and clone
        var branchName = "feature/test-all-statuses";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - No changes should be detected since all issues are identical in both locations
        Assert.That(changes, Is.Empty,
            "No changes should be detected - all issues exist identically in both main and clone");
    }

    /// <summary>
    /// Test 6: Verifies that real changes are still detected correctly when an issue
    /// is modified in the clone after creation.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterModifyingIssueInClone_ReturnsCorrectChange()
    {
        // Arrange - Create initial issues in main
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issues = new List<Issue>
        {
            CreateIssueWithTimestamps("task1", "Original Title", IssueStatus.Open, IssueType.Task, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, issues);
        CommitFleeceDirectory("Add initial fleece issues");

        // Create a branch and clone
        var branchName = "feature/test-real-changes";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Modify the issue in the clone
        var modifiedTimestamp = DateTimeOffset.UtcNow;
        var modifiedIssue = CreateIssueWithTimestamps("task1", "Modified Title", IssueStatus.Progress, IssueType.Task, modifiedTimestamp);
        await SaveIssuesAsync(clonePath!, new List<Issue> { modifiedIssue });

        // Reload fleece service cache to pick up main repo changes
        await _fleeceService.ReloadFromDiskAsync(_fixture.RepositoryPath);

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - Should detect the title and status changes
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(changes[0].IssueId, Is.EqualTo("task1"));
        Assert.That(changes[0].FieldChanges, Has.Some.Matches<FieldChangeDto>(f => f.FieldName == "Title"));
        Assert.That(changes[0].FieldChanges, Has.Some.Matches<FieldChangeDto>(f => f.FieldName == "Status"));
    }

    /// <summary>
    /// Test 7: Verifies that a truly new issue created in the clone is correctly detected.
    /// </summary>
    [Test]
    public async Task DetectChangesAsync_AfterCreatingNewIssueInClone_ReturnsCreatedChange()
    {
        // Arrange - Create initial issues in main
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var initialIssues = new List<Issue>
        {
            CreateIssueWithTimestamps("existing", "Existing Task", IssueStatus.Open, IssueType.Task, timestamp)
        };

        await SaveIssuesAsync(_fixture.RepositoryPath, initialIssues);
        CommitFleeceDirectory("Add initial fleece issues");

        // Create a branch and clone
        var branchName = "feature/test-new-issue";
        var clonePath = await _cloneService.CreateCloneAsync(_fixture.RepositoryPath, branchName, createBranch: true);
        Assert.That(clonePath, Is.Not.Null, "Clone should be created successfully");

        // Add a new issue in the clone (keeping the existing one)
        var newTimestamp = DateTimeOffset.UtcNow;
        var cloneIssues = new List<Issue>
        {
            CreateIssueWithTimestamps("existing", "Existing Task", IssueStatus.Open, IssueType.Task, timestamp),
            CreateIssueWithTimestamps("newissue", "Brand New Task", IssueStatus.Open, IssueType.Feature, newTimestamp)
        };
        await SaveIssuesAsync(clonePath!, cloneIssues);

        // Reload fleece service cache
        await _fleeceService.ReloadFromDiskAsync(_fixture.RepositoryPath);

        // Set up mocks
        var projectId = "proj-1";
        var sessionId = "session-1";
        SetupProjectAndSession(projectId, sessionId, _fixture.RepositoryPath, clonePath!);

        // Act - Detect changes
        var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId);

        // Assert - Should detect only the new issue as created
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].ChangeType, Is.EqualTo(ChangeType.Created));
        Assert.That(changes[0].IssueId, Is.EqualTo("newissue"));
        Assert.That(changes[0].Title, Is.EqualTo("Brand New Task"));
    }

    #endregion
}
