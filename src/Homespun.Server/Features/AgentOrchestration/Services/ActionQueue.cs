namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// A sequential execution pipeline that processes actions one at a time
/// by delegating to AgentStartBackgroundService.
/// </summary>
public class ActionQueue : IActionQueue
{
    private readonly IAgentStartBackgroundService _agentStartService;
    private readonly ILogger<ActionQueue> _logger;
    private readonly List<AgentStartRequest> _pendingRequests = new();
    private readonly List<ActionQueueEntry> _history = new();
    private readonly object _lock = new();

    private bool _paused;
    private DateTimeOffset? _currentStartedAt;

    public ActionQueue(IAgentStartBackgroundService agentStartService, ILogger<ActionQueue> logger)
    {
        _agentStartService = agentStartService;
        _logger = logger;
        Id = Guid.NewGuid().ToString("N")[..12];
    }

    public string Id { get; }
    public ActionQueueState State { get; private set; } = ActionQueueState.Idle;
    public AgentStartRequest? CurrentRequest { get; private set; }
    public IReadOnlyList<AgentStartRequest> PendingRequests
    {
        get { lock (_lock) return _pendingRequests.ToList().AsReadOnly(); }
    }
    public IReadOnlyList<ActionQueueEntry> History
    {
        get { lock (_lock) return _history.ToList().AsReadOnly(); }
    }

    public event Action<ActionQueueEvent>? OnEvent;

    public async Task EnqueueAsync(AgentStartRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (State == ActionQueueState.Completed)
                throw new InvalidOperationException("Cannot enqueue to a completed queue.");

            if (CurrentRequest == null && !_paused)
            {
                CurrentRequest = request;
                _currentStartedAt = DateTimeOffset.UtcNow;
                TransitionState(ActionQueueState.Running);
            }
            else
            {
                _pendingRequests.Add(request);
                return;
            }
        }

        EmitEvent(ActionQueueEventType.IssueStarted, request.IssueId);
        await StartRequestAsync(request);
    }

    public bool Dequeue(string issueId)
    {
        lock (_lock)
        {
            if (CurrentRequest?.IssueId == issueId)
                return false;

            var index = _pendingRequests.FindIndex(r => r.IssueId == issueId);
            if (index < 0)
                return false;

            _pendingRequests.RemoveAt(index);
            return true;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _paused = true;
            _logger.LogInformation("ActionQueue {QueueId} paused", Id);
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        AgentStartRequest? nextRequest = null;

        lock (_lock)
        {
            _paused = false;

            if (State == ActionQueueState.Idle && _pendingRequests.Count > 0)
            {
                nextRequest = _pendingRequests[0];
                _pendingRequests.RemoveAt(0);
                CurrentRequest = nextRequest;
                _currentStartedAt = DateTimeOffset.UtcNow;
                TransitionState(ActionQueueState.Running);
            }
        }

        if (nextRequest != null)
        {
            EmitEvent(ActionQueueEventType.IssueStarted, nextRequest.IssueId);
            await StartRequestAsync(nextRequest);
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _pendingRequests.Clear();
            TransitionState(ActionQueueState.Completed);
            _logger.LogInformation("ActionQueue {QueueId} cancelled", Id);
        }
    }

    public void NotifyCompleted(string issueId, bool success, string? error = null)
    {
        AgentStartRequest? nextRequest = null;

        lock (_lock)
        {
            if (CurrentRequest?.IssueId != issueId)
                return;

            var completedRequest = CurrentRequest;
            var startedAt = _currentStartedAt ?? DateTimeOffset.UtcNow;

            _history.Add(new ActionQueueEntry
            {
                IssueId = issueId,
                Request = completedRequest,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Success = success,
                Error = error
            });

            CurrentRequest = null;
            _currentStartedAt = null;

            if (success)
            {
                EmitEvent(ActionQueueEventType.IssueCompleted, issueId);
            }
            else
            {
                EmitEvent(ActionQueueEventType.IssueFailed, issueId, error: error);
            }

            if (_paused || _pendingRequests.Count == 0)
            {
                TransitionState(ActionQueueState.Idle);
                return;
            }

            nextRequest = _pendingRequests[0];
            _pendingRequests.RemoveAt(0);
            CurrentRequest = nextRequest;
            _currentStartedAt = DateTimeOffset.UtcNow;
        }

        if (nextRequest != null)
        {
            EmitEvent(ActionQueueEventType.IssueStarted, nextRequest.IssueId);
            _ = StartRequestAsync(nextRequest);
        }
    }

    public void NotifyBlocked(string issueId, string reason)
    {
        lock (_lock)
        {
            if (CurrentRequest?.IssueId != issueId)
                return;

            if (State != ActionQueueState.Running)
                return;

            TransitionState(ActionQueueState.Blocked);
            _logger.LogInformation(
                "ActionQueue {QueueId} blocked on issue {IssueId}: {Reason}",
                Id, issueId, reason);
        }
    }

    public async Task UnblockAsync(CancellationToken cancellationToken = default)
    {
        AgentStartRequest? currentRequest;

        lock (_lock)
        {
            if (State != ActionQueueState.Blocked || CurrentRequest == null)
                return;

            currentRequest = CurrentRequest;
            TransitionState(ActionQueueState.Running);
        }

        EmitEvent(ActionQueueEventType.IssueStarted, currentRequest.IssueId);
        await StartRequestAsync(currentRequest);
    }

    private Task StartRequestAsync(AgentStartRequest request)
    {
        return _agentStartService.QueueAgentStartAsync(request);
    }

    private void TransitionState(ActionQueueState newState)
    {
        var previousState = State;
        State = newState;

        EmitEvent(ActionQueueEventType.StateChanged,
            previousState: previousState, newState: newState);
    }

    private void EmitEvent(
        ActionQueueEventType eventType,
        string? issueId = null,
        string? error = null,
        ActionQueueState? previousState = null,
        ActionQueueState? newState = null)
    {
        OnEvent?.Invoke(new ActionQueueEvent
        {
            QueueId = Id,
            EventType = eventType,
            IssueId = issueId,
            Error = error,
            PreviousState = previousState,
            NewState = newState
        });
    }
}
