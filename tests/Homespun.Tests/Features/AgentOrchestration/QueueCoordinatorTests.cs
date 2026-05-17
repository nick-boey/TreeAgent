using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class ActionQueueCoordinatorTests
{
    private Mock<IProjectFleeceService> _mockFleeceService = null!;
    private Mock<IAgentStartBackgroundService> _mockAgentStartService = null!;
    private Mock<IHubContext<NotificationHub>> _mockNotificationHub = null!;
    private Mock<ILogger<ActionQueueCoordinator>> _mockLogger = null!;
    private Mock<ILoggerFactory> _mockLoggerFactory = null!;
    private ActionQueueCoordinator _coordinator = null!;
    private List<ActionQueueCoordinatorEvent> _emittedEvents = null!;

    private const string ProjectId = "proj1";
    private const string ProjectPath = "/path/to/project";
    private const string DefaultBranch = "main";
    private const int DefaultMaxConcurrency = 5;

    [SetUp]
    public void SetUp()
    {
        _mockFleeceService = new Mock<IProjectFleeceService>();
        _mockAgentStartService = new Mock<IAgentStartBackgroundService>();
        _mockNotificationHub = new Mock<IHubContext<NotificationHub>>();
        _mockLogger = new Mock<ILogger<ActionQueueCoordinator>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        // Set up SignalR mock to avoid null reference
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _mockNotificationHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _coordinator = new ActionQueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            DefaultMaxConcurrency);

        _emittedEvents = new List<ActionQueueCoordinatorEvent>();
        _coordinator.OnEvent += e => _emittedEvents.Add(e);
        _issueRegistry.Clear();
        _configuredIssueIds.Clear();
    }

    private Issue CreateIssue(
        string id,
        ExecutionMode executionMode = ExecutionMode.Series,
        IssueStatus status = IssueStatus.Open)
    {
        return new Issue
        {
            Id = id,
            Title = $"Test Issue {id}",
            Status = status,
            Type = IssueType.Task,
            ExecutionMode = executionMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private void SetupIssueWithChildren(
        string parentId,
        ExecutionMode parentMode,
        params string[] childIds)
    {
        var parentIssue = CreateIssue(parentId, parentMode);
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentIssue);

        var children = childIds.Select(id => CreateIssue(id)).ToList();
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children);

        // Set up each child as having no children by default
        foreach (var childId in childIds)
        {
            _mockFleeceService
                .Setup(f => f.GetIssueAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(children.First(c => c.Id == childId));
            _mockFleeceService
                .Setup(f => f.GetChildrenAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Issue>());
        }
    }

    #region Initial State

    [Test]
    public void GetStatus_ReturnsNull_WhenNoExecutionStarted()
    {
        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Null);
    }

    [Test]
    public void GetActiveQueues_ReturnsEmpty_WhenNoExecutionStarted()
    {
        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Is.Empty);
    }

    #endregion

    #region Series Mode

    [Test]
    public async Task StartExecution_SeriesMode_CreatesSingleQueue()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task StartExecution_SeriesMode_EnqueuesChildrenInOrder()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queue = _coordinator.GetActiveQueues(ProjectId)[0];

        // First child should be current (running)
        Assert.That(queue.CurrentRequest?.IssueId, Is.EqualTo("child1"));
        // Remaining children should be pending in order
        Assert.That(queue.PendingRequests.Select(r => r.IssueId),
            Is.EqualTo(new[] { "child2", "child3" }));
    }

    [Test]
    public async Task StartExecution_SeriesMode_EmitsExecutionStartedEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        Assert.That(_emittedEvents, Has.Some.Matches<ActionQueueCoordinatorEvent>(
            e => e.EventType == ActionQueueCoordinatorEventType.ExecutionStarted
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task StartExecution_SeriesMode_StatusIsRunning()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.Status, Is.EqualTo(ActionQueueCoordinatorStatus.Running));
        Assert.That(status.RootIssueId, Is.EqualTo("root"));
    }

    #endregion

    #region Parallel Mode

    [Test]
    public async Task StartExecution_ParallelMode_CreatesMultipleQueues()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task StartExecution_ParallelMode_EachQueueHasOneChild()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var currentIssueIds = queues
            .Select(q => q.CurrentRequest?.IssueId)
            .OrderBy(id => id)
            .ToList();

        Assert.That(currentIssueIds, Is.EqualTo(new[] { "child1", "child2", "child3" }));
        Assert.That(queues.All(q => q.PendingRequests.Count == 0), Is.True);
    }

    [Test]
    public async Task StartExecution_ParallelMode_EmitsQueueCreatedEvents()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queueCreatedEvents = _emittedEvents
            .Where(e => e.EventType == ActionQueueCoordinatorEventType.QueueCreated)
            .ToList();

        Assert.That(queueCreatedEvents, Has.Count.EqualTo(2));
    }

    #endregion

    #region Max Concurrency Enforcement

    [Test]
    public async Task StartExecution_ParallelMode_RespectsMaxConcurrency()
    {
        var coordinator = new ActionQueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            maxConcurrency: 2);

        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            "child1", "child2", "child3", "child4");

        await coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var status = coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Not.Null);
        // Only 2 queues should be actively running
        Assert.That(status!.RunningQueueCount, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task StartExecution_MaxConcurrency_AllQueuesCreated()
    {
        var coordinator = new ActionQueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            maxConcurrency: 2);

        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            "child1", "child2", "child3", "child4");

        await coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = coordinator.GetActiveQueues(ProjectId);
        // All queues are created, but only maxConcurrency are running
        Assert.That(queues, Has.Count.EqualTo(4));
    }

    #endregion

    #region Nested Hierarchies

    [Test]
    public async Task StartExecution_SeriesWithinParallel_CreatesCorrectStructure()
    {
        // root (parallel) -> child1 (series: grandchild1, grandchild2), child2 (leaf)
        var rootIssue = CreateIssue("root", ExecutionMode.Parallel);
        var child1Issue = CreateIssue("child1", ExecutionMode.Series);
        var child2Issue = CreateIssue("child2", ExecutionMode.Series);
        var grandchild1 = CreateIssue("gc1");
        var grandchild2 = CreateIssue("gc2");

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { child1Issue, child2Issue });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child1Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { grandchild1, grandchild2 });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child2Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        // Set up grandchildren as leaf nodes
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchild1);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchild2);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Parallel root creates 2 queues
        Assert.That(queues, Has.Count.EqualTo(2));

        // One queue should have the series children (gc1, gc2)
        var seriesQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc1");
        Assert.That(seriesQueue, Is.Not.Null);
        Assert.That(seriesQueue!.PendingRequests.Select(r => r.IssueId),
            Is.EqualTo(new[] { "gc2" }));

        // Other queue should have child2 as a leaf
        var leafQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "child2");
        Assert.That(leafQueue, Is.Not.Null);
    }

    [Test]
    public async Task StartExecution_ParallelWithinSeries_CreatesCorrectStructure()
    {
        // root (series) -> child1 (parallel: gc1, gc2), child2 (leaf)
        // Series root: one queue with child1 first. Since child1 has children,
        // the coordinator should expand child1's children into the queue.
        var rootIssue = CreateIssue("root", ExecutionMode.Series);
        var child1Issue = CreateIssue("child1", ExecutionMode.Parallel);
        var child2Issue = CreateIssue("child2", ExecutionMode.Series);
        var gc1 = CreateIssue("gc1");
        var gc2 = CreateIssue("gc2");

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { child1Issue, child2Issue });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child1Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { gc1, gc2 });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child2Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(gc1);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(gc2);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        // Series root creates 1 queue initially for the first series child.
        // child1 is parallel, so it should spawn parallel sub-queues for gc1 and gc2.
        // child2 is deferred until child1's parallel group completes.
        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Should have 2 queues for gc1 and gc2 (parallel expansion of child1)
        Assert.That(queues, Has.Count.EqualTo(2));
    }

    #endregion

    #region Completion Signaling

    [Test]
    public async Task AllQueuesComplete_EmitsAllQueuesCompletedEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _emittedEvents.Clear();

        // Simulate the queue completing by notifying through the queue
        var queues = _coordinator.GetActiveQueues(ProjectId);
        var queue = (ActionQueue)queues[0];
        queue.NotifyCompleted("child1", success: true);

        Assert.That(_emittedEvents, Has.Some.Matches<ActionQueueCoordinatorEvent>(
            e => e.EventType == ActionQueueCoordinatorEventType.AllQueuesCompleted
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task AllQueuesComplete_StatusIsCompleted()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queue = (ActionQueue)_coordinator.GetActiveQueues(ProjectId)[0];
        queue.NotifyCompleted("child1", success: true);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(ActionQueueCoordinatorStatus.Completed));
    }

    [Test]
    public async Task ParallelQueues_AllMustComplete_BeforeSignalingDone()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var queue1 = (ActionQueue)queues.First(q => q.CurrentRequest?.IssueId == "child1");
        var queue2 = (ActionQueue)queues.First(q => q.CurrentRequest?.IssueId == "child2");

        _emittedEvents.Clear();

        // Complete first queue - should not signal all done yet
        queue1.NotifyCompleted("child1", success: true);

        Assert.That(_emittedEvents, Has.None.Matches<ActionQueueCoordinatorEvent>(
            e => e.EventType == ActionQueueCoordinatorEventType.AllQueuesCompleted));

        // Complete second queue - now should signal all done
        queue2.NotifyCompleted("child2", success: true);

        Assert.That(_emittedEvents, Has.Some.Matches<ActionQueueCoordinatorEvent>(
            e => e.EventType == ActionQueueCoordinatorEventType.AllQueuesCompleted));
    }

    #endregion

    #region CancelAll

    [Test]
    public async Task CancelAll_CancelsAllQueues()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _coordinator.CancelAll(ProjectId);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues.All(q => q.State == ActionQueueState.Completed), Is.True);
    }

    [Test]
    public async Task CancelAll_EmitsExecutionCancelledEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _emittedEvents.Clear();

        _coordinator.CancelAll(ProjectId);

        Assert.That(_emittedEvents, Has.Some.Matches<ActionQueueCoordinatorEvent>(
            e => e.EventType == ActionQueueCoordinatorEventType.ExecutionCancelled
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task CancelAll_StatusIsCancelled()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _coordinator.CancelAll(ProjectId);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(ActionQueueCoordinatorStatus.Cancelled));
    }

    [Test]
    public void CancelAll_NoOpWhenNoExecution()
    {
        // Should not throw
        Assert.DoesNotThrow(() => _coordinator.CancelAll(ProjectId));
    }

    #endregion

    #region Leaf Issue (No Children)

    [Test]
    public async Task StartExecution_LeafIssue_CreatesSingleQueueWithIssue()
    {
        var leafIssue = CreateIssue("leaf");
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "leaf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(leafIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "leaf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "leaf",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("leaf"));
    }

    #endregion

    #region Issue Not Found

    [Test]
    public void StartExecution_IssueNotFound_Throws()
    {
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null);

        Assert.ThrowsAsync<KeyNotFoundException>(
            () => _coordinator.StartExecution(ProjectId, "missing",
                ProjectPath, DefaultBranch));
    }

    #endregion

    #region SignalR Broadcasting

    [Test]
    public async Task StartExecution_BroadcastsQueueStatusChanges()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        // Verify SignalR broadcasting was called
        _mockNotificationHub.Verify(
            h => h.Clients, Times.AtLeastOnce);
    }

    #endregion

    #region Nested Series/Non-Leaf Expansion

    /// <summary>
    /// Registry of issues created during test setup. Used to ensure child lists
    /// reference the same Issue objects with correct ExecutionMode.
    /// </summary>
    private readonly Dictionary<string, Issue> _issueRegistry = new();

    /// <summary>
    /// Tracks which issue IDs have been explicitly configured with SetupIssue
    /// (i.e., had their children set up). Prevents parent setup from overriding
    /// children's GetChildrenAsync configuration.
    /// </summary>
    private readonly HashSet<string> _configuredIssueIds = new();

    private Issue GetOrCreateIssue(string id, ExecutionMode mode = ExecutionMode.Series)
    {
        if (_issueRegistry.TryGetValue(id, out var existing))
            return existing;
        var issue = CreateIssue(id, mode);
        _issueRegistry[id] = issue;
        return issue;
    }

    /// <summary>
    /// Sets up an issue with children. Call bottom-up (children before parents)
    /// to ensure child issues have correct ExecutionMode in parent's children list.
    /// </summary>
    private void SetupIssue(string id, ExecutionMode mode, params string[] childIds)
    {
        var issue = GetOrCreateIssue(id, mode);
        _configuredIssueIds.Add(id);

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issue);

        var children = childIds.Select(cid => GetOrCreateIssue(cid)).ToList();
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children);

        // Default each child as leaf only if not already configured by a prior SetupIssue call
        foreach (var childId in childIds)
        {
            if (_configuredIssueIds.Contains(childId))
                continue;

            var child = children.First(c => c.Id == childId);
            _mockFleeceService
                .Setup(f => f.GetIssueAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(child);
            _mockFleeceService
                .Setup(f => f.GetChildrenAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Issue>());
        }
    }

    [Test]
    public async Task NestedSeriesWithinSeries_ContinuationWiredCorrectly()
    {
        // root (series) -> child1 (series: gc1, gc2), child2 (leaf)
        // Expected: queue with [gc1, gc2]. When both complete, child2 runs.
        SetupIssue("child1", ExecutionMode.Series, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Should have 1 queue with gc1 (current) and gc2 (pending)
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("gc1"));
        Assert.That(queues[0].PendingRequests.Select(r => r.IssueId), Is.EqualTo(new[] { "gc2" }));

        // Complete gc1 - gc2 should start (still same queue)
        var queue1 = (ActionQueue)queues[0];
        queue1.NotifyCompleted("gc1", success: true);

        // gc2 is now current
        Assert.That(queue1.CurrentRequest?.IssueId, Is.EqualTo("gc2"));

        // Complete gc2 - continuation should fire, creating queue for child2
        queue1.NotifyCompleted("gc2", success: true);

        // Wait briefly for async continuation
        await Task.Delay(100);

        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        var child2Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "child2");
        Assert.That(child2Queue, Is.Not.Null, "child2 should be running after gc1 and gc2 complete");
    }

    [Test]
    public async Task NestedSeriesWithinSeries_CompletionSignaled()
    {
        // root (series) -> child1 (series: gc1), child2 (leaf)
        SetupIssue("child1", ExecutionMode.Series, "gc1");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        var queue1 = (ActionQueue)_coordinator.GetActiveQueues(ProjectId)[0];
        queue1.NotifyCompleted("gc1", success: true);

        await Task.Delay(100);

        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        var child2Queue = (ActionQueue)allQueues.First(q => q.CurrentRequest?.IssueId == "child2");
        child2Queue.NotifyCompleted("child2", success: true);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(ActionQueueCoordinatorStatus.Completed));
    }

    [Test]
    public async Task NonLeafInMiddleOfSeries_ExpandsCorrectly()
    {
        // root (series) -> child1 (leaf), child2 (parallel: gc1, gc2), child3 (leaf)
        // Expected: queue with [child1]. When child1 completes, child2 expands.
        // When gc1 and gc2 complete, child3 runs.
        SetupIssue("child2", ExecutionMode.Parallel, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Should have 1 queue with child1 only (not child2 as-is)
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child1"));
        Assert.That(queues[0].PendingRequests, Is.Empty,
            "Non-leaf child2 should NOT be enqueued as-is");

        // Complete child1 - should trigger expansion of child2
        var queue1 = (ActionQueue)queues[0];
        queue1.NotifyCompleted("child1", success: true);

        await Task.Delay(100);

        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        // child2 is parallel with gc1, gc2 - should have 2 new queues
        var gc1Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc1");
        var gc2Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc2");
        Assert.That(gc1Queue, Is.Not.Null, "gc1 should be running");
        Assert.That(gc2Queue, Is.Not.Null, "gc2 should be running");
    }

    [Test]
    public async Task NonLeafInMiddleOfSeries_ContinuationAfterParallelCompletes()
    {
        // root (series) -> child1 (leaf), child2 (parallel: gc1, gc2), child3 (leaf)
        SetupIssue("child2", ExecutionMode.Parallel, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        // Complete child1
        var queue1 = (ActionQueue)_coordinator.GetActiveQueues(ProjectId)[0];
        queue1.NotifyCompleted("child1", success: true);
        await Task.Delay(100);

        // Complete gc1 and gc2
        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        var gc1Queue = (ActionQueue)allQueues.First(q => q.CurrentRequest?.IssueId == "gc1");
        var gc2Queue = (ActionQueue)allQueues.First(q => q.CurrentRequest?.IssueId == "gc2");
        gc1Queue.NotifyCompleted("gc1", success: true);
        gc2Queue.NotifyCompleted("gc2", success: true);
        await Task.Delay(100);

        // child3 should now be running
        allQueues = _coordinator.GetActiveQueues(ProjectId);
        var child3Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "child3");
        Assert.That(child3Queue, Is.Not.Null, "child3 should run after gc1 and gc2 complete");
    }

    [Test]
    public async Task NonLeafInMiddleOfSeriesWithinParallel_ExpandsCorrectly()
    {
        // root (parallel) -> childA (series: a1(leaf), a2(parallel: x, y)), childB (leaf)
        SetupIssue("a2", ExecutionMode.Parallel, "x", "y");
        SetupIssue("childA", ExecutionMode.Series, "a1", "a2");
        SetupIssue("root", ExecutionMode.Parallel, "childA", "childB");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        // Initially: queue for a1 (series first), queue for childB
        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(2));

        var a1Queue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "a1");
        var childBQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "childB");
        Assert.That(a1Queue, Is.Not.Null);
        Assert.That(childBQueue, Is.Not.Null);
        Assert.That(a1Queue!.PendingRequests, Is.Empty,
            "Non-leaf a2 should NOT be enqueued as-is");
    }

    [Test]
    public async Task DeepNesting_SeriesParallelSeries_WorksCorrectly()
    {
        // root (series) -> child1 (parallel: gc1 (series: ggc1, ggc2), gc2 (leaf)), child2 (leaf)
        SetupIssue("gc1", ExecutionMode.Series, "ggc1", "ggc2");
        SetupIssue("child1", ExecutionMode.Parallel, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        // child1 is parallel: gc1 (series with ggc1,ggc2) and gc2 (leaf) run in parallel
        // child2 is deferred until child1's parallel group completes
        var queues = _coordinator.GetActiveQueues(ProjectId);

        // Should have 2 queues: one for gc1's series (ggc1 + ggc2), one for gc2
        Assert.That(queues, Has.Count.EqualTo(2));

        var seriesQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "ggc1");
        var gc2Queue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc2");
        Assert.That(seriesQueue, Is.Not.Null);
        Assert.That(gc2Queue, Is.Not.Null);
        Assert.That(seriesQueue!.PendingRequests.Select(r => r.IssueId), Is.EqualTo(new[] { "ggc2" }));
    }

    [Test]
    public async Task DeepNesting_AllComplete_SignalsCompletion()
    {
        // root (series) -> child1 (parallel: gc1 (series: ggc1, ggc2), gc2), child2
        SetupIssue("gc1", ExecutionMode.Series, "ggc1", "ggc2");
        SetupIssue("child1", ExecutionMode.Parallel, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var seriesQueue = (ActionQueue)queues.First(q => q.CurrentRequest?.IssueId == "ggc1");
        var gc2Queue = (ActionQueue)queues.First(q => q.CurrentRequest?.IssueId == "gc2");

        // Complete ggc1 -> ggc2 starts
        seriesQueue.NotifyCompleted("ggc1", success: true);
        Assert.That(seriesQueue.CurrentRequest?.IssueId, Is.EqualTo("ggc2"));

        // Complete gc2
        gc2Queue.NotifyCompleted("gc2", success: true);

        // Complete ggc2 - parallel group should complete, child2 continuation fires
        seriesQueue.NotifyCompleted("ggc2", success: true);
        await Task.Delay(100);

        // child2 should now be running
        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        var child2Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "child2");
        Assert.That(child2Queue, Is.Not.Null, "child2 should run after child1's parallel group completes");

        // Complete child2
        ((ActionQueue)child2Queue!).NotifyCompleted("child2", success: true);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(ActionQueueCoordinatorStatus.Completed));
    }

    [Test]
    public async Task NonLeafInMiddleOfSeries_MultipleNonLeaves()
    {
        // root (series) -> c1(leaf), c2(series: gc1, gc2), c3(leaf)
        // Verifies that non-leaf c2 in the middle is properly expanded
        SetupIssue("c2", ExecutionMode.Series, "gc1", "gc2");
        SetupIssue("root", ExecutionMode.Series, "c1", "c2", "c3");

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        // Initially: 1 queue with c1 only
        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("c1"));
        Assert.That(queues[0].PendingRequests, Is.Empty);

        // Complete c1 -> continuation fires, expands [c2, c3]
        // c2 is series with children, so ExpandSeries handles it
        ((ActionQueue)queues[0]).NotifyCompleted("c1", success: true);
        await Task.Delay(100);

        // c2 should be expanded: queue with gc1, gc2
        var allQueues = _coordinator.GetActiveQueues(ProjectId);
        var gc1Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc1");
        Assert.That(gc1Queue, Is.Not.Null, "gc1 should be running after c1 completes");
        Assert.That(gc1Queue!.PendingRequests.Select(r => r.IssueId), Is.EqualTo(new[] { "gc2" }));

        // Complete gc1 and gc2
        ((ActionQueue)gc1Queue).NotifyCompleted("gc1", success: true);
        ((ActionQueue)gc1Queue).NotifyCompleted("gc2", success: true);
        await Task.Delay(100);

        // c3 should now run
        allQueues = _coordinator.GetActiveQueues(ProjectId);
        var c3Queue = allQueues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "c3");
        Assert.That(c3Queue, Is.Not.Null, "c3 should run after c2 completes");
    }

    #endregion
}
