namespace Homespun.Features.AgentOrchestration.Services;

public enum ActionQueueCoordinatorStatus
{
    Idle,
    Running,
    Completed,
    Cancelled
}

public record ActionQueueCoordinatorState
{
    public required string ProjectId { get; init; }
    public required ActionQueueCoordinatorStatus Status { get; init; }
    public required IReadOnlyList<IActionQueue> ActiveQueues { get; init; }
    public required int MaxConcurrency { get; init; }
    public required int RunningQueueCount { get; init; }
    public string? RootIssueId { get; init; }
}

/// <summary>
/// Coordinates multiple ActionQueues for a project, spawning queues based on
/// the issue hierarchy's execution modes (Series vs Parallel).
/// </summary>
public interface IActionQueueCoordinator
{
    Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, CancellationToken ct = default);
    IReadOnlyList<IActionQueue> GetActiveQueues(string projectId);
    void CancelAll(string projectId);
    ActionQueueCoordinatorState? GetStatus(string projectId);

    event Action<ActionQueueCoordinatorEvent>? OnEvent;
}

public record ActionQueueCoordinatorEvent
{
    public required string ProjectId { get; init; }
    public required ActionQueueCoordinatorEventType EventType { get; init; }
    public string? QueueId { get; init; }
    public string? IssueId { get; init; }
    public string? Error { get; init; }
}

public enum ActionQueueCoordinatorEventType
{
    QueueCreated,
    QueueCompleted,
    AllQueuesCompleted,
    ExecutionStarted,
    ExecutionCancelled,
    ExecutionFailed
}
