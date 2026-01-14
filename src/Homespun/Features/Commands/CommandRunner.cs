using System.Diagnostics;
using Homespun.Features.GitHub;

namespace Homespun.Features.Commands;

public class CommandRunner(
    IGitHubEnvironmentService gitHubEnvironmentService,
    ILogger<CommandRunner> logger) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string command, string arguments, string workingDirectory)
    {
        var stopwatch = Stopwatch.StartNew();

        // Add --no-daemon flag for beads commands to bypass daemon socket communication.
        // TODO: Make --no-daemon configurable via BeadsService options
        var effectiveArguments = AddBeadsFlags(command, arguments);

        logger.LogInformation(
            "Executing command: {Command} {Arguments} in {WorkingDirectory}",
            command, effectiveArguments, workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = effectiveArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Inject GitHub environment variables for git/gh commands
        foreach (var (key, value) in gitHubEnvironmentService.GetGitHubEnvironment())
        {
            startInfo.Environment[key] = value;
        }

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
                logger.LogInformation(
                    "Command completed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds);
                
                // Log output at debug level for successful commands
                if (!string.IsNullOrWhiteSpace(output))
                {
                    logger.LogDebug("Command output: {Output}", TruncateOutput(output));
                }
            }
            else
            {
                logger.LogWarning(
                    "Command failed: {Command} {Arguments} | ExitCode={ExitCode} | Duration={Duration}ms | Error={Error}",
                    command, arguments, result.ExitCode, stopwatch.ElapsedMilliseconds, 
                    TruncateOutput(error));
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            logger.LogError(
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

    /// <summary>
    /// Adds flags to beads (bd) commands.
    /// Currently adds --no-daemon to bypass socket communication with the host daemon.
    /// </summary>
    private static string AddBeadsFlags(string command, string arguments)
    {
        if (!command.Equals("bd", StringComparison.OrdinalIgnoreCase))
            return arguments;

        // Prepend --no-daemon to bypass daemon socket communication
        return string.IsNullOrEmpty(arguments)
            ? "--no-daemon"
            : $"--no-daemon {arguments}";
    }
}