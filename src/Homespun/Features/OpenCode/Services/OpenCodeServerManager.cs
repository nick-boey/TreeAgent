using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Homespun.Features.OpenCode.Models;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Manages OpenCode server instances - spawning, tracking, and stopping servers.
/// </summary>
public class OpenCodeServerManager : IOpenCodeServerManager, IDisposable
{
    private readonly OpenCodeOptions _options;
    private readonly IOpenCodeClient _client;
    private readonly ILogger<OpenCodeServerManager> _logger;
    private readonly ConcurrentDictionary<string, OpenCodeServer> _servers = new();
    private readonly ConcurrentBag<int> _releasedPorts = [];
    private readonly string _resolvedExecutablePath;
    private int _nextPort;
    private int _allocatedCount;
    private bool _disposed;

    public OpenCodeServerManager(
        IOptions<OpenCodeOptions> options,
        IOpenCodeClient client,
        ILogger<OpenCodeServerManager> logger)
    {
        _options = options.Value;
        _client = client;
        _logger = logger;
        _nextPort = _options.BasePort;
        _resolvedExecutablePath = ResolveExecutablePath(_options.ExecutablePath);
        _logger.LogDebug("Resolved OpenCode executable path: {Path}", _resolvedExecutablePath);
    }

    /// <summary>
    /// Resolves the full path to an executable by searching PATH.
    /// </summary>
    private static string ResolveExecutablePath(string executableName)
    {
        // If it's already an absolute path, use it directly
        if (Path.IsPathRooted(executableName) && File.Exists(executableName))
        {
            return executableName;
        }

        // Get the PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathSeparator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries);

        // Extensions to try on Windows
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".cmd", ".exe", ".bat", "" }
            : new[] { "" };

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, executableName + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        // If not found, return the original name and let the process fail with a clear error
        return executableName;
    }

    public int AllocatePort()
    {
        if (_allocatedCount >= _options.MaxConcurrentServers)
        {
            throw new InvalidOperationException(
                $"Maximum concurrent servers ({_options.MaxConcurrentServers}) reached. Stop a server before starting a new one.");
        }

        // Try to reuse a released port first
        if (_releasedPorts.TryTake(out var releasedPort))
        {
            _allocatedCount++;
            return releasedPort;
        }

        var port = _nextPort;
        _nextPort++;
        _allocatedCount++;
        return port;
    }

    public void ReleasePort(int port)
    {
        _releasedPorts.Add(port);
        _allocatedCount--;
    }

    public async Task<OpenCodeServer> StartServerAsync(string pullRequestId, string worktreePath, CancellationToken ct = default)
    {
        // Check if already running
        if (_servers.TryGetValue(pullRequestId, out var existing))
        {
            if (existing.Status == OpenCodeServerStatus.Running)
            {
                _logger.LogWarning("Server already running for PR {PullRequestId}", pullRequestId);
                return existing;
            }
            // Remove stale entry
            _servers.TryRemove(pullRequestId, out _);
        }

        var port = AllocatePort();
        var server = new OpenCodeServer
        {
            PullRequestId = pullRequestId,
            WorktreePath = worktreePath,
            Port = port,
            Status = OpenCodeServerStatus.Starting
        };

        try
        {
            var process = StartServerProcess(port, worktreePath);
            server.Process = process;

            if (!_servers.TryAdd(pullRequestId, server))
            {
                throw new InvalidOperationException($"Failed to register server for PR {pullRequestId}");
            }

            // Wait for health check to pass
            await WaitForHealthyAsync(server, ct);
            server.Status = OpenCodeServerStatus.Running;
            
            _logger.LogInformation("OpenCode server started on port {Port} for PR {PullRequestId}", port, pullRequestId);
            return server;
        }
        catch (Exception ex)
        {
            server.Status = OpenCodeServerStatus.Failed;
            _servers.TryRemove(pullRequestId, out _);
            ReleasePort(port);
            
            if (server.Process is { HasExited: false })
            {
                try { server.Process.Kill(); } catch { /* ignore */ }
            }
            
            _logger.LogError(ex, "Failed to start OpenCode server for PR {PullRequestId}", pullRequestId);
            throw;
        }
    }

    public async Task StopServerAsync(string pullRequestId, CancellationToken ct = default)
    {
        if (!_servers.TryRemove(pullRequestId, out var server))
        {
            _logger.LogWarning("No server found for PR {PullRequestId}", pullRequestId);
            return;
        }

        if (server.Process is { HasExited: false })
        {
            try
            {
                server.Process.Kill();
                await server.Process.WaitForExitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error killing server process for PR {PullRequestId}", pullRequestId);
            }
        }

        ReleasePort(server.Port);
        server.Status = OpenCodeServerStatus.Stopped;
        _logger.LogInformation("OpenCode server stopped for PR {PullRequestId}", pullRequestId);
    }

    public OpenCodeServer? GetServerForPullRequest(string pullRequestId)
    {
        return _servers.TryGetValue(pullRequestId, out var server) ? server : null;
    }

    public IReadOnlyList<OpenCodeServer> GetRunningServers()
    {
        return _servers.Values
            .Where(s => s.Status == OpenCodeServerStatus.Running)
            .ToList();
    }

    public async Task<bool> IsHealthyAsync(OpenCodeServer server, CancellationToken ct = default)
    {
        try
        {
            var health = await _client.GetHealthAsync(server.BaseUrl, ct);
            return health.Healthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for server on port {Port}", server.Port);
            return false;
        }
    }

    private Process StartServerProcess(int port, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _resolvedExecutablePath,
            Arguments = $"serve --port {port} --hostname 127.0.0.1",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _logger.LogDebug("Starting OpenCode: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);
        
        var process = new Process { StartInfo = startInfo };
        process.Start();
        
        _logger.LogDebug("Started OpenCode process with PID {ProcessId} on port {Port}", process.Id, port);
        return process;
    }

    private async Task WaitForHealthyAsync(OpenCodeServer server, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.ServerStartTimeoutMs);
        var stopwatch = Stopwatch.StartNew();
        var delay = 100; // Start with 100ms delay

        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (await IsHealthyAsync(server, ct))
            {
                return;
            }

            // Check if process has exited
            if (server.Process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"OpenCode process exited with code {server.Process.ExitCode} before becoming healthy");
            }

            await Task.Delay(delay, ct);
            delay = Math.Min(delay * 2, 1000); // Exponential backoff, max 1 second
        }

        throw new TimeoutException(
            $"OpenCode server on port {server.Port} did not become healthy within {timeout.TotalSeconds} seconds");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var server in _servers.Values)
        {
            if (server.Process is { HasExited: false })
            {
                try
                {
                    server.Process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error killing server process during dispose");
                }
            }
        }

        _servers.Clear();
    }
}
