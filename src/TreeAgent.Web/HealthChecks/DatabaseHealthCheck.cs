using Microsoft.Extensions.Diagnostics.HealthChecks;
using TreeAgent.Web.Data;
using TreeAgent.Web.Services;

namespace TreeAgent.Web.HealthChecks;

/// <summary>
/// Health check for database connectivity
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly TreeAgentDbContext _db;

    public DatabaseHealthCheck(TreeAgentDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to connect to the database
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection is healthy");
            }

            return HealthCheckResult.Unhealthy("Cannot connect to database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}

/// <summary>
/// Health check for Claude Code process manager
/// </summary>
public class ProcessManagerHealthCheck : IHealthCheck
{
    private readonly ClaudeCodeProcessManager _processManager;

    public ProcessManagerHealthCheck(ClaudeCodeProcessManager processManager)
    {
        _processManager = processManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check that the process manager is accessible
            var runningCount = _processManager.GetRunningAgentCount();
            return Task.FromResult(HealthCheckResult.Healthy($"Process manager healthy, {runningCount} agents running"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Process manager check failed", ex));
        }
    }
}
