using Homespun.Features.Beads.Data;
using Homespun.Features.Beads.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.Beads;

[TestFixture]
public class BeadsQueueServiceTests
{
    private BeadsQueueService _service = null!;
    private IOptions<BeadsDatabaseOptions> _options = null!;
    private Mock<ILogger<BeadsQueueService>> _loggerMock = null!;

    private const string TestProjectPath1 = "/test/project1";
    private const string TestProjectPath2 = "/test/project2";
    private const string TestIssueId = "hsp-abc";

    [SetUp]
    public void SetUp()
    {
        _options = Options.Create(new BeadsDatabaseOptions
        {
            DebounceIntervalMs = 100, // Short for testing
            MaxHistoryItems = 10
        });
        _loggerMock = new Mock<ILogger<BeadsQueueService>>();
        _service = new BeadsQueueService(_options, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    #region Enqueue Tests

    [Test]
    public void Enqueue_AddsItemToPendingQueue()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);

        // Act
        _service.Enqueue(item);

        // Assert
        var pending = _service.GetPendingItems(TestProjectPath1);
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].Id, Is.EqualTo(item.Id));
    }

    [Test]
    public void Enqueue_MultipleItems_AddsAllToQueue()
    {
        // Arrange
        var item1 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1");
        var item2 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-2");
        var item3 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-3");

        // Act
        _service.Enqueue(item1);
        _service.Enqueue(item2);
        _service.Enqueue(item3);

        // Assert
        var pending = _service.GetPendingItems(TestProjectPath1);
        Assert.That(pending, Has.Count.EqualTo(3));
    }

    [Test]
    public void Enqueue_DifferentProjects_KeepsSeparateQueues()
    {
        // Arrange
        var item1 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1");
        var item2 = BeadsQueueItem.ForClose(TestProjectPath2, "hsp-2");

        // Act
        _service.Enqueue(item1);
        _service.Enqueue(item2);

        // Assert
        Assert.That(_service.GetPendingItems(TestProjectPath1), Has.Count.EqualTo(1));
        Assert.That(_service.GetPendingItems(TestProjectPath2), Has.Count.EqualTo(1));
    }

    [Test]
    public void Enqueue_FiresItemEnqueuedEvent()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);
        BeadsQueueItem? receivedItem = null;
        _service.ItemEnqueued += i => receivedItem = i;

        // Act
        _service.Enqueue(item);

        // Assert
        Assert.That(receivedItem, Is.Not.Null);
        Assert.That(receivedItem!.Id, Is.EqualTo(item.Id));
    }

    [Test]
    public void Enqueue_UpdatesLastModificationTime()
    {
        // Arrange
        var beforeEnqueue = DateTime.UtcNow;
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);

        // Act
        _service.Enqueue(item);

        // Assert
        var lastModTime = _service.GetLastModificationTime(TestProjectPath1);
        Assert.That(lastModTime, Is.Not.Null);
        Assert.That(lastModTime!.Value, Is.GreaterThanOrEqualTo(beforeEnqueue));
    }

    #endregion

    #region Debounce Tests

    [Test]
    public void IsDebouncing_AfterEnqueue_ReturnsTrue()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);

        // Act
        _service.Enqueue(item);

        // Assert
        Assert.That(_service.IsDebouncing(TestProjectPath1), Is.True);
    }

    [Test]
    public void IsDebouncing_NoItems_ReturnsFalse()
    {
        // Assert
        Assert.That(_service.IsDebouncing(TestProjectPath1), Is.False);
    }

    [Test]
    public async Task DebounceCompleted_FiresAfterInterval()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);
        string? completedProjectPath = null;
        _service.DebounceCompleted += p => completedProjectPath = p;

        // Act
        _service.Enqueue(item);
        await Task.Delay(200); // Wait longer than debounce interval (100ms)

        // Assert
        Assert.That(completedProjectPath, Is.EqualTo(TestProjectPath1));
    }

    [Test]
    public async Task DebounceCompleted_ResetsOnNewEnqueue()
    {
        // Arrange
        var item1 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1");
        var item2 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-2");
        int completedCount = 0;
        _service.DebounceCompleted += _ => completedCount++;

        // Act - enqueue, wait half the debounce, enqueue again
        _service.Enqueue(item1);
        await Task.Delay(50); // Half of 100ms debounce
        _service.Enqueue(item2);
        await Task.Delay(50); // Total 100ms but timer should have reset

        // Assert - debounce should not have fired yet
        Assert.That(completedCount, Is.EqualTo(0));

        // Wait for debounce to complete
        await Task.Delay(100);
        Assert.That(completedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task IsDebouncing_AfterDebounceCompletes_ReturnsFalse()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);
        _service.Enqueue(item);

        // Wait for debounce to complete
        await Task.Delay(200);

        // Assert - no longer debouncing (though items still pending)
        Assert.That(_service.IsDebouncing(TestProjectPath1), Is.False);
    }

    #endregion

    #region Processing State Tests

    [Test]
    public void MarkAsProcessing_SetsProcessingState()
    {
        // Act
        _service.MarkAsProcessing(TestProjectPath1);

        // Assert
        Assert.That(_service.IsProcessing(TestProjectPath1), Is.True);
    }

    [Test]
    public void MarkAsProcessingComplete_ClearsProcessingState()
    {
        // Arrange
        _service.MarkAsProcessing(TestProjectPath1);

        // Act
        _service.MarkAsProcessingComplete(TestProjectPath1, success: true);

        // Assert
        Assert.That(_service.IsProcessing(TestProjectPath1), Is.False);
    }

    [Test]
    public void MarkAsProcessingComplete_FiresQueueProcessingCompletedEvent()
    {
        // Arrange
        string? eventProjectPath = null;
        bool? eventSuccess = null;
        _service.QueueProcessingCompleted += (p, s) =>
        {
            eventProjectPath = p;
            eventSuccess = s;
        };
        _service.MarkAsProcessing(TestProjectPath1);

        // Act
        _service.MarkAsProcessingComplete(TestProjectPath1, success: true);

        // Assert
        Assert.That(eventProjectPath, Is.EqualTo(TestProjectPath1));
        Assert.That(eventSuccess, Is.True);
    }

    [Test]
    public async Task Enqueue_WhileProcessing_DoesNotStartNewDebounce()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);
        int debounceCount = 0;
        _service.DebounceCompleted += _ => debounceCount++;
        _service.MarkAsProcessing(TestProjectPath1);

        // Act
        _service.Enqueue(item);
        await Task.Delay(200);

        // Assert - debounce should not fire while processing
        Assert.That(debounceCount, Is.EqualTo(0));
    }

    #endregion

    #region Clear and GetAll Tests

    [Test]
    public void ClearPendingItems_RemovesAllPendingForProject()
    {
        // Arrange
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1"));
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-2"));

        // Act
        _service.ClearPendingItems(TestProjectPath1);

        // Assert
        Assert.That(_service.GetPendingItems(TestProjectPath1), Is.Empty);
    }

    [Test]
    public void ClearPendingItems_DoesNotAffectOtherProjects()
    {
        // Arrange
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1"));
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath2, "hsp-2"));

        // Act
        _service.ClearPendingItems(TestProjectPath1);

        // Assert
        Assert.That(_service.GetPendingItems(TestProjectPath1), Is.Empty);
        Assert.That(_service.GetPendingItems(TestProjectPath2), Has.Count.EqualTo(1));
    }

    [Test]
    public void GetAllPendingItems_ReturnsItemsFromAllProjects()
    {
        // Arrange
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1"));
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath2, "hsp-2"));

        // Act
        var allPending = _service.GetAllPendingItems();

        // Assert
        Assert.That(allPending, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetProjectsWithPendingItems_ReturnsCorrectProjects()
    {
        // Arrange
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1"));
        _service.Enqueue(BeadsQueueItem.ForClose(TestProjectPath2, "hsp-2"));

        // Act
        var projects = _service.GetProjectsWithPendingItems();

        // Assert
        Assert.That(projects, Has.Count.EqualTo(2));
        Assert.That(projects, Contains.Item(TestProjectPath1));
        Assert.That(projects, Contains.Item(TestProjectPath2));
    }

    #endregion

    #region History Tests

    [Test]
    public void AddToHistory_AddsItemToHistory()
    {
        // Arrange
        var item = BeadsQueueItem.ForClose(TestProjectPath1, TestIssueId);
        item.Status = BeadsQueueItemStatus.Completed;

        // Act
        _service.AddToHistory(item);

        // Assert
        var history = _service.GetCompletedHistory(TestProjectPath1);
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].Id, Is.EqualTo(item.Id));
    }

    [Test]
    public void GetCompletedHistory_ReturnsNewestFirst()
    {
        // Arrange
        var item1 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1");
        var item2 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-2");
        var item3 = BeadsQueueItem.ForClose(TestProjectPath1, "hsp-3");

        // Act
        _service.AddToHistory(item1);
        _service.AddToHistory(item2);
        _service.AddToHistory(item3);

        // Assert
        var history = _service.GetCompletedHistory(TestProjectPath1);
        Assert.That(history[0].IssueId, Is.EqualTo("hsp-3"));
        Assert.That(history[1].IssueId, Is.EqualTo("hsp-2"));
        Assert.That(history[2].IssueId, Is.EqualTo("hsp-1"));
    }

    [Test]
    public void GetCompletedHistory_RespectsLimit()
    {
        // Arrange - add more than limit
        for (int i = 0; i < 15; i++)
        {
            _service.AddToHistory(BeadsQueueItem.ForClose(TestProjectPath1, $"hsp-{i}"));
        }

        // Act
        var history = _service.GetCompletedHistory(TestProjectPath1, limit: 5);

        // Assert
        Assert.That(history, Has.Count.EqualTo(5));
    }

    [Test]
    public void AddToHistory_TrimsByMaxHistoryItems()
    {
        // Arrange - options set MaxHistoryItems to 10
        for (int i = 0; i < 15; i++)
        {
            _service.AddToHistory(BeadsQueueItem.ForClose(TestProjectPath1, $"hsp-{i}"));
        }

        // Act
        var history = _service.GetCompletedHistory(TestProjectPath1, limit: 100);

        // Assert - should be trimmed to MaxHistoryItems (10)
        Assert.That(history, Has.Count.EqualTo(10));
    }

    [Test]
    public void GetCompletedHistory_DifferentProjects_ReturnsSeparateHistory()
    {
        // Arrange
        _service.AddToHistory(BeadsQueueItem.ForClose(TestProjectPath1, "hsp-1"));
        _service.AddToHistory(BeadsQueueItem.ForClose(TestProjectPath2, "hsp-2"));

        // Act & Assert
        Assert.That(_service.GetCompletedHistory(TestProjectPath1), Has.Count.EqualTo(1));
        Assert.That(_service.GetCompletedHistory(TestProjectPath2), Has.Count.EqualTo(1));
    }

    #endregion

    #region Configuration Tests

    [Test]
    public void DebounceInterval_ReturnsConfiguredValue()
    {
        // Assert
        Assert.That(_service.DebounceInterval, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
    }

    #endregion
}
