using Microsoft.AspNetCore.SignalR;

namespace TreeAgent.Web.Features.Agents.Hubs;

/// <summary>
/// Implementation that sends notifications via SignalR
/// </summary>
public class AgentHubNotifier(IHubContext<AgentHub> hubContext) : IAgentHubNotifier
{
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

        await hubContext.Clients.Group($"agent-{agentId}").SendAsync("ReceiveMessage", message);
        await hubContext.Clients.Group("all-agents").SendAsync("ReceiveMessage", message);
    }

    public async Task SendStatusChangeAsync(string agentId, string status)
    {
        var update = new
        {
            AgentId = agentId,
            Status = status,
            Timestamp = DateTime.UtcNow
        };

        await hubContext.Clients.Group($"agent-{agentId}").SendAsync("StatusChanged", update);
        await hubContext.Clients.Group("all-agents").SendAsync("StatusChanged", update);
    }
}