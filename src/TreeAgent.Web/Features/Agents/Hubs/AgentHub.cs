using Microsoft.AspNetCore.SignalR;

namespace TreeAgent.Web.Features.Agents.Hubs;

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