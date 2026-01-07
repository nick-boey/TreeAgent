using Homespun.Features.OpenCode.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Background service that broadcasts agent startup state changes via SignalR.
/// </summary>
public class AgentStartupBroadcaster : IHostedService
{
    private readonly IAgentStartupTracker _tracker;
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ILogger<AgentStartupBroadcaster> _logger;

    public AgentStartupBroadcaster(
        IAgentStartupTracker tracker,
        IHubContext<AgentHub> hubContext,
        ILogger<AgentStartupBroadcaster> logger)
    {
        _tracker = tracker;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tracker.StateChanged += OnStateChanged;
        _logger.LogInformation("AgentStartupBroadcaster started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tracker.StateChanged -= OnStateChanged;
        _logger.LogInformation("AgentStartupBroadcaster stopped");
        return Task.CompletedTask;
    }

    private async void OnStateChanged(AgentStartupInfo info)
    {
        try
        {
            _logger.LogDebug(
                "Broadcasting agent startup state change: {EntityId} -> {State}", 
                info.EntityId, 
                info.State);
            
            await _hubContext.BroadcastAgentStartupStateChanged(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast agent startup state change for {EntityId}", info.EntityId);
        }
    }
}
