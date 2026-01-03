using System.Diagnostics;
using TreeAgent.Web.Features.Agents.Data;

namespace TreeAgent.Web.Features.Agents.Services;

public class ClaudeCodeProcess(
    string agentId,
    string claudeCodePath,
    string workingDirectory,
    string? systemPrompt = null)
    : IClaudeCodeProcess
{
    private readonly string _agentId = agentId;
    private Process? _process;
    private bool _disposed;

    public event Action<string>? OnMessageReceived;
    public event Action<AgentStatus>? OnStatusChanged;

    public bool IsRunning => _process != null && !_process.HasExited;
    public AgentStatus Status { get; private set; } = AgentStatus.Idle;

    public Task StartAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = claudeCodePath,
            Arguments = BuildArguments(),
            WorkingDirectory = workingDirectory,
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

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var escapedPrompt = systemPrompt.Replace("\"", "\\\"");
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