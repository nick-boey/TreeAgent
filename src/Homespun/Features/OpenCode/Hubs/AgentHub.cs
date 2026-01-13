using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.OpenCode.Hubs;

/// <summary>
/// SignalR hub for real-time agent communication.
/// </summary>
public class AgentHub(
    IAgentWorkflowService workflowService,
    IOpenCodeServerManager serverManager,
    IOpenCodeClient openCodeClient)
    : Hub
{
    /// <summary>
    /// The name of the global group for server list updates.
    /// </summary>
    public const string GlobalGroupName = "global-servers";

    /// <summary>
    /// Join a group to receive updates for a specific pull request's agent.
    /// </summary>
    public async Task JoinAgentGroup(string pullRequestId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, pullRequestId);
    }

    /// <summary>
    /// Leave a pull request's agent group.
    /// </summary>
    public async Task LeaveAgentGroup(string pullRequestId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, pullRequestId);
    }

    /// <summary>
    /// Start an agent for a pull request.
    /// </summary>
    public async Task<AgentStatus> StartAgent(string pullRequestId, string? model = null)
    {
        var status = await workflowService.StartAgentForPullRequestAsync(pullRequestId, model);
        await Clients.Group(pullRequestId).SendAsync("AgentStarted", pullRequestId, status);
        return status;
    }

    /// <summary>
    /// Stop an agent for a pull request.
    /// </summary>
    public async Task StopAgent(string pullRequestId)
    {
        await workflowService.StopAgentAsync(pullRequestId);
        await Clients.Group(pullRequestId).SendAsync("AgentStopped", pullRequestId);
    }

    /// <summary>
    /// Get the current status of an agent.
    /// </summary>
    public async Task<AgentStatus?> GetAgentStatus(string pullRequestId)
    {
        return await workflowService.GetAgentStatusAsync(pullRequestId);
    }

    /// <summary>
    /// Send a prompt to the agent.
    /// </summary>
    public async Task<OpenCodeMessage> SendPrompt(string pullRequestId, string prompt)
    {
        var response = await workflowService.SendPromptAsync(pullRequestId, prompt);
        await Clients.Group(pullRequestId).SendAsync("MessageReceived", pullRequestId, response);
        return response;
    }

    /// <summary>
    /// Join the global group to receive updates about all running servers.
    /// </summary>
    public async Task JoinGlobalGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GlobalGroupName);
    }

    /// <summary>
    /// Leave the global group.
    /// </summary>
    public async Task LeaveGlobalGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GlobalGroupName);
    }

    /// <summary>
    /// Gets all currently running servers with their session information.
    /// Uses external URLs for UI display when configured.
    /// </summary>
    public IReadOnlyList<RunningServerInfo> GetAllRunningServers()
    {
        return serverManager.GetRunningServers()
            .Select(s => new RunningServerInfo
            {
                EntityId = s.EntityId,
                Port = s.Port,
                BaseUrl = s.ExternalBaseUrl, // Use external URL for UI display
                WorktreePath = s.WorktreePath,
                StartedAt = s.StartedAt,
                ActiveSessionId = s.ActiveSessionId,
                WebViewUrl = s.WebViewUrl
            }).ToList();
    }

    /// <summary>
    /// Gets all sessions for a specific running server.
    /// </summary>
    /// <param name="entityId">The entity ID of the server</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of sessions, or empty list if server not found</returns>
    public async Task<List<OpenCodeSession>> GetSessionsForServer(string entityId, CancellationToken ct = default)
    {
        var server = serverManager.GetServerForEntity(entityId);
        if (server == null)
        {
            return [];
        }
        
        return await openCodeClient.ListSessionsAsync(server.BaseUrl, ct);
    }
}

/// <summary>
/// Extension methods for broadcasting agent events to SignalR clients.
/// </summary>
public static class AgentHubExtensions
{
    public static async Task BroadcastAgentStarted(
        this IHubContext<AgentHub> hubContext,
        string pullRequestId,
        AgentStatus status)
    {
        await hubContext.Clients.Group(pullRequestId).SendAsync("AgentStarted", pullRequestId, status);
    }

    public static async Task BroadcastAgentStopped(
        this IHubContext<AgentHub> hubContext,
        string pullRequestId)
    {
        await hubContext.Clients.Group(pullRequestId).SendAsync("AgentStopped", pullRequestId);
    }

    public static async Task BroadcastMessageReceived(
        this IHubContext<AgentHub> hubContext,
        string pullRequestId,
        OpenCodeMessage message)
    {
        await hubContext.Clients.Group(pullRequestId).SendAsync("MessageReceived", pullRequestId, message);
    }

    public static async Task BroadcastAgentEvent(
        this IHubContext<AgentHub> hubContext,
        string pullRequestId,
        OpenCodeEvent evt)
    {
        await hubContext.Clients.Group(pullRequestId).SendAsync("AgentEvent", pullRequestId, evt);
    }

    /// <summary>
    /// Broadcasts the updated list of running servers to all clients in the global group.
    /// </summary>
    /// <param name="hubContext">The hub context</param>
    /// <param name="servers">The list of running servers</param>
    public static async Task BroadcastServerListChanged(
        this IHubContext<AgentHub> hubContext,
        IReadOnlyList<RunningServerInfo> servers)
    {
        await hubContext.Clients.Group(AgentHub.GlobalGroupName).SendAsync("ServerListChanged", servers);
    }

    /// <summary>
    /// Broadcasts agent startup state changes to all clients in the global group.
    /// </summary>
    /// <param name="hubContext">The hub context</param>
    /// <param name="info">The startup state info</param>
    public static async Task BroadcastAgentStartupStateChanged(
        this IHubContext<AgentHub> hubContext,
        AgentStartupInfo info)
    {
        await hubContext.Clients.Group(AgentHub.GlobalGroupName).SendAsync("AgentStartupStateChanged", info);
    }
}
