using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class SetParentTests
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

        _service = new ProjectFleeceService(_mockQueue.Object, new Mock<global::Fleece.Core.Services.Interfaces.IIssueLayoutService>().Object, serviceProvider: null, _mockLogger.Object);
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

    #region SetParentAsync Tests

    [Test]
    public async Task SetParentAsync_ReplacesExistingParents_WhenReplaceFalse()
    {
        // Arrange - B has parent A
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);
        var issueB = await _service.CreateIssueAsync(_tempDir, "Issue B", IssueType.Task);
        var issueC = await _service.CreateIssueAsync(_tempDir, "Issue C", IssueType.Task);
        await _service.AddParentAsync(_tempDir, issueB.Id, issueA.Id);

        // Act - set C as the new parent, replacing existing
        var updated = await _service.SetParentAsync(_tempDir, issueB.Id, issueC.Id, addToExisting: false);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(issueC.Id));
    }

    [Test]
    public async Task SetParentAsync_AddsToExistingParents_WhenAddToExistingTrue()
    {
        // Arrange - B has parent A
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);
        var issueB = await _service.CreateIssueAsync(_tempDir, "Issue B", IssueType.Task);
        var issueC = await _service.CreateIssueAsync(_tempDir, "Issue C", IssueType.Task);
        await _service.AddParentAsync(_tempDir, issueB.Id, issueA.Id);

        // Act - add C as an additional parent
        var updated = await _service.SetParentAsync(_tempDir, issueB.Id, issueC.Id, addToExisting: true);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(2));
        var parentIds = updated.ParentIssues.Select(p => p.ParentIssue).ToList();
        Assert.That(parentIds, Does.Contain(issueA.Id));
        Assert.That(parentIds, Does.Contain(issueC.Id));
    }

    [Test]
    public async Task SetParentAsync_ThrowsInvalidOperationException_WhenCycleWouldBeCreated()
    {
        // Arrange - A -> B hierarchy
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);
        var issueB = await _service.CreateIssueAsync(_tempDir, "Issue B", IssueType.Task);
        await _service.AddParentAsync(_tempDir, issueB.Id, issueA.Id);

        // Act & Assert - try to make A a child of B (would create cycle)
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.SetParentAsync(_tempDir, issueA.Id, issueB.Id, addToExisting: false));

        Assert.That(ex!.Message, Does.Contain("cycle"));
    }

    [Test]
    public async Task SetParentAsync_ThrowsKeyNotFoundException_WhenChildNotFound()
    {
        // Arrange
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.SetParentAsync(_tempDir, "non-existent", issueA.Id, addToExisting: false));
    }

    [Test]
    public async Task SetParentAsync_UpdatesCache()
    {
        // Arrange
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);
        var issueB = await _service.CreateIssueAsync(_tempDir, "Issue B", IssueType.Task);

        // Act
        await _service.SetParentAsync(_tempDir, issueB.Id, issueA.Id, addToExisting: false);

        // Assert - verify cache reflects the change
        var retrieved = await _service.GetIssueAsync(_tempDir, issueB.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(retrieved.ParentIssues[0].ParentIssue, Is.EqualTo(issueA.Id));
    }

    [Test]
    public async Task SetParentAsync_RemovesAllParents_WhenReplacingWithNoExistingAndCalledWithEmptyParent()
    {
        // Arrange - B has parent A
        var issueA = await _service.CreateIssueAsync(_tempDir, "Issue A", IssueType.Task);
        var issueB = await _service.CreateIssueAsync(_tempDir, "Issue B", IssueType.Task);
        var issueC = await _service.CreateIssueAsync(_tempDir, "Issue C", IssueType.Task);
        await _service.AddParentAsync(_tempDir, issueB.Id, issueA.Id);

        // Act - replace existing parents with C
        var updated = await _service.SetParentAsync(_tempDir, issueB.Id, issueC.Id, addToExisting: false);

        // Assert - A should no longer be a parent
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues.Any(p => p.ParentIssue == issueA.Id), Is.False);
    }

    #endregion
}
