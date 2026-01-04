using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.OpenCode.Hubs;

/// <summary>
/// SignalR hub for real-time agent communication.
/// </summary>
public class AgentHub : Hub
{
    private readonly IAgentWorkflowService _workflowService;

    public AgentHub(IAgentWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

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
        var status = await _workflowService.StartAgentForPullRequestAsync(pullRequestId, model);
        await Clients.Group(pullRequestId).SendAsync("AgentStarted", pullRequestId, status);
        return status;
    }

    /// <summary>
    /// Stop an agent for a pull request.
    /// </summary>
    public async Task StopAgent(string pullRequestId)
    {
        await _workflowService.StopAgentAsync(pullRequestId);
        await Clients.Group(pullRequestId).SendAsync("AgentStopped", pullRequestId);
    }

    /// <summary>
    /// Get the current status of an agent.
    /// </summary>
    public async Task<AgentStatus?> GetAgentStatus(string pullRequestId)
    {
        return await _workflowService.GetAgentStatusAsync(pullRequestId);
    }

    /// <summary>
    /// Send a prompt to the agent.
    /// </summary>
    public async Task<OpenCodeMessage> SendPrompt(string pullRequestId, string prompt)
    {
        var response = await _workflowService.SendPromptAsync(pullRequestId, prompt);
        await Clients.Group(pullRequestId).SendAsync("MessageReceived", pullRequestId, response);
        return response;
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
}
