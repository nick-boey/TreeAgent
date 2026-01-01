using System.Collections.Concurrent;
using System.Diagnostics;
using TreeAgent.Web.Data.Entities;

namespace TreeAgent.Web.Services;

public class ClaudeCodeProcessManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IClaudeCodeProcess> _processes = new();
    private readonly IClaudeCodeProcessFactory _processFactory;
    private bool _disposed;

    public event Action<string, string>? OnMessageReceived;
    public event Action<string, AgentStatus>? OnStatusChanged;

    public ClaudeCodeProcessManager() : this(new ClaudeCodeProcessFactory())
    {
    }

    public ClaudeCodeProcessManager(IClaudeCodeProcessFactory processFactory)
    {
        _processFactory = processFactory;
    }

    public async Task<bool> StartAgentAsync(string agentId, string workingDirectory, string? systemPrompt = null)
    {
        if (_processes.ContainsKey(agentId))
            return false;

        var process = _processFactory.Create(agentId, workingDirectory, systemPrompt);
        process.OnMessageReceived += (message) => OnMessageReceived?.Invoke(agentId, message);
        process.OnStatusChanged += (status) => OnStatusChanged?.Invoke(agentId, status);

        if (!_processes.TryAdd(agentId, process))
            return false;

        await process.StartAsync();
        return true;
    }

    public async Task<bool> StopAgentAsync(string agentId)
    {
        if (!_processes.TryRemove(agentId, out var process))
            return false;

        await process.StopAsync();
        process.Dispose();
        return true;
    }

    public bool IsAgentRunning(string agentId)
    {
        return _processes.TryGetValue(agentId, out var process) && process.IsRunning;
    }

    public AgentStatus GetAgentStatus(string agentId)
    {
        if (!_processes.TryGetValue(agentId, out var process))
            return AgentStatus.Stopped;

        return process.Status;
    }

    public async Task<bool> SendMessageAsync(string agentId, string message)
    {
        if (!_processes.TryGetValue(agentId, out var process))
            return false;

        await process.SendMessageAsync(message);
        return true;
    }

    public IEnumerable<string> GetAllAgentIds()
    {
        return _processes.Keys.ToList();
    }

    public int GetRunningAgentCount()
    {
        return _processes.Values.Count(p => p.IsRunning);
    }

    // For testing purposes
    public void SimulateMessageReceived(string agentId, string message)
    {
        OnMessageReceived?.Invoke(agentId, message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var process in _processes.Values)
        {
            process.Dispose();
        }
        _processes.Clear();
    }
}

public class ClaudeCodeProcess : IClaudeCodeProcess
{
    private readonly string _agentId;
    private readonly string _claudeCodePath;
    private readonly string _workingDirectory;
    private readonly string? _systemPrompt;
    private Process? _process;
    private bool _disposed;

    public event Action<string>? OnMessageReceived;
    public event Action<AgentStatus>? OnStatusChanged;

    public bool IsRunning => _process != null && !_process.HasExited;
    public AgentStatus Status { get; private set; } = AgentStatus.Idle;

    public ClaudeCodeProcess(string agentId, string claudeCodePath, string workingDirectory, string? systemPrompt = null)
    {
        _agentId = agentId;
        _claudeCodePath = claudeCodePath;
        _workingDirectory = workingDirectory;
        _systemPrompt = systemPrompt;
    }

    public Task StartAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _claudeCodePath,
            Arguments = BuildArguments(),
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;
        _process.EnableRaisingEvents = true;

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Status = AgentStatus.Running;
            OnStatusChanged?.Invoke(Status);
        }
        catch (Exception)
        {
            Status = AgentStatus.Error;
            OnStatusChanged?.Invoke(Status);
        }

        return Task.CompletedTask;
    }

    private string BuildArguments()
    {
        var args = "--output-format json";

        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            var escapedPrompt = _systemPrompt.Replace("\"", "\\\"");
            args += $" --system-prompt \"{escapedPrompt}\"";
        }

        return args;
    }

    public async Task SendMessageAsync(string message)
    {
        if (_process?.StandardInput == null || !IsRunning)
            return;

        await _process.StandardInput.WriteLineAsync(message);
        await _process.StandardInput.FlushAsync();
    }

    public async Task StopAsync()
    {
        if (_process == null || _process.HasExited)
            return;

        try
        {
            await _process.StandardInput.WriteLineAsync("/exit");
            await _process.StandardInput.FlushAsync();

            if (!_process.WaitForExit(5000))
            {
                _process.Kill();
            }
        }
        catch
        {
            try { _process.Kill(); } catch { }
        }

        Status = AgentStatus.Stopped;
        OnStatusChanged?.Invoke(Status);
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        OnMessageReceived?.Invoke(e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        // Log errors but don't treat all stderr as fatal
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Status = _process?.ExitCode == 0 ? AgentStatus.Stopped : AgentStatus.Error;
        OnStatusChanged?.Invoke(Status);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }
            _process?.Dispose();
        }
        catch { }
    }
}
