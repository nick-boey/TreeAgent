using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Requests;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<ProjectFleeceService>> _mockLogger = null!;
    private Mock<IIssueSerializationQueue> _mockQueue = null!;
    private ProjectFleeceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<ProjectFleeceService>>();
        _mockQueue = new Mock<IIssueSerializationQueue>();
        _mockQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<IssueWriteOperation>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _service = new ProjectFleeceService(_mockQueue.Object, new Mock<global::Fleece.Core.Services.Interfaces.IIssueLayoutService>().Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region CreateIssueAsync Tests

    [Test]
    public async Task CreateIssueAsync_WithTitleAndType_CreatesIssue()
    {
        // Arrange
        var title = "Test Issue";
        var type = IssueType.Feature;

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, title, type);

        // Assert
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
    }

    [Test]
    public async Task CreateIssueAsync_WithVerifyType_CreatesVerifyIssue()
    {
        // Arrange - Verify is a new issue type added in Fleece.Core v1.6.0
        var title = "Verify Feature Works";
        var type = IssueType.Verify;

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, title, type);

        // Assert
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(IssueType.Verify));
    }

    [Test]
    public async Task CreateIssueAsync_WithDescription_SetsDescription()
    {
        // Arrange
        var description = "This is a detailed description";

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Task,
            description: description);

        // Assert
        Assert.That(issue.Description, Is.EqualTo(description));
    }

    [Test]
    public async Task CreateIssueAsync_WithPriority_SetsPriority()
    {
        // Arrange
        var priority = 3;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Bug,
            priority: priority);

        // Assert
        Assert.That(issue.Priority, Is.EqualTo(priority));
    }

    [Test]
    public async Task CreateIssueAsync_WithExecutionMode_SetsExecutionMode()
    {
        // Arrange
        var executionMode = ExecutionMode.Parallel;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature,
            executionMode: executionMode);

        // Assert
        Assert.That(issue.ExecutionMode, Is.EqualTo(executionMode));
    }

    [Test]
    public async Task CreateIssueAsync_WithNullExecutionMode_DefaultsToSeries()
    {
        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature);

        // Assert
        Assert.That(issue.ExecutionMode, Is.EqualTo(ExecutionMode.Series));
    }

    [Test]
    public async Task CreateIssueAsync_WithExecutionModeAndAllOtherParams_SetsAllCorrectly()
    {
        // Arrange
        var title = "Full Feature Issue";
        var type = IssueType.Feature;
        var description = "A complete issue";
        var priority = 2;
        var executionMode = ExecutionMode.Parallel;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            title,
            type,
            description: description,
            priority: priority,
            executionMode: executionMode);

        // Assert
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
        Assert.That(issue.Description, Is.EqualTo(description));
        Assert.That(issue.Priority, Is.EqualTo(priority));
        Assert.That(issue.ExecutionMode, Is.EqualTo(executionMode));
    }

    [Test]
    public async Task CreateIssueAsync_IssueCanBeRetrieved()
    {
        // Arrange
        var executionMode = ExecutionMode.Parallel;
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Retrievable Issue",
            IssueType.Task,
            executionMode: executionMode);

        // Act
        var retrieved = await _service.GetIssueAsync(_tempDir, issue.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ExecutionMode, Is.EqualTo(executionMode));
    }

    #endregion

    #region Queue Integration Tests

    [Test]
    public async Task CreateIssueAsync_EnqueuesWriteOperation()
    {
        // Act
        await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Create),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateIssueAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Act
        await _service.UpdateIssueAsync(_tempDir, issue.Id, title: "Updated Title");

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Update && op.IssueId == issue.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DeleteIssueAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Act
        await _service.DeleteIssueAsync(_tempDir, issue.Id);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Delete && op.IssueId == issue.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateIssueAsync_ReturnsImmediately_WithoutWaitingForQueueProcessing()
    {
        // Arrange - queue that simulates slow processing
        var queueCalled = false;
        _mockQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<IssueWriteOperation>(), It.IsAny<CancellationToken>()))
            .Callback(() => queueCalled = true)
            .Returns(ValueTask.CompletedTask);

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Assert - issue should be returned and queue should have been called
        Assert.That(issue, Is.Not.Null);
        Assert.That(queueCalled, Is.True);
    }

    [Test]
    public async Task GetIssueAsync_ReturnsFromCache_AfterCreate()
    {
        // Arrange - create an issue
        var created = await _service.CreateIssueAsync(_tempDir, "Cached Issue", IssueType.Feature);

        // Act - retrieve immediately from cache (no disk I/O needed)
        var retrieved = await _service.GetIssueAsync(_tempDir, created.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
        Assert.That(retrieved.Title, Is.EqualTo("Cached Issue"));
    }

    [Test]
    public async Task UpdateIssueAsync_UpdatesCache_BeforePersistence()
    {
        // Arrange
        var created = await _service.CreateIssueAsync(_tempDir, "Original Title", IssueType.Task);

        // Act - update the issue
        var updated = await _service.UpdateIssueAsync(_tempDir, created.Id, title: "Updated Title");

        // Assert - reading from cache should return the updated version
        var retrieved = await _service.GetIssueAsync(_tempDir, created.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task UpdateIssueAsync_NonExistentIssue_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateIssueAsync(_tempDir, "non-existent-id", title: "New Title");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ReloadFromDiskAsync Tests

    [Test]
    public async Task ReloadFromDiskAsync_ClearsAndReloadsCache()
    {
        // Arrange - create an issue through the service (writes to disk and populates cache)
        var issue = await _service.CreateIssueAsync(_tempDir, "Original Title", IssueType.Task);

        // Verify it's in the cache
        var cached = await _service.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.Title, Is.EqualTo("Original Title"));

        // Modify the issue directly on disk by finding and editing the JSONL file
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        Assert.That(issueFiles, Has.Length.GreaterThan(0), "Expected at least one issues JSONL file on disk");

        var issueFile = issueFiles[0];
        var lines = await File.ReadAllLinesAsync(issueFile);
        var updatedLines = lines.Select(line =>
            line.Contains(issue.Id)
                ? line.Replace("Original Title", "Modified On Disk")
                : line
        ).ToArray();
        await File.WriteAllLinesAsync(issueFile, updatedLines);

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - cache should now reflect the on-disk changes
        var reloaded = await _service.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Title, Is.EqualTo("Modified On Disk"));
    }

    [Test]
    public async Task ReloadFromDiskAsync_PicksUpExternallyAddedIssues()
    {
        // Arrange - create an initial issue to establish the .fleece directory and file
        var existingIssue = await _service.CreateIssueAsync(_tempDir, "Existing Issue", IssueType.Task);

        // Find the issues JSONL file on disk
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        Assert.That(issueFiles, Has.Length.GreaterThan(0));

        var issueFile = issueFiles[0];

        // Read an existing line to use as a template, then create a new issue line
        var lines = await File.ReadAllLinesAsync(issueFile);
        var templateLine = lines.First(l => l.Contains(existingIssue.Id));

        // Create a new issue entry by modifying the template
        var newIssueLine = templateLine
            .Replace(existingIssue.Id, "EXT001")
            .Replace("Existing Issue", "Externally Added Issue");
        await File.AppendAllTextAsync(issueFile, newIssueLine + Environment.NewLine);

        // Verify the external issue is NOT visible before reload
        var beforeReload = await _service.GetIssueAsync(_tempDir, "EXT001");
        Assert.That(beforeReload, Is.Null, "External issue should not be in cache before reload");

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - the externally added issue should now be visible
        var afterReload = await _service.GetIssueAsync(_tempDir, "EXT001");
        Assert.That(afterReload, Is.Not.Null, "External issue should be visible after reload");
        Assert.That(afterReload!.Title, Is.EqualTo("Externally Added Issue"));

        // Original issue should still be there
        var original = await _service.GetIssueAsync(_tempDir, existingIssue.Id);
        Assert.That(original, Is.Not.Null);
    }

    [Test]
    public async Task ReloadFromDiskAsync_PicksUpExternallyDeletedIssues()
    {
        // Arrange - create two issues through the service
        var issue1 = await _service.CreateIssueAsync(_tempDir, "Issue One", IssueType.Task);
        var issue2 = await _service.CreateIssueAsync(_tempDir, "Issue Two", IssueType.Bug);

        // Verify both are in cache
        Assert.That(await _service.GetIssueAsync(_tempDir, issue1.Id), Is.Not.Null);
        Assert.That(await _service.GetIssueAsync(_tempDir, issue2.Id), Is.Not.Null);

        // Remove issue1 from disk by filtering it out of the JSONL file
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        var issueFile = issueFiles[0];
        var lines = await File.ReadAllLinesAsync(issueFile);
        var filteredLines = lines.Where(line => !line.Contains(issue1.Id)).ToArray();
        await File.WriteAllLinesAsync(issueFile, filteredLines);

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - issue1 should no longer be in cache, issue2 should still be there
        var reloadedIssue1 = await _service.GetIssueAsync(_tempDir, issue1.Id);
        Assert.That(reloadedIssue1, Is.Null, "Deleted issue should not be in cache after reload");

        var reloadedIssue2 = await _service.GetIssueAsync(_tempDir, issue2.Id);
        Assert.That(reloadedIssue2, Is.Not.Null, "Remaining issue should still be in cache after reload");
        Assert.That(reloadedIssue2!.Title, Is.EqualTo("Issue Two"));
    }

    #endregion

    #region AddParentAsync Tests

    [Test]
    public async Task AddParentAsync_CreatesParentRelationship()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task AddParentAsync_UpdatesCache()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert - verify cache is updated
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(retrieved.ParentIssues[0].ParentIssue, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task AddParentAsync_NonExistentChild_ThrowsKeyNotFoundException()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.AddParentAsync(_tempDir, "non-existent-id", parent.Id));
    }

    [Test]
    public async Task AddParentAsync_CanAddMultipleParents()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Task);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);

        // Act
        await _service.AddParentAsync(_tempDir, child.Id, parent1.Id);
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent2.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(2));
        var parentIds = updated.ParentIssues.Select(p => p.ParentIssue).ToList();
        Assert.That(parentIds, Does.Contain(parent1.Id));
        Assert.That(parentIds, Does.Contain(parent2.Id));
    }

    [Test]
    public async Task AddParentAsync_AssignsProperSortOrder()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert - DependencyService assigns a proper sort order (not "0")
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].SortOrder, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task AddParentAsync_MultipleChildren_HaveDistinctSortOrders()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        // Act
        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Assert - children should have distinct sort orders with child1 before child2
        var c1 = await _service.GetIssueAsync(_tempDir, child1.Id);
        var c2 = await _service.GetIssueAsync(_tempDir, child2.Id);
        Assert.That(c1!.ParentIssues[0].SortOrder, Is.Not.EqualTo(c2!.ParentIssues[0].SortOrder));
        Assert.That(string.Compare(c1.ParentIssues[0].SortOrder, c2.ParentIssues[0].SortOrder, StringComparison.Ordinal), Is.LessThan(0));
    }

    #endregion

    #region RemoveParentAsync Tests

    [Test]
    public async Task RemoveParentAsync_RemovesParentRelationship()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated.ParentIssues, Is.Empty);
    }

    [Test]
    public async Task RemoveParentAsync_UpdatesCache()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        await _service.RemoveParentAsync(_tempDir, child.Id, parent.Id);

        // Assert - verify cache is updated
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Is.Empty);
    }

    [Test]
    public async Task RemoveParentAsync_NonExistentChild_ThrowsKeyNotFoundException()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveParentAsync(_tempDir, "non-existent-id", parent.Id));
    }

    [Test]
    public async Task RemoveParentAsync_KeepsOtherParents()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Task);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);
        await _service.AddParentAsync(_tempDir, child.Id, parent1.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parent2.Id);

        // Act
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parent1.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parent2.Id));
    }

    #endregion

    #region MoveSeriesSiblingAsync Tests

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_SwapsSortOrdersCorrectly()
    {
        // Arrange - create a parent with two children in series
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        // Add children to parent with sort orders
        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - move child2 up (should swap with child1)
        var updated = await _service.MoveSeriesSiblingAsync(_tempDir, child2.Id, MoveDirection.Up);

        // Assert - child2 should now have the lower sort order
        Assert.That(updated, Is.Not.Null);
        var updatedChild2 = await _service.GetIssueAsync(_tempDir, child2.Id);
        var updatedChild1 = await _service.GetIssueAsync(_tempDir, child1.Id);

        var child2SortOrder = updatedChild2!.ParentIssues[0].SortOrder;
        var child1SortOrder = updatedChild1!.ParentIssues[0].SortOrder;

        // After swapping, child2 should have lower sort order than child1
        Assert.That(string.Compare(child2SortOrder, child1SortOrder, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected child2 ({child2SortOrder}) < child1 ({child1SortOrder}) after move up");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Down_SwapsSortOrdersCorrectly()
    {
        // Arrange - create a parent with two children in series
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        // Add children to parent with sort orders
        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - move child1 down (should swap with child2)
        var updated = await _service.MoveSeriesSiblingAsync(_tempDir, child1.Id, MoveDirection.Down);

        // Assert - child1 should now have the higher sort order
        Assert.That(updated, Is.Not.Null);
        var updatedChild1 = await _service.GetIssueAsync(_tempDir, child1.Id);
        var updatedChild2 = await _service.GetIssueAsync(_tempDir, child2.Id);

        var child1SortOrder = updatedChild1!.ParentIssues[0].SortOrder;
        var child2SortOrder = updatedChild2!.ParentIssues[0].SortOrder;

        // After swapping, child1 should have higher sort order than child2
        Assert.That(string.Compare(child1SortOrder, child2SortOrder, StringComparison.Ordinal), Is.GreaterThan(0),
            $"Expected child1 ({child1SortOrder}) > child2 ({child2SortOrder}) after move down");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Throws_WhenNoParent()
    {
        // Arrange - create an issue without a parent
        var issue = await _service.CreateIssueAsync(_tempDir, "Orphan Issue", IssueType.Task);

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.MoveSeriesSiblingAsync(_tempDir, issue.Id, MoveDirection.Up));

        Assert.That(ex!.Message, Does.Contain("parent"));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Throws_WhenMultipleParents()
    {
        // Arrange - create an issue with multiple parents
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Feature);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child.Id, parent1.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parent2.Id);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.MoveSeriesSiblingAsync(_tempDir, child.Id, MoveDirection.Up));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Throws_WhenAlreadyFirst()
    {
        // Arrange - create a parent with two children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act & Assert - try to move the first child up
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.MoveSeriesSiblingAsync(_tempDir, child1.Id, MoveDirection.Up));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Throws_WhenAlreadyLast()
    {
        // Arrange - create a parent with two children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act & Assert - try to move the last child down
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.MoveSeriesSiblingAsync(_tempDir, child2.Id, MoveDirection.Down));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Throws_WhenIssueNotFound()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.MoveSeriesSiblingAsync(_tempDir, "non-existent", MoveDirection.Up));

        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_UpdatesCache()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act
        await _service.MoveSeriesSiblingAsync(_tempDir, child2.Id, MoveDirection.Up);

        // Assert - cache should immediately reflect the changes
        var cachedChild1 = await _service.GetIssueAsync(_tempDir, child1.Id);
        var cachedChild2 = await _service.GetIssueAsync(_tempDir, child2.Id);

        var child1SortOrder = cachedChild1!.ParentIssues[0].SortOrder;
        var child2SortOrder = cachedChild2!.ParentIssues[0].SortOrder;

        Assert.That(string.Compare(child2SortOrder, child1SortOrder, StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_DependencyServiceAssignsDistinctSortOrders()
    {
        // Arrange - DependencyService assigns distinct sort orders when adding children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Verify distinct sort orders were assigned (DependencyService handles this)
        var c1Before = await _service.GetIssueAsync(_tempDir, child1.Id);
        var c2Before = await _service.GetIssueAsync(_tempDir, child2.Id);
        Assert.That(c1Before!.ParentIssues[0].SortOrder,
            Is.Not.EqualTo(c2Before!.ParentIssues[0].SortOrder));

        // Act - move child2 up
        var updated = await _service.MoveSeriesSiblingAsync(_tempDir, child2.Id, MoveDirection.Up);

        // Assert - child2 should now be before child1
        Assert.That(updated, Is.Not.Null);
        var updatedChild1 = await _service.GetIssueAsync(_tempDir, child1.Id);
        var updatedChild2 = await _service.GetIssueAsync(_tempDir, child2.Id);

        Assert.That(string.Compare(updatedChild2!.ParentIssues[0].SortOrder,
            updatedChild1!.ParentIssues[0].SortOrder, StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Down_DependencyServiceAssignsDistinctSortOrders()
    {
        // Arrange - DependencyService assigns distinct sort orders when adding children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - move child1 down
        var updated = await _service.MoveSeriesSiblingAsync(_tempDir, child1.Id, MoveDirection.Down);

        // Assert - child1 should now be after child2
        Assert.That(updated, Is.Not.Null);
        var updatedChild1 = await _service.GetIssueAsync(_tempDir, child1.Id);
        var updatedChild2 = await _service.GetIssueAsync(_tempDir, child2.Id);

        Assert.That(string.Compare(updatedChild1!.ParentIssues[0].SortOrder,
            updatedChild2!.ParentIssues[0].SortOrder, StringComparison.Ordinal), Is.GreaterThan(0));
    }

    #region Complex Hierarchy MoveSeriesSiblingAsync Tests

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_ThreeSiblings_MovesMiddleCorrectly()
    {
        // Arrange - parent with A(0), B(1), C(2)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var childC = await _service.CreateIssueAsync(_tempDir, "Child C", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childC.Id, parent.Id);

        // Act - move B up -> B, A, C
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        var c = await _service.GetIssueAsync(_tempDir, childC.Id);

        var orderA = a!.ParentIssues[0].SortOrder;
        var orderB = b!.ParentIssues[0].SortOrder;
        var orderC = c!.ParentIssues[0].SortOrder;

        Assert.That(string.Compare(orderB, orderA, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected B ({orderB}) < A ({orderA})");
        Assert.That(string.Compare(orderA, orderC, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected A ({orderA}) < C ({orderC})");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Down_ThreeSiblings_MovesMiddleCorrectly()
    {
        // Arrange - parent with A(0), B(1), C(2)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var childC = await _service.CreateIssueAsync(_tempDir, "Child C", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childC.Id, parent.Id);

        // Act - move B down -> A, C, B
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Down);

        // Assert
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        var c = await _service.GetIssueAsync(_tempDir, childC.Id);

        var orderA = a!.ParentIssues[0].SortOrder;
        var orderB = b!.ParentIssues[0].SortOrder;
        var orderC = c!.ParentIssues[0].SortOrder;

        Assert.That(string.Compare(orderA, orderC, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected A ({orderA}) < C ({orderC})");
        Assert.That(string.Compare(orderC, orderB, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected C ({orderC}) < B ({orderB})");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_FiveSiblings_MoveFromMiddleUpTwice()
    {
        // Arrange - 5 children: A(0), B(1), C(2), D(3), E(4)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var children = new List<Issue>();
        for (var i = 0; i < 5; i++)
        {
            var child = await _service.CreateIssueAsync(_tempDir, $"Child {(char)('A' + i)}", IssueType.Task);
            await _service.AddParentAsync(_tempDir, child.Id, parent.Id);
            children.Add(child);
        }

        // Act - move C (index 2) up twice -> C, A, B, D, E
        await _service.MoveSeriesSiblingAsync(_tempDir, children[2].Id, MoveDirection.Up);
        await _service.MoveSeriesSiblingAsync(_tempDir, children[2].Id, MoveDirection.Up);

        // Assert - C should be first
        var orders = new List<(string Id, string? Order)>();
        foreach (var c in children)
        {
            var updated = await _service.GetIssueAsync(_tempDir, c.Id);
            orders.Add((c.Id, updated!.ParentIssues[0].SortOrder));
        }

        var sorted = orders.OrderBy(o => o.Order ?? "", StringComparer.Ordinal).ToList();
        Assert.That(sorted[0].Id, Is.EqualTo(children[2].Id), "C should be first after moving up twice");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_DeeplyNested_WorksAtAnyDepth()
    {
        // Arrange - Grandparent -> Parent -> A, B
        var grandparent = await _service.CreateIssueAsync(_tempDir, "Grandparent", IssueType.Feature);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Task);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);

        await _service.AddParentAsync(_tempDir, parent.Id, grandparent.Id);
        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);

        // Act - move B up under parent
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);

        var orderA = a!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;
        var orderB = b!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;

        Assert.That(string.Compare(orderB, orderA, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected B ({orderB}) < A ({orderA}) after move up");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_GreatGrandchild_WorksAtAnyDepth()
    {
        // Arrange - Root -> GP -> Parent -> A, B
        var root = await _service.CreateIssueAsync(_tempDir, "Root", IssueType.Feature);
        var gp = await _service.CreateIssueAsync(_tempDir, "Grandparent", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Task);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);

        await _service.AddParentAsync(_tempDir, gp.Id, root.Id);
        await _service.AddParentAsync(_tempDir, parent.Id, gp.Id);
        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);

        // Act
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);

        var orderA = a!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;
        var orderB = b!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;

        Assert.That(string.Compare(orderB, orderA, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected B ({orderB}) < A ({orderA}) at great-grandchild depth");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_SwapTargetHasMultipleParents_PreservesOtherRefs()
    {
        // Arrange - A has single parent P1, B has parents P1 + P2
        var p1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Feature);
        var p2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, p1.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, p1.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, p2.Id);

        // Record B's sort order under P2 before the move
        var bBefore = await _service.GetIssueAsync(_tempDir, childB.Id);
        var p2OrderBefore = bBefore!.ParentIssues.First(p => p.ParentIssue == p2.Id).SortOrder;

        // Act - move A down (swaps with B). A has single parent so this is valid.
        await _service.MoveSeriesSiblingAsync(_tempDir, childA.Id, MoveDirection.Down);

        // Assert - B's P2 ref should be unchanged
        var bAfter = await _service.GetIssueAsync(_tempDir, childB.Id);
        var p2OrderAfter = bAfter!.ParentIssues.First(p => p.ParentIssue == p2.Id).SortOrder;

        Assert.That(p2OrderAfter, Is.EqualTo(p2OrderBefore),
            "B's sort order under P2 should be unchanged after swapping under P1");

        // Also verify the swap happened under P1
        var aAfter = await _service.GetIssueAsync(_tempDir, childA.Id);
        var aP1Order = aAfter!.ParentIssues.First(p => p.ParentIssue == p1.Id).SortOrder;
        var bP1Order = bAfter!.ParentIssues.First(p => p.ParentIssue == p1.Id).SortOrder;

        Assert.That(string.Compare(bP1Order, aP1Order, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected B ({bP1Order}) < A ({aP1Order}) under P1 after move down");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_SiblingWithChildren_OnlyChangesSelectedIssue()
    {
        // Arrange - Parent -> A (has child X, Y), B
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var grandX = await _service.CreateIssueAsync(_tempDir, "Grandchild X", IssueType.Task);
        var grandY = await _service.CreateIssueAsync(_tempDir, "Grandchild Y", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, grandX.Id, childA.Id);
        await _service.AddParentAsync(_tempDir, grandY.Id, childA.Id);

        // Record grandchildren state before move
        var xBefore = await _service.GetIssueAsync(_tempDir, grandX.Id);
        var yBefore = await _service.GetIssueAsync(_tempDir, grandY.Id);

        // Act - move B up
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert - grandchildren X and Y should be completely unchanged
        var xAfter = await _service.GetIssueAsync(_tempDir, grandX.Id);
        var yAfter = await _service.GetIssueAsync(_tempDir, grandY.Id);

        Assert.That(xAfter!.ParentIssues[0].ParentIssue, Is.EqualTo(xBefore!.ParentIssues[0].ParentIssue));
        Assert.That(xAfter.ParentIssues[0].SortOrder, Is.EqualTo(xBefore.ParentIssues[0].SortOrder));
        Assert.That(yAfter!.ParentIssues[0].ParentIssue, Is.EqualTo(yBefore!.ParentIssues[0].ParentIssue));
        Assert.That(yAfter.ParentIssues[0].SortOrder, Is.EqualTo(yBefore.ParentIssues[0].SortOrder));

        // Verify the swap happened
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        Assert.That(string.Compare(b!.ParentIssues[0].SortOrder, a!.ParentIssues[0].SortOrder, StringComparison.Ordinal),
            Is.LessThan(0));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Down_BothSiblingsHaveChildren_OnlySwapsSortOrders()
    {
        // Arrange - Parent -> A (has children), B (has children)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var grandA1 = await _service.CreateIssueAsync(_tempDir, "Grand A1", IssueType.Task);
        var grandA2 = await _service.CreateIssueAsync(_tempDir, "Grand A2", IssueType.Task);
        var grandB1 = await _service.CreateIssueAsync(_tempDir, "Grand B1", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, grandA1.Id, childA.Id);
        await _service.AddParentAsync(_tempDir, grandA2.Id, childA.Id);
        await _service.AddParentAsync(_tempDir, grandB1.Id, childB.Id);

        // Record all grandchildren state before move
        var ga1Before = await _service.GetIssueAsync(_tempDir, grandA1.Id);
        var ga2Before = await _service.GetIssueAsync(_tempDir, grandA2.Id);
        var gb1Before = await _service.GetIssueAsync(_tempDir, grandB1.Id);

        // Act - move A down
        await _service.MoveSeriesSiblingAsync(_tempDir, childA.Id, MoveDirection.Down);

        // Assert - all grandchildren unchanged
        var ga1After = await _service.GetIssueAsync(_tempDir, grandA1.Id);
        var ga2After = await _service.GetIssueAsync(_tempDir, grandA2.Id);
        var gb1After = await _service.GetIssueAsync(_tempDir, grandB1.Id);

        Assert.That(ga1After!.ParentIssues[0].SortOrder, Is.EqualTo(ga1Before!.ParentIssues[0].SortOrder));
        Assert.That(ga2After!.ParentIssues[0].SortOrder, Is.EqualTo(ga2Before!.ParentIssues[0].SortOrder));
        Assert.That(gb1After!.ParentIssues[0].SortOrder, Is.EqualTo(gb1Before!.ParentIssues[0].SortOrder));

        // Verify swap happened
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        Assert.That(string.Compare(a!.ParentIssues[0].SortOrder, b!.ParentIssues[0].SortOrder, StringComparison.Ordinal),
            Is.GreaterThan(0), "A should now sort after B");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_Up_DeeplyNestedWithChildren_OnlyChangesMovedIssue()
    {
        // Arrange - GP -> Parent -> A (has child tree), B (has child tree)
        var gp = await _service.CreateIssueAsync(_tempDir, "Grandparent", IssueType.Feature);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Task);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var subA = await _service.CreateIssueAsync(_tempDir, "Sub A", IssueType.Task);
        var subB = await _service.CreateIssueAsync(_tempDir, "Sub B", IssueType.Task);

        await _service.AddParentAsync(_tempDir, parent.Id, gp.Id);
        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, subA.Id, childA.Id);
        await _service.AddParentAsync(_tempDir, subB.Id, childB.Id);

        var subABefore = await _service.GetIssueAsync(_tempDir, subA.Id);
        var subBBefore = await _service.GetIssueAsync(_tempDir, subB.Id);

        // Act - move B up
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert - subtrees unchanged
        var subAAfter = await _service.GetIssueAsync(_tempDir, subA.Id);
        var subBAfter = await _service.GetIssueAsync(_tempDir, subB.Id);

        Assert.That(subAAfter!.ParentIssues[0].ParentIssue, Is.EqualTo(subABefore!.ParentIssues[0].ParentIssue));
        Assert.That(subAAfter.ParentIssues[0].SortOrder, Is.EqualTo(subABefore.ParentIssues[0].SortOrder));
        Assert.That(subBAfter!.ParentIssues[0].ParentIssue, Is.EqualTo(subBBefore!.ParentIssues[0].ParentIssue));
        Assert.That(subBAfter.ParentIssues[0].SortOrder, Is.EqualTo(subBBefore.ParentIssues[0].SortOrder));

        // Verify swap
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        var orderA = a!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;
        var orderB = b!.ParentIssues.First(p => p.ParentIssue == parent.Id).SortOrder;
        Assert.That(string.Compare(orderB, orderA, StringComparison.Ordinal), Is.LessThan(0));
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_MultipleConsecutiveMoves_Up()
    {
        // Arrange - A, B, C
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var childC = await _service.CreateIssueAsync(_tempDir, "Child C", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childC.Id, parent.Id);

        // Act - move C up twice -> C, A, B
        await _service.MoveSeriesSiblingAsync(_tempDir, childC.Id, MoveDirection.Up);
        await _service.MoveSeriesSiblingAsync(_tempDir, childC.Id, MoveDirection.Up);

        // Assert - order should be C, A, B
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        var c = await _service.GetIssueAsync(_tempDir, childC.Id);

        var orderA = a!.ParentIssues[0].SortOrder;
        var orderB = b!.ParentIssues[0].SortOrder;
        var orderC = c!.ParentIssues[0].SortOrder;

        Assert.That(string.Compare(orderC, orderA, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected C ({orderC}) < A ({orderA})");
        Assert.That(string.Compare(orderA, orderB, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected A ({orderA}) < B ({orderB})");
    }

    [Test]
    public async Task MoveSeriesSiblingAsync_MoveDownThenUp_RoundTrip()
    {
        // Arrange - A, B, C
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var childA = await _service.CreateIssueAsync(_tempDir, "Child A", IssueType.Task);
        var childB = await _service.CreateIssueAsync(_tempDir, "Child B", IssueType.Task);
        var childC = await _service.CreateIssueAsync(_tempDir, "Child C", IssueType.Task);

        await _service.AddParentAsync(_tempDir, childA.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childB.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, childC.Id, parent.Id);

        // Act - move B down then up -> should return to original order A, B, C
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Down);
        await _service.MoveSeriesSiblingAsync(_tempDir, childB.Id, MoveDirection.Up);

        // Assert - order should be A, B, C again
        var a = await _service.GetIssueAsync(_tempDir, childA.Id);
        var b = await _service.GetIssueAsync(_tempDir, childB.Id);
        var c = await _service.GetIssueAsync(_tempDir, childC.Id);

        var orderA = a!.ParentIssues[0].SortOrder;
        var orderB = b!.ParentIssues[0].SortOrder;
        var orderC = c!.ParentIssues[0].SortOrder;

        Assert.That(string.Compare(orderA, orderB, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected A ({orderA}) < B ({orderB})");
        Assert.That(string.Compare(orderB, orderC, StringComparison.Ordinal), Is.LessThan(0),
            $"Expected B ({orderB}) < C ({orderC})");
    }

    #endregion

    #endregion
}
