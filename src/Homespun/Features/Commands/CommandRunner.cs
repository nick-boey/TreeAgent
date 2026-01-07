using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Commands;

public class CommandRunner : ICommandRunner
{
    private readonly ILogger<CommandRunner> _logger;

    public CommandRunner(ILogger<CommandRunner> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> RunAsync(string command, string arguments, string workingDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "Executing command: {Command} {Arguments} in {WorkingDirectory}",
            command, arguments, workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            stopwatch.Stop();

            var result = new CommandResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = error,
                ExitCode = process.ExitCode
            };

            if (result.Success)
            {
                _logger.LogInformation(
                    "Command completed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds);
                
                // Log output at debug level for successful commands
                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogDebug("Command output: {Output}", TruncateOutput(output));
                }
            }
            else
            {
                _logger.LogWarning(
                    "Command failed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms | Error={Error}",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds, 
                    TruncateOutput(error));
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(
                ex,
                "Command exception: {Command} {Arguments} in {WorkingDirectory} | Duration={Duration}ms",
                command, arguments, workingDirectory, stopwatch.ElapsedMilliseconds);

            return new CommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// Truncates output to a reasonable length for logging.
    /// Full output is available in the CommandResult.
    /// </summary>
    private static string TruncateOutput(string output, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(output))
            return string.Empty;
        
        var trimmed = output.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;
        
        return trimmed[..maxLength] + "... [truncated]";
    }
}