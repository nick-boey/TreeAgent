using System.Collections.Concurrent;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Thread-safe tracker for agent startup state across UI components.
/// Enables non-blocking agent creation by tracking startup progress independently.
/// </summary>
public class AgentStartupTracker : IAgentStartupTracker
{
    private readonly ConcurrentDictionary<string, AgentStartupInfo> _states = new();
    
    /// <inheritdoc />
    public event Action<AgentStartupInfo>? StateChanged;

    /// <inheritdoc />
    public AgentStartupInfo GetState(string entityId)
    {
        return _states.TryGetValue(entityId, out var info) 
            ? info 
            : new AgentStartupInfo(entityId, AgentStartupState.NotStarted);
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentStartupInfo> GetAllStates()
    {
        return _states.Values.ToList();
    }

    /// <inheritdoc />
    public void MarkAsStarting(string entityId)
    {
        var info = new AgentStartupInfo(entityId, AgentStartupState.Starting);
        _states[entityId] = info;
        StateChanged?.Invoke(info);
    }

    /// <inheritdoc />
    public void MarkAsStarted(string entityId)
    {
        var info = new AgentStartupInfo(entityId, AgentStartupState.Started);
        _states[entityId] = info;
        StateChanged?.Invoke(info);
    }

    /// <inheritdoc />
    public void MarkAsFailed(string entityId, string errorMessage)
    {
        var info = new AgentStartupInfo(entityId, AgentStartupState.Failed, errorMessage);
        _states[entityId] = info;
        StateChanged?.Invoke(info);
    }

    /// <inheritdoc />
    public void ClearState(string entityId)
    {
        _states.TryRemove(entityId, out _);
        StateChanged?.Invoke(new AgentStartupInfo(entityId, AgentStartupState.NotStarted));
    }

    /// <inheritdoc />
    public bool IsStarting(string entityId)
    {
        return _states.TryGetValue(entityId, out var info) && info.State == AgentStartupState.Starting;
    }

    /// <inheritdoc />
    public bool HasFailed(string entityId)
    {
        return _states.TryGetValue(entityId, out var info) && info.State == AgentStartupState.Failed;
    }
}
