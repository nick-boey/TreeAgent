using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.AgentOrchestration.Services;

internal class ProjectExecution
{
    public required string ProjectId { get; init; }
    public required string RootIssueId { get; init; }
    public required string ProjectPath { get; init; }
    public required string DefaultBranch { get; init; }
    public ActionQueueCoordinatorStatus Status { get; set; } = ActionQueueCoordinatorStatus.Running;
    public List<IActionQueue> Queues { get; } = new();
    public List<ParallelGroup> ParallelGroups { get; } = new();
    public List<SeriesContinuation> SeriesContinuations { get; } = new();
}

internal class ParallelGroup
{
    public required string ParentIssueId { get; init; }
    public List<string> QueueIds { get; } = new();
    public bool IsComplete => QueueIds.All(id => CompletedQueueIds.Contains(id));
    public HashSet<string> CompletedQueueIds { get; } = new();
    public string? ContinuationGroupId { get; init; }
}

internal class SeriesContinuation
{
    public required string GroupId { get; init; }
    public required List<Issue> RemainingChildren { get; init; }
    public ParallelGroup? ParentParallelGroup { get; init; }
}

/// <summary>
/// Coordinates multiple ActionQueues for a project, spawning queues based on
/// the issue hierarchy's execution modes (Series vs Parallel).
/// </summary>
public class ActionQueueCoordinator : IActionQueueCoordinator
{
    private readonly IProjectFleeceService _fleeceService;
    private readonly IAgentStartBackgroundService _agentStartService;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly ILogger<ActionQueueCoordinator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _maxConcurrency;
    private readonly object _lock = new();
    private readonly Dictionary<string, ProjectExecution> _executions = new();

    public ActionQueueCoordinator(
        IProjectFleeceService fleeceService,
        IAgentStartBackgroundService agentStartService,
        IHubContext<NotificationHub> notificationHub,
        ILogger<ActionQueueCoordinator> logger,
        ILoggerFactory loggerFactory)
        : this(fleeceService, agentStartService, notificationHub, logger, loggerFactory, maxConcurrency: 5)
    {
    }

    public ActionQueueCoordinator(
        IProjectFleeceService fleeceService,
        IAgentStartBackgroundService agentStartService,
        IHubContext<NotificationHub> notificationHub,
        ILogger<ActionQueueCoordinator> logger,
        ILoggerFactory loggerFactory,
        int maxConcurrency)
    {
        _fleeceService = fleeceService;
        _agentStartService = agentStartService;
        _notificationHub = notificationHub;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _maxConcurrency = maxConcurrency;
    }

    public event Action<ActionQueueCoordinatorEvent>? OnEvent;

    public async Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        var issue = await _fleeceService.GetIssueAsync(projectPath, issueId, ct);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found.");

        var execution = new ProjectExecution
        {
            ProjectId = projectId,
            RootIssueId = issueId,
            ProjectPath = projectPath,
            DefaultBranch = defaultBranch
        };

        lock (_lock)
        {
            _executions[projectId] = execution;
        }

        EmitEvent(projectId, ActionQueueCoordinatorEventType.ExecutionStarted, issueId: issueId);

        await ExpandIssueIntoQueues(execution, issue, ct);

        _ = BroadcastStatusAsync(projectId);
    }

    public IReadOnlyList<IActionQueue> GetActiveQueues(string projectId)
    {
        lock (_lock)
        {
            return _executions.TryGetValue(projectId, out var execution)
                ? execution.Queues.ToList().AsReadOnly()
                : Array.Empty<IActionQueue>();
        }
    }

    public void CancelAll(string projectId)
    {
        ProjectExecution? execution;
        lock (_lock)
        {
            if (!_executions.TryGetValue(projectId, out execution))
                return;
            execution.Status = ActionQueueCoordinatorStatus.Cancelled;
        }

        foreach (var queue in execution.Queues)
            queue.Cancel();

        EmitEvent(projectId, ActionQueueCoordinatorEventType.ExecutionCancelled);
        _ = BroadcastStatusAsync(projectId);
    }

    public ActionQueueCoordinatorState? GetStatus(string projectId)
    {
        lock (_lock)
        {
            if (!_executions.TryGetValue(projectId, out var execution))
                return null;

            return new ActionQueueCoordinatorState
            {
                ProjectId = projectId,
                Status = execution.Status,
                ActiveQueues = execution.Queues.ToList().AsReadOnly(),
                MaxConcurrency = _maxConcurrency,
                RunningQueueCount = execution.Queues.Count(q =>
                    q.State == ActionQueueState.Running || q.State == ActionQueueState.Blocked),
                RootIssueId = execution.RootIssueId
            };
        }
    }

    private async Task ExpandIssueIntoQueues(ProjectExecution execution, Issue issue, CancellationToken ct)
    {
        var children = await _fleeceService.GetChildrenAsync(execution.ProjectPath, issue.Id, ct);

        if (children.Count == 0)
        {
            var queue = CreateQueue(execution);
            await queue.EnqueueAsync(CreateRequest(execution, issue), ct);
            return;
        }

        if (issue.ExecutionMode == ExecutionMode.Parallel)
        {
            await ExpandParallel(execution, issue, children, null, ct);
        }
        else
        {
            await ExpandSeries(execution, children, ct);
        }
    }

    private async Task ExpandParallel(
        ProjectExecution execution,
        Issue parentIssue,
        IReadOnlyList<Issue> children,
        string? continuationGroupId,
        CancellationToken ct)
    {
        var group = new ParallelGroup
        {
            ParentIssueId = parentIssue.Id,
            ContinuationGroupId = continuationGroupId
        };

        lock (_lock)
        {
            execution.ParallelGroups.Add(group);
        }

        foreach (var child in children)
        {
            var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);

            if (childChildren.Count == 0)
            {
                var queue = CreateQueue(execution);
                group.QueueIds.Add(queue.Id);
                await queue.EnqueueAsync(CreateRequest(execution, child), ct);
            }
            else if (child.ExecutionMode == ExecutionMode.Series)
            {
                await ExpandSeriesIntoSingleQueue(execution, child, childChildren, group, ct);
            }
            else
            {
                await ExpandParallel(execution, child, childChildren, null, ct);
                var innerGroup = execution.ParallelGroups.Last();
                group.QueueIds.AddRange(innerGroup.QueueIds);
            }
        }
    }

    private async Task ExpandSeriesIntoSingleQueue(
        ProjectExecution execution,
        Issue seriesParent,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        CancellationToken ct)
    {
        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        if (firstChildChildren.Count > 0)
        {
            if (children.Count > 1)
            {
                var groupId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = groupId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }

                if (firstChild.ExecutionMode == ExecutionMode.Parallel)
                {
                    await ExpandParallel(execution, firstChild, firstChildChildren, groupId, ct);
                    if (parentGroup != null)
                    {
                        var innerGroup = execution.ParallelGroups.Last();
                        parentGroup.QueueIds.AddRange(innerGroup.QueueIds);
                    }
                }
                else
                {
                    await ExpandSeriesRecursive(execution, firstChild, firstChildChildren, parentGroup, groupId, ct);
                }
            }
            else
            {
                await ExpandIssueIntoQueuesWithParentGroup(execution, firstChild, firstChildChildren, parentGroup, ct);
            }
        }
        else
        {
            var queue = CreateQueue(execution);
            parentGroup?.QueueIds.Add(queue.Id);

            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            for (var i = 1; i < children.Count; i++)
            {
                var child = children[i];
                var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);
                if (childChildren.Count == 0)
                {
                    await queue.EnqueueAsync(CreateRequest(execution, child), ct);
                }
                else
                {
                    var contGroupId = Guid.NewGuid().ToString("N")[..12];
                    lock (_lock)
                    {
                        execution.SeriesContinuations.Add(new SeriesContinuation
                        {
                            GroupId = contGroupId,
                            RemainingChildren = children.Skip(i).ToList(),
                            ParentParallelGroup = parentGroup
                        });
                        var bridgeGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = contGroupId
                        };
                        bridgeGroup.QueueIds.Add(queue.Id);
                        execution.ParallelGroups.Add(bridgeGroup);
                    }
                    parentGroup?.QueueIds.Remove(queue.Id);
                    return;
                }
            }
        }
    }

    private async Task ExpandSeriesRecursive(
        ProjectExecution execution,
        Issue seriesParent,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        string? continuationGroupId,
        CancellationToken ct)
    {
        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        string? innerContinuationId = null;
        if (children.Count > 1 || continuationGroupId != null)
        {
            innerContinuationId = continuationGroupId;
            if (children.Count > 1)
            {
                innerContinuationId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = innerContinuationId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }
            }
        }

        if (firstChildChildren.Count == 0)
        {
            var queue = CreateQueue(execution);
            parentGroup?.QueueIds.Add(queue.Id);
            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            for (var i = 1; i < children.Count; i++)
            {
                await queue.EnqueueAsync(CreateRequest(execution, children[i]), ct);
            }
        }
        else
        {
            await ExpandIssueIntoQueuesWithParentGroup(execution, firstChild, firstChildChildren, parentGroup, ct);
        }
    }

    private async Task ExpandIssueIntoQueuesWithParentGroup(
        ProjectExecution execution,
        Issue issue,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        CancellationToken ct)
    {
        if (issue.ExecutionMode == ExecutionMode.Parallel)
        {
            await ExpandParallel(execution, issue, children, null, ct);
            if (parentGroup != null)
            {
                var innerGroup = execution.ParallelGroups.Last();
                parentGroup.QueueIds.AddRange(innerGroup.QueueIds);
            }
        }
        else
        {
            await ExpandSeriesIntoSingleQueue(execution, issue, children, parentGroup, ct);
        }
    }

    private async Task ExpandSeries(ProjectExecution execution, IReadOnlyList<Issue> children, CancellationToken ct)
    {
        if (children.Count == 0) return;

        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        if (firstChildChildren.Count > 0)
        {
            if (children.Count > 1)
            {
                var groupId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = groupId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }

                if (firstChild.ExecutionMode == ExecutionMode.Parallel)
                {
                    await ExpandParallel(execution, firstChild, firstChildChildren, groupId, ct);
                }
                else
                {
                    var queueCountBefore = execution.Queues.Count;
                    await ExpandSeries(execution, firstChildChildren, ct);
                    lock (_lock)
                    {
                        var innerGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = groupId
                        };
                        for (var qi = queueCountBefore; qi < execution.Queues.Count; qi++)
                            innerGroup.QueueIds.Add(execution.Queues[qi].Id);
                        execution.ParallelGroups.Add(innerGroup);
                    }
                }
            }
            else
            {
                await ExpandIssueIntoQueues(execution, firstChild, ct);
            }
        }
        else
        {
            var queue = CreateQueue(execution);
            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            for (var i = 1; i < children.Count; i++)
            {
                var child = children[i];
                var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);
                if (childChildren.Count == 0)
                {
                    await queue.EnqueueAsync(CreateRequest(execution, child), ct);
                }
                else
                {
                    var contGroupId = Guid.NewGuid().ToString("N")[..12];
                    lock (_lock)
                    {
                        execution.SeriesContinuations.Add(new SeriesContinuation
                        {
                            GroupId = contGroupId,
                            RemainingChildren = children.Skip(i).ToList()
                        });
                        var bridgeGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = contGroupId
                        };
                        bridgeGroup.QueueIds.Add(queue.Id);
                        execution.ParallelGroups.Add(bridgeGroup);
                    }
                    return;
                }
            }
        }
    }

    private ActionQueue CreateQueue(ProjectExecution execution)
    {
        var queueLogger = _loggerFactory.CreateLogger<ActionQueue>();
        var queue = new ActionQueue(_agentStartService, queueLogger);

        bool shouldPause;
        lock (_lock)
        {
            execution.Queues.Add(queue);
            var runningCount = execution.Queues.Count(q =>
                q.State == ActionQueueState.Running || q.State == ActionQueueState.Blocked);
            shouldPause = runningCount >= _maxConcurrency;
        }

        if (shouldPause)
            queue.Pause();

        queue.OnEvent += e => HandleQueueEvent(execution, queue, e);

        EmitEvent(execution.ProjectId, ActionQueueCoordinatorEventType.QueueCreated, queueId: queue.Id);

        return queue;
    }

    private void HandleQueueEvent(ProjectExecution execution, ActionQueue queue, ActionQueueEvent e)
    {
        if (e.EventType == ActionQueueEventType.StateChanged &&
            e.NewState == ActionQueueState.Idle &&
            e.PreviousState == ActionQueueState.Running)
        {
            OnQueueIdle(execution, queue);
        }
    }

    private void OnQueueIdle(ProjectExecution execution, ActionQueue queue)
    {
        if (queue.CurrentRequest != null || queue.PendingRequests.Count > 0)
            return;

        EmitEvent(execution.ProjectId, ActionQueueCoordinatorEventType.QueueCompleted, queueId: queue.Id);

        List<SeriesContinuation>? continuationsToFire = null;
        lock (_lock)
        {
            foreach (var group in execution.ParallelGroups)
            {
                if (group.QueueIds.Contains(queue.Id))
                {
                    group.CompletedQueueIds.Add(queue.Id);
                    if (group.IsComplete && group.ContinuationGroupId != null)
                    {
                        var continuation = execution.SeriesContinuations
                            .FirstOrDefault(c => c.GroupId == group.ContinuationGroupId);
                        if (continuation != null)
                        {
                            execution.SeriesContinuations.Remove(continuation);
                            continuationsToFire ??= new List<SeriesContinuation>();
                            continuationsToFire.Add(continuation);
                        }
                    }
                }
            }
        }

        if (continuationsToFire != null)
        {
            foreach (var continuation in continuationsToFire)
            {
                _ = FireContinuationAsync(execution, continuation);
            }
        }

        ResumeWaitingQueues(execution);
        CheckAllComplete(execution);
    }

    private async Task FireContinuationAsync(ProjectExecution execution, SeriesContinuation continuation)
    {
        int queueCountBefore;
        lock (_lock)
        {
            queueCountBefore = execution.Queues.Count;
        }

        await ExpandSeries(execution, continuation.RemainingChildren, CancellationToken.None);

        if (continuation.ParentParallelGroup != null)
        {
            lock (_lock)
            {
                for (var i = queueCountBefore; i < execution.Queues.Count; i++)
                {
                    continuation.ParentParallelGroup.QueueIds.Add(execution.Queues[i].Id);
                }
            }
        }
    }

    private void ResumeWaitingQueues(ProjectExecution execution)
    {
        lock (_lock)
        {
            var runningCount = execution.Queues.Count(q =>
                q.State == ActionQueueState.Running || q.State == ActionQueueState.Blocked);

            var pausedQueues = execution.Queues
                .Where(q => q.State == ActionQueueState.Idle && q.PendingRequests.Count > 0)
                .ToList();

            foreach (var queue in pausedQueues)
            {
                if (runningCount >= _maxConcurrency)
                    break;

                _ = queue.ResumeAsync();
                runningCount++;
            }
        }
    }

    private void CheckAllComplete(ProjectExecution execution)
    {
        lock (_lock)
        {
            var allDone = execution.Queues.All(q =>
                q.State == ActionQueueState.Idle || q.State == ActionQueueState.Completed);

            var allEmpty = execution.Queues.All(q =>
                q.CurrentRequest == null && q.PendingRequests.Count == 0);

            if (allDone && allEmpty && execution.SeriesContinuations.Count == 0)
            {
                execution.Status = ActionQueueCoordinatorStatus.Completed;
                EmitEvent(execution.ProjectId, ActionQueueCoordinatorEventType.AllQueuesCompleted);
                _ = BroadcastStatusAsync(execution.ProjectId);
            }
        }
    }

    private AgentStartRequest CreateRequest(ProjectExecution execution, Issue issue)
    {
        return new AgentStartRequest
        {
            IssueId = issue.Id,
            ProjectId = execution.ProjectId,
            ProjectLocalPath = execution.ProjectPath,
            ProjectDefaultBranch = execution.DefaultBranch,
            Issue = issue,
            BranchName = $"task/{issue.Id}"
        };
    }

    private void EmitEvent(
        string projectId,
        ActionQueueCoordinatorEventType eventType,
        string? queueId = null,
        string? issueId = null,
        string? error = null)
    {
        OnEvent?.Invoke(new ActionQueueCoordinatorEvent
        {
            ProjectId = projectId,
            EventType = eventType,
            QueueId = queueId,
            IssueId = issueId,
            Error = error
        });
    }

    private async Task BroadcastStatusAsync(string projectId)
    {
        try
        {
            var status = GetStatus(projectId);
            if (status != null)
            {
                await _notificationHub.Clients.All.SendAsync("ActionQueueCoordinatorStatusChanged", status);
                await _notificationHub.Clients.Group($"project-{projectId}")
                    .SendAsync("ActionQueueCoordinatorStatusChanged", status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast action queue coordinator status for project {ProjectId}", projectId);
        }
    }
}
