namespace Homespun.Features.AgentOrchestration.Services;

public enum ActionQueueState
{
    Idle,
    Running,
    Blocked,
    Completed
}

public record ActionQueueEvent
{
    public required string QueueId { get; init; }
    public required ActionQueueEventType EventType { get; init; }
    public string? IssueId { get; init; }
    public string? Error { get; init; }
    public ActionQueueState? PreviousState { get; init; }
    public ActionQueueState? NewState { get; init; }
}

public enum ActionQueueEventType
{
    IssueStarted,
    IssueCompleted,
    IssueFailed,
    StateChanged
}

public record ActionQueueEntry
{
    public required string IssueId { get; init; }
    public required AgentStartRequest Request { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Represents a single sequential execution pipeline for processing actions.
/// </summary>
public interface IActionQueue
{
    string Id { get; }
    ActionQueueState State { get; }
    AgentStartRequest? CurrentRequest { get; }
    IReadOnlyList<AgentStartRequest> PendingRequests { get; }
    IReadOnlyList<ActionQueueEntry> History { get; }

    Task EnqueueAsync(AgentStartRequest request, CancellationToken cancellationToken = default);
    bool Dequeue(string issueId);
    void Pause();
    Task ResumeAsync(CancellationToken cancellationToken = default);
    void Cancel();
    Task UnblockAsync(CancellationToken cancellationToken = default);

    event Action<ActionQueueEvent>? OnEvent;
}
