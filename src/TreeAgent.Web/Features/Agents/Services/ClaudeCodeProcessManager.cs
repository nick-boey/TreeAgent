using System.Collections.Concurrent;
using TreeAgent.Web.Features.Agents.Data;

namespace TreeAgent.Web.Features.Agents.Services;

public class ClaudeCodeProcessManager(IClaudeCodeProcessFactory processFactory) : IDisposable
{
    private readonly ConcurrentDictionary<string, IClaudeCodeProcess> _processes = new();
    private bool _disposed;

    public event Action<string, string>? OnMessageReceived;
    public event Action<string, AgentStatus>? OnStatusChanged;

    public ClaudeCodeProcessManager() : this(new ClaudeCodeProcessFactory())
    {
    }

    public async Task<bool> StartAgentAsync(string agentId, string workingDirectory, string? systemPrompt = null)
    {
        if (_processes.ContainsKey(agentId))
            return false;

        var process = processFactory.Create(agentId, workingDirectory, systemPrompt);
        process.OnMessageReceived += (message) => OnMessageReceived?.Invoke(agentId, message);
        process.OnStatusChanged += (status) => OnStatusChanged?.Invoke(agentId, status);

        if (!_processes.TryAdd(agentId, process))
            return false;

        await process.StartAsync();
        return true;
    }

    public async Task<bool> StopAgentAsync(string agentId)
    {
        if (!_processes.TryRemove(agentId, out var process))
            return false;

        await process.StopAsync();
        process.Dispose();
        return true;
    }

    public bool IsAgentRunning(string agentId)
    {
        return _processes.TryGetValue(agentId, out var process) && process.IsRunning;
    }

    public AgentStatus GetAgentStatus(string agentId)
    {
        if (!_processes.TryGetValue(agentId, out var process))
            return AgentStatus.Stopped;

        return process.Status;
    }

    public async Task<bool> SendMessageAsync(string agentId, string message)
    {
        if (!_processes.TryGetValue(agentId, out var process))
            return false;

        await process.SendMessageAsync(message);
        return true;
    }

    public IEnumerable<string> GetAllAgentIds()
    {
        return _processes.Keys.ToList();
    }

    public int GetRunningAgentCount()
    {
        return _processes.Values.Count(p => p.IsRunning);
    }

    // For testing purposes
    public void SimulateMessageReceived(string agentId, string message)
    {
        OnMessageReceived?.Invoke(agentId, message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var process in _processes.Values)
        {
            process.Dispose();
        }
        _processes.Clear();
    }
}