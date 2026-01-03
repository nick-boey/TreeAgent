namespace TreeAgent.Web.Features.Agents.Hubs;

/// <summary>
/// Interface for sending agent-related notifications
/// </summary>
public interface IAgentHubNotifier
{
    Task SendMessageAsync(string agentId, string role, string content, string? metadata = null);
    Task SendStatusChangeAsync(string agentId, string status);
}