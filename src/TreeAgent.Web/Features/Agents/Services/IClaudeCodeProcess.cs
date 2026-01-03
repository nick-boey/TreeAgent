using TreeAgent.Web.Features.Agents.Data;

namespace TreeAgent.Web.Features.Agents.Services;

public interface IClaudeCodeProcess : IDisposable
{
    bool IsRunning { get; }
    AgentStatus Status { get; }
    event Action<string>? OnMessageReceived;
    event Action<AgentStatus>? OnStatusChanged;
    Task StartAsync();
    Task StopAsync();
    Task SendMessageAsync(string message);
}