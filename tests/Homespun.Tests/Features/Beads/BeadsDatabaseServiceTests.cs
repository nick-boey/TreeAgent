using Homespun.Features.Beads.Data;
using Homespun.Features.Beads.Services;
using Homespun.Tests.Features.Beads.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.Beads;

[TestFixture]
public class BeadsDatabaseServiceTests
{
    private BeadsDatabaseService _service = null!;
    private Mock<IBeadsQueueService> _queueServiceMock = null!;
    private IOptions<BeadsDatabaseOptions> _options = null!;
    private Mock<ILogger<BeadsDatabaseService>> _loggerMock = null!;
    private BeadsTestDatabaseFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _queueServiceMock = new Mock<IBeadsQueueService>();
        _options = Options.Create(new BeadsDatabaseOptions());
        _loggerMock = new Mock<ILogger<BeadsDatabaseService>>();
        _fixture = BeadsTestDatabaseFixture.CreateEmpty();
        _service = new BeadsDatabaseService(_queueServiceMock.Object, _options, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        _fixture.Dispose();
    }

    #region RefreshFromDatabaseAsync Tests

    [Test]
    public async Task RefreshFromDatabaseAsync_LoadsIssuesFromDatabase()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Test Issue 1");
        _fixture.InsertIssue("hsp-002", "Test Issue 2");

        // Act
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Assert
        Assert.That(_service.IsProjectLoaded(_fixture.ProjectPath), Is.True);
        var issues = _service.ListIssues(_fixture.ProjectPath);
        Assert.That(issues, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RefreshFromDatabaseAsync_LoadsLabels()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Test Issue");
        _fixture.AddLabel("hsp-001", "urgent");
        _fixture.AddLabel("hsp-001", "bug");

        // Act
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Assert
        var issue = _service.GetIssue(_fixture.ProjectPath, "hsp-001");
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue!.Labels, Has.Count.EqualTo(2));
        Assert.That(issue.Labels, Contains.Item("urgent"));
        Assert.That(issue.Labels, Contains.Item("bug"));
    }

    [Test]
    public async Task RefreshFromDatabaseAsync_ExcludesTombstoneIssues()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Active Issue", status: "open");
        _fixture.InsertIssue("hsp-002", "Deleted Issue", status: "tombstone");

        // Act
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Assert
        var issues = _service.ListIssues(_fixture.ProjectPath);
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Id, Is.EqualTo("hsp-001"));
    }

    #endregion

    #region GetIssue Tests

    [Test]
    public async Task GetIssue_ReturnsIssueWhenExists()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Test Issue", status: "in_progress", issueType: "feature", priority: 1);
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issue = _service.GetIssue(_fixture.ProjectPath, "hsp-001");

        // Assert
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue!.Id, Is.EqualTo("hsp-001"));
        Assert.That(issue.Title, Is.EqualTo("Test Issue"));
        Assert.That(issue.Status, Is.EqualTo(BeadsIssueStatus.InProgress));
        Assert.That(issue.Type, Is.EqualTo(BeadsIssueType.Feature));
        Assert.That(issue.Priority, Is.EqualTo(1));
    }

    [Test]
    public async Task GetIssue_ReturnsNullWhenNotExists()
    {
        // Arrange
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issue = _service.GetIssue(_fixture.ProjectPath, "hsp-nonexistent");

        // Assert
        Assert.That(issue, Is.Null);
    }

    [Test]
    public void GetIssue_ReturnsNullWhenProjectNotLoaded()
    {
        // Act
        var issue = _service.GetIssue(_fixture.ProjectPath, "hsp-001");

        // Assert
        Assert.That(issue, Is.Null);
    }

    #endregion

    #region ListIssues Tests

    [Test]
    public async Task ListIssues_ReturnsAllIssuesWhenNoFilter()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Issue 1", status: "open");
        _fixture.InsertIssue("hsp-002", "Issue 2", status: "closed");
        _fixture.InsertIssue("hsp-003", "Issue 3", status: "in_progress");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.ListIssues(_fixture.ProjectPath);

        // Assert
        Assert.That(issues, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListIssues_FiltersByStatus()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Open Issue", status: "open");
        _fixture.InsertIssue("hsp-002", "Closed Issue", status: "closed");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.ListIssues(_fixture.ProjectPath, new BeadsListOptions { Status = "open" });

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Status, Is.EqualTo(BeadsIssueStatus.Open));
    }

    [Test]
    public async Task ListIssues_FiltersByType()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Feature", issueType: "feature");
        _fixture.InsertIssue("hsp-002", "Bug", issueType: "bug");
        _fixture.InsertIssue("hsp-003", "Task", issueType: "task");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.ListIssues(_fixture.ProjectPath, new BeadsListOptions { Type = BeadsIssueType.Bug });

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Type, Is.EqualTo(BeadsIssueType.Bug));
    }

    [Test]
    public async Task ListIssues_FiltersByAssignee()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Issue 1", assignee: "alice");
        _fixture.InsertIssue("hsp-002", "Issue 2", assignee: "bob");
        _fixture.InsertIssue("hsp-003", "Issue 3");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.ListIssues(_fixture.ProjectPath, new BeadsListOptions { Assignee = "alice" });

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Assignee, Is.EqualTo("alice"));
    }

    [Test]
    public void ListIssues_ReturnsEmptyWhenProjectNotLoaded()
    {
        // Act
        var issues = _service.ListIssues(_fixture.ProjectPath);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    #endregion

    #region GetReadyIssues Tests

    [Test]
    public async Task GetReadyIssues_ReturnsOpenIssuesWithNoDependencies()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Ready Issue", status: "open");
        _fixture.InsertIssue("hsp-002", "Closed Issue", status: "closed");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.GetReadyIssues(_fixture.ProjectPath);

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Id, Is.EqualTo("hsp-001"));
    }

    [Test]
    public async Task GetReadyIssues_ExcludesBlockedIssues()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Blocking Issue", status: "open");
        _fixture.InsertIssue("hsp-002", "Blocked Issue", status: "open");
        _fixture.AddDependency("hsp-002", "hsp-001", "blocks");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.GetReadyIssues(_fixture.ProjectPath);

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Id, Is.EqualTo("hsp-001"));
    }

    [Test]
    public async Task GetReadyIssues_IncludesIssueWhenBlockerIsClosed()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Blocking Issue (Closed)", status: "closed");
        _fixture.InsertIssue("hsp-002", "Was Blocked Issue", status: "open");
        _fixture.AddDependency("hsp-002", "hsp-001", "blocks");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var issues = _service.GetReadyIssues(_fixture.ProjectPath);

        // Assert
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Id, Is.EqualTo("hsp-002"));
    }

    #endregion

    #region GetDependencies Tests

    [Test]
    public async Task GetDependencies_ReturnsDependenciesForIssue()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Parent Issue");
        _fixture.InsertIssue("hsp-002", "Child Issue");
        _fixture.AddDependency("hsp-002", "hsp-001", "blocks");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var dependencies = _service.GetDependencies(_fixture.ProjectPath, "hsp-002");

        // Assert
        Assert.That(dependencies, Has.Count.EqualTo(1));
        Assert.That(dependencies[0].ToIssueId, Is.EqualTo("hsp-001"));
        Assert.That(dependencies[0].Type, Is.EqualTo(BeadsDependencyType.Blocks));
    }

    [Test]
    public async Task GetDependencies_ReturnsEmptyWhenNoDependencies()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Independent Issue");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var dependencies = _service.GetDependencies(_fixture.ProjectPath, "hsp-001");

        // Assert
        Assert.That(dependencies, Is.Empty);
    }

    #endregion

    #region GetUniqueGroups Tests

    [Test]
    public async Task GetUniqueGroups_ExtractsGroupsFromHspLabels()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Issue 1");
        _fixture.InsertIssue("hsp-002", "Issue 2");
        _fixture.AddLabel("hsp-001", "hsp:frontend/-/some-branch");
        _fixture.AddLabel("hsp-002", "hsp:backend/-/other-branch");
        _fixture.AddLabel("hsp-002", "regular-label");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var groups = _service.GetUniqueGroups(_fixture.ProjectPath);

        // Assert
        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(groups, Contains.Item("frontend"));
        Assert.That(groups, Contains.Item("backend"));
    }

    [Test]
    public async Task GetUniqueGroups_ReturnsEmptyWhenNoHspLabels()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Issue 1");
        _fixture.AddLabel("hsp-001", "regular-label");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act
        var groups = _service.GetUniqueGroups(_fixture.ProjectPath);

        // Assert
        Assert.That(groups, Is.Empty);
    }

    #endregion

    #region Status Mapping Tests

    [Test]
    public async Task StatusMapping_MapsAllStatusesCorrectly()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Open", status: "open");
        _fixture.InsertIssue("hsp-002", "In Progress", status: "in_progress");
        _fixture.InsertIssue("hsp-003", "Blocked", status: "blocked");
        _fixture.InsertIssue("hsp-004", "Closed", status: "closed");
        _fixture.InsertIssue("hsp-005", "Deferred", status: "deferred");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act & Assert
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-001")!.Status, Is.EqualTo(BeadsIssueStatus.Open));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-002")!.Status, Is.EqualTo(BeadsIssueStatus.InProgress));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-003")!.Status, Is.EqualTo(BeadsIssueStatus.Blocked));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-004")!.Status, Is.EqualTo(BeadsIssueStatus.Closed));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-005")!.Status, Is.EqualTo(BeadsIssueStatus.Deferred));
    }

    [Test]
    public async Task TypeMapping_MapsAllTypesCorrectly()
    {
        // Arrange
        _fixture.InsertIssue("hsp-001", "Feature", issueType: "feature");
        _fixture.InsertIssue("hsp-002", "Bug", issueType: "bug");
        _fixture.InsertIssue("hsp-003", "Task", issueType: "task");
        _fixture.InsertIssue("hsp-004", "Epic", issueType: "epic");
        _fixture.InsertIssue("hsp-005", "Chore", issueType: "chore");
        await _service.RefreshFromDatabaseAsync(_fixture.ProjectPath);

        // Act & Assert
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-001")!.Type, Is.EqualTo(BeadsIssueType.Feature));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-002")!.Type, Is.EqualTo(BeadsIssueType.Bug));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-003")!.Type, Is.EqualTo(BeadsIssueType.Task));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-004")!.Type, Is.EqualTo(BeadsIssueType.Epic));
        Assert.That(_service.GetIssue(_fixture.ProjectPath, "hsp-005")!.Type, Is.EqualTo(BeadsIssueType.Chore));
    }

    #endregion
}
