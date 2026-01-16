using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Agents.Services;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Agents.Hubs;

/// <summary>
/// SignalR hub for real-time agent communication.
/// Supports multiple harness types.
/// </summary>
public class AgentHub : Hub
{
    private readonly IAgentWorkflowService _workflowService;
    private readonly IAgentHarnessFactory _harnessFactory;

    /// <summary>
    /// The name of the global group for server list updates.
    /// </summary>
    public const string GlobalGroupName = "global-servers";

    public AgentHub(
        IAgentWorkflowService workflowService,
        IAgentHarnessFactory harnessFactory)
    {
        _workflowService = workflowService;
        _harnessFactory = harnessFactory;
    }

    /// <summary>
    /// Join a group to receive updates for a specific entity's agent.
    /// </summary>
    public async Task JoinAgentGroup(string entityId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, entityId);
    }

    /// <summary>
    /// Leave an entity's agent group.
    /// </summary>
    public async Task LeaveAgentGroup(string entityId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, entityId);
    }

    /// <summary>
    /// Start an agent for a pull request.
    /// </summary>
    /// <param name="pullRequestId">The pull request ID</param>
    /// <param name="model">Optional model override</param>
    /// <param name="harnessType">Optional harness type (defaults to configured default)</param>
    public async Task<WorkflowAgentStatus> StartAgent(string pullRequestId, string? model = null, string? harnessType = null)
    {
        var status = await _workflowService.StartAgentForPullRequestAsync(pullRequestId, model, harnessType);
        await Clients.Group(pullRequestId).SendAsync("AgentStarted", pullRequestId, status);
        return status;
    }

    /// <summary>
    /// Stop an agent for an entity.
    /// </summary>
    public async Task StopAgent(string entityId)
    {
        await _workflowService.StopAgentAsync(entityId);
        await Clients.Group(entityId).SendAsync("AgentStopped", entityId);
    }

    /// <summary>
    /// Get the current status of an agent.
    /// </summary>
    public async Task<WorkflowAgentStatus?> GetAgentStatus(string entityId)
    {
        return await _workflowService.GetAgentStatusAsync(entityId);
    }

    /// <summary>
    /// Send a prompt to the agent.
    /// </summary>
    public async Task<AgentMessage> SendPrompt(string entityId, string prompt)
    {
        var response = await _workflowService.SendPromptAsync(entityId, prompt);
        await Clients.Group(entityId).SendAsync("MessageReceived", entityId, response);
        return response;
    }

    /// <summary>
    /// Join the global group to receive updates about all running agents.
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
    /// Gets all currently running agents with their information.
    /// </summary>
    public IReadOnlyList<RunningAgentInfo> GetAllRunningAgents()
    {
        return _workflowService.GetAllRunningAgents()
            .Select(RunningAgentInfo.FromAgentInstance)
            .ToList();
    }

    /// <summary>
    /// Gets available harness types.
    /// </summary>
    public IReadOnlyList<string> GetAvailableHarnessTypes()
    {
        return _harnessFactory.AvailableHarnessTypes;
    }

    /// <summary>
    /// Gets the default harness type.
    /// </summary>
    public string GetDefaultHarnessType()
    {
        return _harnessFactory.DefaultHarnessType;
    }
}

/// <summary>
/// Extension methods for broadcasting agent events to SignalR clients.
/// </summary>
public static class AgentHubExtensions
{
    public static async Task BroadcastAgentStarted(
        this IHubContext<AgentHub> hubContext,
        string entityId,
        WorkflowAgentStatus status)
    {
        await hubContext.Clients.Group(entityId).SendAsync("AgentStarted", entityId, status);
    }

    public static async Task BroadcastAgentStopped(
        this IHubContext<AgentHub> hubContext,
        string entityId)
    {
        await hubContext.Clients.Group(entityId).SendAsync("AgentStopped", entityId);
    }

    public static async Task BroadcastMessageReceived(
        this IHubContext<AgentHub> hubContext,
        string entityId,
        AgentMessage message)
    {
        await hubContext.Clients.Group(entityId).SendAsync("MessageReceived", entityId, message);
    }

    public static async Task BroadcastAgentEvent(
        this IHubContext<AgentHub> hubContext,
        string entityId,
        AgentEvent evt)
    {
        await hubContext.Clients.Group(entityId).SendAsync("AgentEvent", entityId, evt);
    }

    /// <summary>
    /// Broadcasts the updated list of running agents to all clients in the global group.
    /// </summary>
    public static async Task BroadcastAgentListChanged(
        this IHubContext<AgentHub> hubContext,
        IReadOnlyList<RunningAgentInfo> agents)
    {
        await hubContext.Clients.Group(AgentHub.GlobalGroupName).SendAsync("AgentListChanged", agents);
    }

    /// <summary>
    /// Broadcasts agent startup state changes to all clients in the global group.
    /// </summary>
    public static async Task BroadcastAgentStartupStateChanged(
        this IHubContext<AgentHub> hubContext,
        string entityId,
        AgentInstanceStatus status,
        string? error = null)
    {
        await hubContext.Clients.Group(AgentHub.GlobalGroupName).SendAsync("AgentStartupStateChanged", new
        {
            EntityId = entityId,
            Status = status,
            Error = error
        });
    }
}
