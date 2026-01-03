using Microsoft.Extensions.Diagnostics.HealthChecks;
using TreeAgent.Web.Features.Agents.Services;

namespace TreeAgent.Web.HealthChecks;

/// <summary>
/// Health check for Claude Code process manager
/// </summary>
public class ProcessManagerHealthCheck(ClaudeCodeProcessManager processManager) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check that the process manager is accessible
            var runningCount = processManager.GetRunningAgentCount();
            return Task.FromResult(HealthCheckResult.Healthy($"Process manager healthy, {runningCount} agents running"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Process manager check failed", ex));
        }
    }
}