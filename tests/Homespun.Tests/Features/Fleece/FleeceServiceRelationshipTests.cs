using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceServiceRelationshipTests
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

    #region GetPriorSiblingAsync Tests

    [Test]
    public async Task GetPriorSiblingAsync_WithNoSiblings_ReturnsNull()
    {
        // Arrange - create a single issue with a parent (but no siblings)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, child.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_WithPriorSiblings_ReturnsImmediatePrior()
    {
        // Arrange - create a parent with three children in series
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);
        var child3 = await _service.CreateIssueAsync(_tempDir, "Child 3", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child3.Id, parent.Id);

        // Act - get the prior sibling of child3
        var result = await _service.GetPriorSiblingAsync(_tempDir, child3.Id);

        // Assert - should return child2 (the immediate prior sibling)
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(child2.Id));
    }

    [Test]
    public async Task GetPriorSiblingAsync_WithOnlyLaterSiblings_ReturnsNull()
    {
        // Arrange - create a parent with two children, query the first one
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - get the prior sibling of child1 (which is first)
        var result = await _service.GetPriorSiblingAsync(_tempDir, child1.Id);

        // Assert - should return null because child1 has no prior sibling
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_IssueWithNoParent_ReturnsNull()
    {
        // Arrange - create an issue without a parent
        var orphan = await _service.CreateIssueAsync(_tempDir, "Orphan Issue", IssueType.Task);

        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, orphan.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_NonExistentIssue_ReturnsNull()
    {
        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetChildrenAsync Tests

    [Test]
    public async Task GetChildrenAsync_WithNoChildren_ReturnsEmpty()
    {
        // Arrange - create a parent issue with no children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);

        // Act
        var result = await _service.GetChildrenAsync(_tempDir, parent.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChildrenAsync_WithChildren_ReturnsSortedList()
    {
        // Arrange - create a parent with three children added in a specific order
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);
        var child3 = await _service.CreateIssueAsync(_tempDir, "Child 3", IssueType.Task);

        // DependencyService assigns ascending sort orders in insertion order
        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child3.Id, parent.Id);

        // Act
        var result = await _service.GetChildrenAsync(_tempDir, parent.Id);

        // Assert - should be sorted by sortOrder (insertion order)
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Id, Is.EqualTo(child1.Id));
        Assert.That(result[1].Id, Is.EqualTo(child2.Id));
        Assert.That(result[2].Id, Is.EqualTo(child3.Id));
    }

    [Test]
    public async Task GetChildrenAsync_NonExistentParent_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetChildrenAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region RemoveParentAsync Tests

    [Test]
    public async Task RemoveParentAsync_RemovesSpecificParent()
    {
        // Arrange - create child with two parents
        var parentA = await _service.CreateIssueAsync(_tempDir, "Parent A", IssueType.Feature);
        var parentB = await _service.CreateIssueAsync(_tempDir, "Parent B", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parentA.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parentB.Id);

        // Act - remove parent A
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parentA.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parentB.Id));
    }

    [Test]
    public void RemoveParentAsync_ThrowsWhenChildNotFound()
    {
        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveParentAsync(_tempDir, "non-existent", "some-parent"));
    }

    #endregion

    #region RemoveAllParentsAsync Tests

    [Test]
    public async Task RemoveAllParentsAsync_RemovesAllParents()
    {
        // Arrange - create child with two parents
        var parentA = await _service.CreateIssueAsync(_tempDir, "Parent A", IssueType.Feature);
        var parentB = await _service.CreateIssueAsync(_tempDir, "Parent B", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parentA.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parentB.Id);

        // Act
        var updated = await _service.RemoveAllParentsAsync(_tempDir, child.Id);

        // Assert
        Assert.That(updated.ParentIssues, Is.Empty);
    }

    [Test]
    public void RemoveAllParentsAsync_ThrowsWhenIssueNotFound()
    {
        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveAllParentsAsync(_tempDir, "non-existent"));
    }

    [Test]
    public async Task RemoveAllParentsAsync_UpdatesCache()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        await _service.RemoveAllParentsAsync(_tempDir, child.Id);

        // Assert - verify cache reflects the change
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Is.Empty);
    }

    #endregion
}
