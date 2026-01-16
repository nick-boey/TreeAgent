using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Agents.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Background service that broadcasts agent startup state changes via SignalR.
/// </summary>
public class AgentStartupBroadcaster(
    IAgentStartupTracker tracker,
    IHubContext<AgentHub> hubContext,
    ILogger<AgentStartupBroadcaster> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        tracker.StateChanged += OnStateChanged;
        logger.LogInformation("AgentStartupBroadcaster started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        tracker.StateChanged -= OnStateChanged;
        logger.LogInformation("AgentStartupBroadcaster stopped");
        return Task.CompletedTask;
    }

    private async void OnStateChanged(AgentStartupInfo info)
    {
        try
        {
            logger.LogDebug(
                "Broadcasting agent startup state change: {EntityId} -> {State}",
                info.EntityId,
                info.State);

            var status = info.State switch
            {
                AgentStartupState.Starting => AgentInstanceStatus.Starting,
                AgentStartupState.Started => AgentInstanceStatus.Running,
                AgentStartupState.Failed => AgentInstanceStatus.Failed,
                _ => AgentInstanceStatus.Stopped
            };

            await hubContext.BroadcastAgentStartupStateChanged(info.EntityId, status, info.ErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast agent startup state change for {EntityId}", info.EntityId);
        }
    }
}
