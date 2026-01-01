using Microsoft.AspNetCore.SignalR;

namespace TreeAgent.Web.Hubs;

/// <summary>
/// SignalR hub for real-time agent updates
/// </summary>
public class AgentHub : Hub
{
    /// <summary>
    /// Join a group to receive updates for a specific agent
    /// </summary>
    public async Task JoinAgent(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent-{agentId}");
    }

    /// <summary>
    /// Leave an agent's update group
    /// </summary>
    public async Task LeaveAgent(string agentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent-{agentId}");
    }

    /// <summary>
    /// Join a group to receive updates for all agents
    /// </summary>
    public async Task JoinAllAgents()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-agents");
    }

    /// <summary>
    /// Leave the all-agents group
    /// </summary>
    public async Task LeaveAllAgents()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all-agents");
    }
}

/// <summary>
/// Interface for sending agent-related notifications
/// </summary>
public interface IAgentHubNotifier
{
    Task SendMessageAsync(string agentId, string role, string content, string? metadata = null);
    Task SendStatusChangeAsync(string agentId, string status);
}

/// <summary>
/// Implementation that sends notifications via SignalR
/// </summary>
public class AgentHubNotifier : IAgentHubNotifier
{
    private readonly IHubContext<AgentHub> _hubContext;

    public AgentHubNotifier(IHubContext<AgentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendMessageAsync(string agentId, string role, string content, string? metadata = null)
    {
        var message = new
        {
            AgentId = agentId,
            Role = role,
            Content = content,
            Metadata = metadata,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients.Group($"agent-{agentId}").SendAsync("ReceiveMessage", message);
        await _hubContext.Clients.Group("all-agents").SendAsync("ReceiveMessage", message);
    }

    public async Task SendStatusChangeAsync(string agentId, string status)
    {
        var update = new
        {
            AgentId = agentId,
            Status = status,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients.Group($"agent-{agentId}").SendAsync("StatusChanged", update);
        await _hubContext.Clients.Group("all-agents").SendAsync("StatusChanged", update);
    }
}
