using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Homespun.Features.ClaudeCodeUI.Models;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCodeUI.Services;

/// <summary>
/// Manages Claude Code UI server instances.
/// </summary>
public class ClaudeCodeUIServerManager : IDisposable
{
    private readonly ClaudeCodeUIOptions _options;
    private readonly IClaudeCodeUIClient _client;
    private readonly IPortAllocationService _portService;
    private readonly IGitHubEnvironmentService _githubEnvService;
    private readonly ILogger<ClaudeCodeUIServerManager> _logger;
    private readonly ConcurrentDictionary<string, ClaudeCodeUIServer> _servers = new();
    private bool _disposed;

    public ClaudeCodeUIServerManager(
        IOptions<ClaudeCodeUIOptions> options,
        IClaudeCodeUIClient client,
        IPortAllocationService portService,
        IGitHubEnvironmentService githubEnvService,
        ILogger<ClaudeCodeUIServerManager> logger)
    {
        _options = options.Value;
        _client = client;
        _portService = portService;
        _githubEnvService = githubEnvService;
        _logger = logger;
    }

    /// <summary>
    /// Starts a new Claude Code UI server for an entity.
    /// </summary>
    public async Task<ClaudeCodeUIServer> StartServerAsync(
        string entityId,
        string workingDirectory,
        CancellationToken ct = default)
    {
        // Check if already running
        if (_servers.TryGetValue(entityId, out var existing) &&
            existing.Status == ClaudeCodeUIServerStatus.Running)
        {
            return existing;
        }

        var port = _portService.AllocatePort();
        var server = new ClaudeCodeUIServer
        {
            EntityId = entityId,
            WorkingDirectory = workingDirectory,
            Port = port
        };

        _servers[entityId] = server;

        try
        {
            // Find executable
            var executablePath = ResolveExecutablePath(_options.ExecutablePath);

            // Start process
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--port {port}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Add environment variables
            startInfo.Environment["ANTHROPIC_API_KEY"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

            // Enable platform mode to disable authentication (Tailscale provides network security)
            startInfo.Environment["VITE_IS_PLATFORM"] = "true";

            // Set database path to persist auth data in the working directory
            // This ensures the registered user survives server restarts
            var dbPath = Path.Combine(workingDirectory, ".cloudcli-auth.db");
            startInfo.Environment["DATABASE_PATH"] = dbPath;

            // Add GitHub environment (token, GIT_ASKPASS, and Git identity)
            // This provides GITHUB_TOKEN, GH_TOKEN, GIT_AUTHOR_NAME, GIT_AUTHOR_EMAIL, etc.
            var githubEnv = _githubEnvService.GetGitHubEnvironment();
            foreach (var kvp in githubEnv)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }

            _logger.LogInformation(
                "Starting Claude Code UI server for {EntityId} on port {Port}, cwd: {WorkingDirectory}",
                entityId, port, workingDirectory);

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Claude Code UI process");
            }

            server.Process = process;

            // Wait for server to be healthy
            var healthy = await WaitForHealthyAsync(server, ct);
            if (!healthy)
            {
                await StopServerAsync(entityId, ct);
                throw new InvalidOperationException(
                    $"Claude Code UI server failed to become healthy within {_options.ServerStartTimeoutMs}ms");
            }

            // Ensure a default user exists for platform mode authentication
            // Platform mode bypasses token validation but requires at least one user in the database
            var userCreated = await _client.EnsureDefaultUserAsync(server.BaseUrl, ct);
            if (!userCreated)
            {
                _logger.LogWarning(
                    "Failed to ensure default user for Claude Code UI server on port {Port}. " +
                    "Authentication may not work correctly.", port);
            }

            server.Status = ClaudeCodeUIServerStatus.Running;
            _logger.LogInformation(
                "Claude Code UI server started for {EntityId} on port {Port}",
                entityId, port);

            return server;
        }
        catch (Exception ex)
        {
            server.Status = ClaudeCodeUIServerStatus.Failed;
            _portService.ReleasePort(port);
            _servers.TryRemove(entityId, out _);
            _logger.LogError(ex, "Failed to start Claude Code UI server for {EntityId}", entityId);
            throw;
        }
    }

    /// <summary>
    /// Stops a running server.
    /// </summary>
    public async Task StopServerAsync(string entityId, CancellationToken ct = default)
    {
        if (!_servers.TryRemove(entityId, out var server))
        {
            return;
        }

        try
        {
            if (server.Process != null && !server.Process.HasExited)
            {
                server.Process.Kill(entireProcessTree: true);
                await server.Process.WaitForExitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping Claude Code UI server for {EntityId}", entityId);
        }
        finally
        {
            _portService.ReleasePort(server.Port);
            server.Status = ClaudeCodeUIServerStatus.Stopped;
            _logger.LogInformation("Claude Code UI server stopped for {EntityId}", entityId);
        }
    }

    /// <summary>
    /// Gets a server for an entity.
    /// </summary>
    public ClaudeCodeUIServer? GetServerForEntity(string entityId)
    {
        _servers.TryGetValue(entityId, out var server);
        return server;
    }

    /// <summary>
    /// Gets all running servers.
    /// </summary>
    public IReadOnlyList<ClaudeCodeUIServer> GetRunningServers()
    {
        return _servers.Values
            .Where(s => s.Status == ClaudeCodeUIServerStatus.Running)
            .ToList();
    }

    /// <summary>
    /// Checks if a server is healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(ClaudeCodeUIServer server, CancellationToken ct = default)
    {
        return await _client.IsHealthyAsync(server.BaseUrl, ct);
    }

    private async Task<bool> WaitForHealthyAsync(ClaudeCodeUIServer server, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.ServerStartTimeoutMs);
        var interval = TimeSpan.FromMilliseconds(_options.HealthCheckIntervalMs);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout && !ct.IsCancellationRequested)
        {
            if (server.Process?.HasExited == true)
            {
                _logger.LogWarning("Claude Code UI process exited unexpectedly");
                return false;
            }

            if (await _client.IsHealthyAsync(server.BaseUrl, ct))
            {
                return true;
            }

            await Task.Delay(interval, ct);
        }

        return false;
    }

    /// <summary>
    /// Resolves the full path to an executable by searching PATH and common installation locations.
    /// </summary>
    private string ResolveExecutablePath(string executableName)
    {
        // If it's already an absolute path, use it directly
        if (Path.IsPathRooted(executableName) && File.Exists(executableName))
        {
            _logger.LogDebug("Using absolute path for cloudcli: {Path}", executableName);
            return executableName;
        }

        // Extensions to try on Windows
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".cmd", ".exe", ".bat", "" }
            : new[] { "" };

        // Build list of directories to search
        var searchPaths = new List<string>();

        // 1. Add PATH directories
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathSeparator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        searchPaths.AddRange(pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries));

        // 2. Add common installation locations
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // npm global installation paths on Windows
            searchPaths.Add(Path.Combine(appData, "npm"));
            searchPaths.Add(Path.Combine(localAppData, "npm"));
            searchPaths.Add(Path.Combine(userProfile, "AppData", "Roaming", "npm"));

            // pnpm global installation path
            searchPaths.Add(Path.Combine(localAppData, "pnpm"));

            // yarn global bin
            searchPaths.Add(Path.Combine(localAppData, "Yarn", "bin"));

            // Scoop
            searchPaths.Add(Path.Combine(userProfile, "scoop", "shims"));

            // Chocolatey
            var chocoPath = Environment.GetEnvironmentVariable("ChocolateyInstall");
            if (!string.IsNullOrEmpty(chocoPath))
            {
                searchPaths.Add(Path.Combine(chocoPath, "bin"));
            }
            else
            {
                searchPaths.Add(@"C:\ProgramData\chocolatey\bin");
            }

            // Volta
            searchPaths.Add(Path.Combine(localAppData, "Volta", "bin"));
        }
        else
        {
            // Unix-like systems (macOS, Linux)

            // npm global paths
            searchPaths.Add(Path.Combine(userProfile, ".npm-global", "bin"));
            searchPaths.Add("/usr/local/bin");
            searchPaths.Add("/usr/bin");
            searchPaths.Add(Path.Combine(userProfile, ".local", "bin"));

            // pnpm
            searchPaths.Add(Path.Combine(userProfile, ".local", "share", "pnpm"));

            // yarn
            searchPaths.Add(Path.Combine(userProfile, ".yarn", "bin"));

            // Homebrew (macOS)
            searchPaths.Add("/opt/homebrew/bin");

            // Volta
            searchPaths.Add(Path.Combine(userProfile, ".volta", "bin"));

            // asdf
            searchPaths.Add(Path.Combine(userProfile, ".asdf", "shims"));
        }

        // Search all paths
        foreach (var searchPath in searchPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(searchPath, executableName + ext);
                if (File.Exists(fullPath))
                {
                    _logger.LogDebug("Found cloudcli at: {Path}", fullPath);
                    return fullPath;
                }
            }
        }

        // If not found, return the original name and let the process fail with a clear error
        _logger.LogWarning(
            "Could not find '{ExecutableName}' in PATH or common installation locations. " +
            "Searched {PathCount} directories. The process may fail to start.",
            executableName, searchPaths.Count);

        return executableName;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var server in _servers.Values)
        {
            try
            {
                if (server.Process != null && !server.Process.HasExited)
                {
                    server.Process.Kill(entireProcessTree: true);
                }
                _portService.ReleasePort(server.Port);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing server for {EntityId}", server.EntityId);
            }
        }

        _servers.Clear();
    }
}
