using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.OpenCode.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Manages OpenCode server instances - spawning, tracking, and stopping servers.
/// </summary>
public class OpenCodeServerManager : IOpenCodeServerManager, IDisposable
{
    private readonly OpenCodeOptions _options;
    private readonly IOpenCodeClient _client;
    private readonly IPortAllocationService _portAllocationService;
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ILogger<OpenCodeServerManager> _logger;
    private readonly ConcurrentDictionary<string, OpenCodeServer> _servers = new();
    private readonly string _resolvedExecutablePath;
    private bool _disposed;

    public OpenCodeServerManager(
        IOptions<OpenCodeOptions> options,
        IOpenCodeClient client,
        IPortAllocationService portAllocationService,
        IHubContext<AgentHub> hubContext,
        ILogger<OpenCodeServerManager> logger)
    {
        _options = options.Value;
        _client = client;
        _portAllocationService = portAllocationService;
        _hubContext = hubContext;
        _logger = logger;
        _resolvedExecutablePath = ResolveExecutablePath(_options.ExecutablePath);
        _logger.LogDebug("Resolved OpenCode executable path: {Path}", _resolvedExecutablePath);
    }

    /// <summary>
    /// Resolves the full path to an executable by searching PATH and common installation locations.
    /// </summary>
    private string ResolveExecutablePath(string executableName)
    {
        // If it's already an absolute path, use it directly
        if (Path.IsPathRooted(executableName) && File.Exists(executableName))
        {
            _logger.LogDebug("Using absolute path for OpenCode: {Path}", executableName);
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
            
            // nvm for Windows
            var nvmHome = Environment.GetEnvironmentVariable("NVM_HOME");
            if (!string.IsNullOrEmpty(nvmHome))
            {
                searchPaths.Add(nvmHome);
            }
            
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
            
            // fnm (Fast Node Manager)
            searchPaths.Add(Path.Combine(localAppData, "fnm_multishells"));
            var fnmDir = Path.Combine(localAppData, "fnm_multishells");
            if (Directory.Exists(fnmDir))
            {
                try
                {
                    // fnm creates subdirectories for each shell session
                    foreach (var dir in Directory.GetDirectories(fnmDir))
                    {
                        searchPaths.Add(dir);
                    }
                }
                catch { /* ignore directory access errors */ }
            }
        }
        else
        {
            // Unix-like systems (macOS, Linux)
            
            // npm global paths
            searchPaths.Add(Path.Combine(userProfile, ".npm-global", "bin"));
            searchPaths.Add("/usr/local/bin");
            searchPaths.Add("/usr/bin");
            searchPaths.Add(Path.Combine(userProfile, ".local", "bin"));
            
            // nvm
            var nvmDir = Environment.GetEnvironmentVariable("NVM_DIR") 
                         ?? Path.Combine(userProfile, ".nvm");
            if (Directory.Exists(nvmDir))
            {
                // Try to find the current node version's bin directory
                var versionsDir = Path.Combine(nvmDir, "versions", "node");
                if (Directory.Exists(versionsDir))
                {
                    try
                    {
                        foreach (var versionDir in Directory.GetDirectories(versionsDir))
                        {
                            searchPaths.Add(Path.Combine(versionDir, "bin"));
                        }
                    }
                    catch { /* ignore */ }
                }
            }
            
            // pnpm
            searchPaths.Add(Path.Combine(userProfile, ".local", "share", "pnpm"));
            
            // yarn
            searchPaths.Add(Path.Combine(userProfile, ".yarn", "bin"));
            
            // Homebrew (macOS)
            searchPaths.Add("/opt/homebrew/bin");
            searchPaths.Add("/usr/local/Cellar");
            
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
                    _logger.LogDebug("Found OpenCode at: {Path}", fullPath);
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

    public async Task<OpenCodeServer> StartServerAsync(string entityId, string worktreePath, bool continueSession = false, CancellationToken ct = default)
    {
        // Check if already running
        if (_servers.TryGetValue(entityId, out var existing))
        {
            if (existing.Status == OpenCodeServerStatus.Running)
            {
                _logger.LogWarning("Server already running for entity {EntityId}", entityId);
                return existing;
            }
            // Remove stale entry
            _servers.TryRemove(entityId, out _);
        }

        var port = _portAllocationService.AllocatePort();
        var server = new OpenCodeServer
        {
            EntityId = entityId,
            WorktreePath = worktreePath,
            Port = port,
            Status = OpenCodeServerStatus.Starting,
            ContinueSession = continueSession
        };

        // Log URL configuration for debugging
        _logger.LogInformation(
            "Creating server for entity {EntityId}: Port={Port}, ExternalHostname={ExternalHostname}, BaseUrl={BaseUrl}, ExternalBaseUrl={ExternalBaseUrl}",
            entityId,
            port,
            OpenCodeServer.ExternalHostname ?? "(null)",
            server.BaseUrl,
            server.ExternalBaseUrl);

        try
        {
            var process = StartServerProcess(port, worktreePath, continueSession);
            server.Process = process;

            if (!_servers.TryAdd(entityId, server))
            {
                throw new InvalidOperationException($"Failed to register server for entity {entityId}");
            }

            // Wait for health check to pass
            await WaitForHealthyAsync(server, ct);
            
            // Diagnostic: Verify OpenCode is running in the expected directory
            try
            {
                var reportedPath = await _client.GetCurrentPathAsync(server.BaseUrl, ct);
                _logger.LogInformation(
                    "OpenCode server path verification: ReportedPath={ReportedPath}, ExpectedPath={ExpectedPath}",
                    reportedPath ?? "(null)",
                    worktreePath);
                
                if (reportedPath != null)
                {
                    var normalizedReported = Path.GetFullPath(reportedPath);
                    var normalizedExpected = Path.GetFullPath(worktreePath);
                    
                    if (!string.Equals(normalizedReported, normalizedExpected, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "OpenCode working directory MISMATCH! Expected={Expected}, Actual={Actual}. " +
                            "The agent may be working in the wrong directory.",
                            normalizedExpected,
                            normalizedReported);
                    }
                    else
                    {
                        _logger.LogInformation("OpenCode working directory verified successfully: {Path}", normalizedReported);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Unable to verify OpenCode working directory - /path endpoint returned null. " +
                        "Expected: {ExpectedPath}",
                        worktreePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify OpenCode working directory for entity {EntityId}", entityId);
            }
            
            server.Status = OpenCodeServerStatus.Running;
            
            _logger.LogInformation("OpenCode server started on port {Port} for entity {EntityId}", port, entityId);
            
            // Broadcast server list change to all connected clients
            await BroadcastServerListAsync();
            
            return server;
        }
        catch (Exception ex)
        {
            server.Status = OpenCodeServerStatus.Failed;
            _servers.TryRemove(entityId, out _);
            _portAllocationService.ReleasePort(port);
            
            if (server.Process is { HasExited: false })
            {
                try { server.Process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
            
            _logger.LogError(ex, "Failed to start OpenCode server for entity {EntityId}", entityId);
            throw;
        }
    }

    public async Task StopServerAsync(string entityId, CancellationToken ct = default)
    {
        if (!_servers.TryRemove(entityId, out var server))
        {
            _logger.LogWarning("No server found for entity {EntityId}", entityId);
            return;
        }

        _logger.LogInformation(
            "Stopping OpenCode server for entity {EntityId}, port {Port}, PID {ProcessId}",
            entityId, server.Port, server.Process?.Id);

        if (server.Process is { HasExited: false })
        {
            try
            {
                // On Windows, .cmd scripts spawn child processes that need to be killed separately
                // Use Kill(entireProcessTree: true) to kill the process and all its children
                _logger.LogDebug("Killing process tree for PID {ProcessId}", server.Process.Id);
                server.Process.Kill(entireProcessTree: true);
                await server.Process.WaitForExitAsync(ct);
                _logger.LogDebug("Process {ProcessId} exited with code {ExitCode}", 
                    server.Process.Id, server.Process.ExitCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error killing server process for entity {EntityId}", entityId);
            }
        }
        else
        {
            _logger.LogDebug("Process already exited for entity {EntityId}", entityId);
        }

        _portAllocationService.ReleasePort(server.Port);
        server.Status = OpenCodeServerStatus.Stopped;
        _logger.LogInformation("OpenCode server stopped for entity {EntityId}", entityId);
        
        // Broadcast server list change to all connected clients
        await BroadcastServerListAsync();
    }

    public OpenCodeServer? GetServerForEntity(string entityId)
    {
        return _servers.TryGetValue(entityId, out var server) ? server : null;
    }

    public IReadOnlyList<OpenCodeServer> GetRunningServers()
    {
        return _servers.Values
            .Where(s => s.Status == OpenCodeServerStatus.Running)
            .ToList();
    }

    /// <summary>
    /// Broadcasts the current list of running servers to all connected SignalR clients.
    /// Uses external URLs for UI display when configured.
    /// </summary>
    private async Task BroadcastServerListAsync()
    {
        var servers = GetRunningServers()
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

        await _hubContext.BroadcastServerListChanged(servers);
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

    private Process StartServerProcess(int port, string workingDirectory, bool continueSession = false)
    {
        var arguments = $"serve --port {port} --hostname 127.0.0.1";
        if (continueSession)
        {
            arguments += " --continue";
        }
        
        // Normalize the working directory path to use platform-native separators
        // This fixes issues on Windows where mixed forward/back slashes cause problems
        var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
        
        // Pre-flight check: Verify working directory exists
        var directoryExists = Directory.Exists(normalizedWorkingDirectory);
        if (!directoryExists)
        {
            _logger.LogError(
                "OpenCode working directory does not exist: {WorkingDirectory}",
                normalizedWorkingDirectory);
            throw new DirectoryNotFoundException(
                $"OpenCode working directory does not exist: {normalizedWorkingDirectory}");
        }
        
        // Check if executable is a .cmd file (Windows npm package wrapper)
        var isCmdFile = _resolvedExecutablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        
        // Log all process start parameters at Info level for debugging
        _logger.LogInformation(
            "Starting OpenCode server: Executable={Executable}, Arguments={Arguments}, " +
            "WorkingDirectory={WorkingDirectory}, DirectoryExists={DirectoryExists}, IsCmdFile={IsCmdFile}",
            _resolvedExecutablePath,
            arguments,
            normalizedWorkingDirectory,
            directoryExists,
            isCmdFile);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _resolvedExecutablePath,
            Arguments = arguments,
            WorkingDirectory = normalizedWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _logger.LogInformation(
            "ProcessStartInfo details: FileName={FileName}, Arguments={Arguments}, WorkingDirectory={WorkingDirectory}, " +
            "UseShellExecute={UseShellExecute}, CreateNoWindow={CreateNoWindow}",
            startInfo.FileName,
            startInfo.Arguments,
            startInfo.WorkingDirectory,
            startInfo.UseShellExecute,
            startInfo.CreateNoWindow);
        
        var process = new Process { StartInfo = startInfo };
        process.Start();
        
        _logger.LogInformation(
            "Started OpenCode process: PID={ProcessId}, Port={Port}, WorkingDirectory={WorkingDirectory}",
            process.Id, port, normalizedWorkingDirectory);
        
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
                    _logger.LogDebug("Disposing: killing process tree for PID {ProcessId}", server.Process.Id);
                    server.Process.Kill(entireProcessTree: true);
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
