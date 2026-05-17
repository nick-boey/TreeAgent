using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class TaskQueueTests
{
    private Mock<IAgentStartBackgroundService> _mockAgentStartService = null!;
    private Mock<ILogger<TaskQueue>> _mockLogger = null!;
    private TaskQueue _queue = null!;
    private List<TaskQueueEvent> _emittedEvents = null!;

    [SetUp]
    public void SetUp()
    {
        _mockAgentStartService = new Mock<IAgentStartBackgroundService>();
        _mockLogger = new Mock<ILogger<TaskQueue>>();
        _queue = new TaskQueue(_mockAgentStartService.Object, _mockLogger.Object);
        _emittedEvents = new List<TaskQueueEvent>();
        _queue.OnEvent += e => _emittedEvents.Add(e);
    }

    private AgentStartRequest CreateRequest(string issueId = "issue1")
    {
        return new AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = "proj1",
            ProjectLocalPath = "/path/to/project",
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = issueId,
                Title = $"Test Issue {issueId}",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            },
            BranchName = $"task/test-{issueId}+{issueId}"
        };
    }

    #region Initial State

    [Test]
    public void NewQueue_HasIdleState()
    {
        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
    }

    [Test]
    public void NewQueue_HasUniqueId()
    {
        var queue2 = new TaskQueue(_mockAgentStartService.Object, _mockLogger.Object);
        Assert.That(_queue.Id, Is.Not.EqualTo(queue2.Id));
        Assert.That(_queue.Id, Is.Not.Empty);
    }

    [Test]
    public void NewQueue_HasNoPendingRequests()
    {
        Assert.That(_queue.PendingRequests, Is.Empty);
    }

    [Test]
    public void NewQueue_HasNoCurrentRequest()
    {
        Assert.That(_queue.CurrentRequest, Is.Null);
    }

    [Test]
    public void NewQueue_HasEmptyHistory()
    {
        Assert.That(_queue.History, Is.Empty);
    }

    #endregion

    #region Enqueue and State Transitions

    [Test]
    public async Task EnqueueAsync_TransitionsFromIdleToRunning()
    {
        var request = CreateRequest();

        await _queue.EnqueueAsync(request);

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
    }

    [Test]
    public async Task EnqueueAsync_SetsCurrentRequest()
    {
        var request = CreateRequest();

        await _queue.EnqueueAsync(request);

        Assert.That(_queue.CurrentRequest, Is.EqualTo(request));
    }

    [Test]
    public async Task EnqueueAsync_DelegatesFirstRequestToAgentStartService()
    {
        var request = CreateRequest();

        await _queue.EnqueueAsync(request);

        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request), Times.Once);
    }

    [Test]
    public async Task EnqueueAsync_EmitsStateChangedAndIssueStartedEvents()
    {
        var request = CreateRequest();

        await _queue.EnqueueAsync(request);

        Assert.That(_emittedEvents, Has.Count.EqualTo(2));

        var stateEvent = _emittedEvents[0];
        Assert.That(stateEvent.EventType, Is.EqualTo(TaskQueueEventType.StateChanged));
        Assert.That(stateEvent.PreviousState, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(stateEvent.NewState, Is.EqualTo(TaskQueueState.Running));

        var startEvent = _emittedEvents[1];
        Assert.That(startEvent.EventType, Is.EqualTo(TaskQueueEventType.IssueStarted));
        Assert.That(startEvent.IssueId, Is.EqualTo(request.IssueId));
    }

    [Test]
    public async Task EnqueueAsync_SecondRequest_AddsToPending()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        Assert.That(_queue.CurrentRequest, Is.EqualTo(request1));
        Assert.That(_queue.PendingRequests, Has.Count.EqualTo(1));
        Assert.That(_queue.PendingRequests[0], Is.EqualTo(request2));
    }

    [Test]
    public async Task EnqueueAsync_SecondRequest_DoesNotDelegateToService()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        // Only the first request should be delegated
        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request1), Times.Once);
        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request2), Times.Never);
    }

    [Test]
    public async Task EnqueueAsync_WhenCompleted_ThrowsInvalidOperation()
    {
        _queue.Cancel();

        var request = CreateRequest();
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _queue.EnqueueAsync(request));
    }

    #endregion

    #region Dequeue

    [Test]
    public async Task Dequeue_RemovesPendingRequest()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");
        var request3 = CreateRequest("issue3");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);
        await _queue.EnqueueAsync(request3);

        var removed = _queue.Dequeue("issue2");

        Assert.That(removed, Is.True);
        Assert.That(_queue.PendingRequests, Has.Count.EqualTo(1));
        Assert.That(_queue.PendingRequests[0].IssueId, Is.EqualTo("issue3"));
    }

    [Test]
    public async Task Dequeue_ReturnsFalse_WhenIssueNotFound()
    {
        var request = CreateRequest("issue1");
        await _queue.EnqueueAsync(request);

        var removed = _queue.Dequeue("nonexistent");

        Assert.That(removed, Is.False);
    }

    [Test]
    public async Task Dequeue_CannotRemoveCurrentRequest()
    {
        var request = CreateRequest("issue1");
        await _queue.EnqueueAsync(request);

        var removed = _queue.Dequeue("issue1");

        Assert.That(removed, Is.False);
        Assert.That(_queue.CurrentRequest, Is.EqualTo(request));
    }

    [Test]
    public async Task Dequeue_MaintainsOrder()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");
        var request3 = CreateRequest("issue3");
        var request4 = CreateRequest("issue4");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);
        await _queue.EnqueueAsync(request3);
        await _queue.EnqueueAsync(request4);

        _queue.Dequeue("issue3");

        Assert.That(_queue.PendingRequests.Select(r => r.IssueId),
            Is.EqualTo(new[] { "issue2", "issue4" }));
    }

    #endregion

    #region NotifyCompleted - Issue Completion and Queue Progression

    [Test]
    public async Task NotifyCompleted_TransitionsToIdle_WhenQueueEmpty()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyCompleted(request.IssueId, success: true);

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.CurrentRequest, Is.Null);
    }

    [Test]
    public async Task NotifyCompleted_StartsNextIssue_WhenMoreQueued()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);
        _emittedEvents.Clear();

        _queue.NotifyCompleted("issue1", success: true);

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
        Assert.That(_queue.CurrentRequest, Is.EqualTo(request2));
        Assert.That(_queue.PendingRequests, Is.Empty);

        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request2), Times.Once);
    }

    [Test]
    public async Task NotifyCompleted_AddsToHistory_OnSuccess()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);

        _queue.NotifyCompleted(request.IssueId, success: true);

        Assert.That(_queue.History, Has.Count.EqualTo(1));
        Assert.That(_queue.History[0].IssueId, Is.EqualTo(request.IssueId));
        Assert.That(_queue.History[0].Success, Is.True);
        Assert.That(_queue.History[0].Error, Is.Null);
    }

    [Test]
    public async Task NotifyCompleted_AddsToHistory_OnFailure()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);

        _queue.NotifyCompleted(request.IssueId, success: false, error: "Clone failed");

        Assert.That(_queue.History, Has.Count.EqualTo(1));
        Assert.That(_queue.History[0].Success, Is.False);
        Assert.That(_queue.History[0].Error, Is.EqualTo("Clone failed"));
    }

    [Test]
    public async Task NotifyCompleted_EmitsIssueCompletedAndStateChangedEvents()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyCompleted(request.IssueId, success: true);

        Assert.That(_emittedEvents, Has.Count.EqualTo(2));

        var completedEvent = _emittedEvents[0];
        Assert.That(completedEvent.EventType, Is.EqualTo(TaskQueueEventType.IssueCompleted));
        Assert.That(completedEvent.IssueId, Is.EqualTo(request.IssueId));

        var stateEvent = _emittedEvents[1];
        Assert.That(stateEvent.EventType, Is.EqualTo(TaskQueueEventType.StateChanged));
        Assert.That(stateEvent.NewState, Is.EqualTo(TaskQueueState.Idle));
    }

    [Test]
    public async Task NotifyCompleted_EmitsIssueFailedEvent_OnFailure()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyCompleted(request.IssueId, success: false, error: "Timeout");

        var failedEvent = _emittedEvents[0];
        Assert.That(failedEvent.EventType, Is.EqualTo(TaskQueueEventType.IssueFailed));
        Assert.That(failedEvent.IssueId, Is.EqualTo(request.IssueId));
        Assert.That(failedEvent.Error, Is.EqualTo("Timeout"));
    }

    [Test]
    public async Task NotifyCompleted_IgnoresUnknownIssueId()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyCompleted("unknown-issue", success: true);

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
        Assert.That(_emittedEvents, Is.Empty);
    }

    #endregion

    #region Pause and Resume

    [Test]
    public async Task Pause_PreventsNextIssueFromStarting()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        _queue.Pause();

        // Complete current issue - queue should go Idle, not start issue2
        _queue.NotifyCompleted("issue1", success: true);

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.CurrentRequest, Is.Null);
        Assert.That(_queue.PendingRequests, Has.Count.EqualTo(1));

        // issue2 should not have been started
        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request2), Times.Never);
    }

    [Test]
    public async Task ResumeAsync_StartsNextPendingIssue()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        _queue.Pause();
        _queue.NotifyCompleted("issue1", success: true);

        await _queue.ResumeAsync();

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
        Assert.That(_queue.CurrentRequest, Is.EqualTo(request2));

        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request2), Times.Once);
    }

    [Test]
    public async Task ResumeAsync_StaysIdle_WhenNoPendingRequests()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _queue.Pause();
        _queue.NotifyCompleted(request.IssueId, success: true);

        await _queue.ResumeAsync();

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.CurrentRequest, Is.Null);
    }

    #endregion

    #region Cancel

    [Test]
    public async Task Cancel_TransitionsToCompleted()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);
        _emittedEvents.Clear();

        _queue.Cancel();

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Completed));
    }

    [Test]
    public async Task Cancel_ClearsPendingRequests()
    {
        var request1 = CreateRequest("issue1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        _queue.Cancel();

        Assert.That(_queue.PendingRequests, Is.Empty);
    }

    [Test]
    public async Task Cancel_DoesNotClearCurrentRequest()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);

        _queue.Cancel();

        // Current request remains - it was already dispatched
        Assert.That(_queue.CurrentRequest, Is.EqualTo(request));
    }

    [Test]
    public void Cancel_FromIdle_TransitionsToCompleted()
    {
        _queue.Cancel();

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Completed));
    }

    [Test]
    public async Task Cancel_EmitsStateChangedEvent()
    {
        await _queue.EnqueueAsync(CreateRequest());
        _emittedEvents.Clear();

        _queue.Cancel();

        var stateEvent = _emittedEvents.Single(e => e.EventType == TaskQueueEventType.StateChanged);
        Assert.That(stateEvent.PreviousState, Is.EqualTo(TaskQueueState.Running));
        Assert.That(stateEvent.NewState, Is.EqualTo(TaskQueueState.Completed));
    }

    #endregion

    #region Blocked State

    [Test]
    public async Task NotifyBlocked_TransitionsFromRunningToBlocked()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyBlocked(request.IssueId, "Waiting on dependency issue123");

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Blocked));
    }

    [Test]
    public async Task NotifyBlocked_EmitsStateChangedEvent()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyBlocked(request.IssueId, "Blocked by dependency");

        var stateEvent = _emittedEvents.Single(e => e.EventType == TaskQueueEventType.StateChanged);
        Assert.That(stateEvent.PreviousState, Is.EqualTo(TaskQueueState.Running));
        Assert.That(stateEvent.NewState, Is.EqualTo(TaskQueueState.Blocked));
    }

    [Test]
    public async Task UnblockAsync_TransitionsFromBlockedToRunning()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _queue.NotifyBlocked(request.IssueId, "Blocked");
        _emittedEvents.Clear();

        await _queue.UnblockAsync();

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
    }

    [Test]
    public async Task UnblockAsync_RestartsCurrentIssue()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _queue.NotifyBlocked(request.IssueId, "Blocked");

        await _queue.UnblockAsync();

        // Should re-delegate the current request to the agent start service
        _mockAgentStartService.Verify(
            x => x.QueueAgentStartAsync(request), Times.Exactly(2));
    }

    [Test]
    public async Task UnblockAsync_EmitsStateChangedAndIssueStartedEvents()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _queue.NotifyBlocked(request.IssueId, "Blocked");
        _emittedEvents.Clear();

        await _queue.UnblockAsync();

        Assert.That(_emittedEvents, Has.Count.EqualTo(2));

        var stateEvent = _emittedEvents[0];
        Assert.That(stateEvent.EventType, Is.EqualTo(TaskQueueEventType.StateChanged));
        Assert.That(stateEvent.PreviousState, Is.EqualTo(TaskQueueState.Blocked));
        Assert.That(stateEvent.NewState, Is.EqualTo(TaskQueueState.Running));

        var startEvent = _emittedEvents[1];
        Assert.That(startEvent.EventType, Is.EqualTo(TaskQueueEventType.IssueStarted));
        Assert.That(startEvent.IssueId, Is.EqualTo(request.IssueId));
    }

    [Test]
    public async Task NotifyBlocked_IgnoresUnknownIssueId()
    {
        var request = CreateRequest();
        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyBlocked("unknown", "Blocked");

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Running));
        Assert.That(_emittedEvents, Is.Empty);
    }

    #endregion

    #region Ordering

    [Test]
    public async Task ProcessesIssuesInFifoOrder()
    {
        var requests = Enumerable.Range(1, 5)
            .Select(i => CreateRequest($"issue{i}"))
            .ToList();

        foreach (var req in requests)
            await _queue.EnqueueAsync(req);

        // First should be current
        Assert.That(_queue.CurrentRequest!.IssueId, Is.EqualTo("issue1"));

        // Complete each and verify next starts
        for (var i = 0; i < requests.Count - 1; i++)
        {
            _queue.NotifyCompleted(requests[i].IssueId, success: true);
            Assert.That(_queue.CurrentRequest!.IssueId, Is.EqualTo(requests[i + 1].IssueId));
        }

        // Complete last
        _queue.NotifyCompleted(requests[^1].IssueId, success: true);
        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.History, Has.Count.EqualTo(5));
    }

    #endregion
}
