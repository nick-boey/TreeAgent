using Homespun.Features.Beads.Data;

namespace Homespun.Tests.Features.Beads;

[TestFixture]
public class BeadsQueueItemTests
{
    private const string TestProjectPath = "/test/project";
    private const string TestIssueId = "hsp-abc";

    #region Factory Method Tests

    [Test]
    public void ForCreate_ReturnsCorrectQueueItem()
    {
        // Arrange
        var options = new BeadsCreateOptions
        {
            Title = "New Issue",
            Type = BeadsIssueType.Feature
        };

        // Act
        var item = BeadsQueueItem.ForCreate(TestProjectPath, TestIssueId, options);

        // Assert
        Assert.That(item.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(item.ProjectPath, Is.EqualTo(TestProjectPath));
        Assert.That(item.IssueId, Is.EqualTo(TestIssueId));
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.Create));
        Assert.That(item.CreateOptions, Is.EqualTo(options));
        Assert.That(item.Status, Is.EqualTo(BeadsQueueItemStatus.Pending));
        Assert.That(item.PreviousState, Is.Null);
    }

    [Test]
    public void ForUpdate_ReturnsCorrectQueueItem()
    {
        // Arrange
        var options = new BeadsUpdateOptions { Title = "Updated Title" };
        var previousState = CreateTestIssue();

        // Act
        var item = BeadsQueueItem.ForUpdate(TestProjectPath, TestIssueId, options, previousState);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.Update));
        Assert.That(item.UpdateOptions, Is.EqualTo(options));
        Assert.That(item.PreviousState, Is.EqualTo(previousState));
    }

    [Test]
    public void ForClose_ReturnsCorrectQueueItem()
    {
        // Arrange
        var reason = "Completed";
        var previousState = CreateTestIssue();

        // Act
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId, reason, previousState);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.Close));
        Assert.That(item.Reason, Is.EqualTo(reason));
        Assert.That(item.PreviousState, Is.EqualTo(previousState));
    }

    [Test]
    public void ForReopen_ReturnsCorrectQueueItem()
    {
        // Arrange
        var reason = "Not actually done";
        var previousState = CreateTestIssue();

        // Act
        var item = BeadsQueueItem.ForReopen(TestProjectPath, TestIssueId, reason, previousState);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.Reopen));
        Assert.That(item.Reason, Is.EqualTo(reason));
        Assert.That(item.PreviousState, Is.EqualTo(previousState));
    }

    [Test]
    public void ForDelete_ReturnsCorrectQueueItem()
    {
        // Arrange
        var previousState = CreateTestIssue();

        // Act
        var item = BeadsQueueItem.ForDelete(TestProjectPath, TestIssueId, previousState);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.Delete));
        Assert.That(item.PreviousState, Is.EqualTo(previousState));
    }

    [Test]
    public void ForAddLabel_ReturnsCorrectQueueItem()
    {
        // Arrange
        var label = "urgent";
        var previousState = CreateTestIssue();

        // Act
        var item = BeadsQueueItem.ForAddLabel(TestProjectPath, TestIssueId, label, previousState);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.AddLabel));
        Assert.That(item.Label, Is.EqualTo(label));
        Assert.That(item.PreviousState, Is.EqualTo(previousState));
    }

    [Test]
    public void ForRemoveLabel_ReturnsCorrectQueueItem()
    {
        // Arrange
        var label = "wont-fix";

        // Act
        var item = BeadsQueueItem.ForRemoveLabel(TestProjectPath, TestIssueId, label);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.RemoveLabel));
        Assert.That(item.Label, Is.EqualTo(label));
    }

    [Test]
    public void ForAddDependency_ReturnsCorrectQueueItem()
    {
        // Arrange
        var dependsOnIssueId = "hsp-xyz";
        var dependencyType = "blocks";

        // Act
        var item = BeadsQueueItem.ForAddDependency(TestProjectPath, TestIssueId, dependsOnIssueId, dependencyType);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.AddDependency));
        Assert.That(item.DependsOnIssueId, Is.EqualTo(dependsOnIssueId));
        Assert.That(item.DependencyType, Is.EqualTo(dependencyType));
    }

    [Test]
    public void ForRemoveDependency_ReturnsCorrectQueueItem()
    {
        // Arrange
        var dependsOnIssueId = "hsp-xyz";

        // Act
        var item = BeadsQueueItem.ForRemoveDependency(TestProjectPath, TestIssueId, dependsOnIssueId);

        // Assert
        Assert.That(item.Operation, Is.EqualTo(BeadsOperationType.RemoveDependency));
        Assert.That(item.DependsOnIssueId, Is.EqualTo(dependsOnIssueId));
    }

    #endregion

    #region Queue Item Status Tests

    [Test]
    public void QueueItem_DefaultStatusIsPending()
    {
        // Act
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);

        // Assert
        Assert.That(item.Status, Is.EqualTo(BeadsQueueItemStatus.Pending));
        Assert.That(item.ProcessedAt, Is.Null);
        Assert.That(item.Error, Is.Null);
    }

    [Test]
    public void QueueItem_StatusCanBeUpdated()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);

        // Act
        item.Status = BeadsQueueItemStatus.Processing;

        // Assert
        Assert.That(item.Status, Is.EqualTo(BeadsQueueItemStatus.Processing));
    }

    [Test]
    public void QueueItem_ProcessedAtCanBeSet()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);
        var processedTime = DateTime.UtcNow;

        // Act
        item.ProcessedAt = processedTime;

        // Assert
        Assert.That(item.ProcessedAt, Is.EqualTo(processedTime));
    }

    [Test]
    public void QueueItem_ErrorCanBeSet()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);
        var errorMessage = "Database locked";

        // Act
        item.Status = BeadsQueueItemStatus.Failed;
        item.Error = errorMessage;

        // Assert
        Assert.That(item.Status, Is.EqualTo(BeadsQueueItemStatus.Failed));
        Assert.That(item.Error, Is.EqualTo(errorMessage));
    }

    #endregion

    #region Unique ID Tests

    [Test]
    public void QueueItem_GeneratesUniqueIds()
    {
        // Act
        var item1 = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);
        var item2 = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);

        // Assert
        Assert.That(item1.Id, Is.Not.EqualTo(item2.Id));
    }

    [Test]
    public void QueueItem_CreatedAtIsSet()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;

        // Act
        var item = BeadsQueueItem.ForClose(TestProjectPath, TestIssueId);

        // Assert
        Assert.That(item.CreatedAt, Is.GreaterThanOrEqualTo(beforeCreate));
        Assert.That(item.CreatedAt, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    #endregion

    private static BeadsIssue CreateTestIssue()
    {
        return new BeadsIssue
        {
            Id = TestIssueId,
            Title = "Test Issue",
            Status = BeadsIssueStatus.Open,
            Type = BeadsIssueType.Task,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
