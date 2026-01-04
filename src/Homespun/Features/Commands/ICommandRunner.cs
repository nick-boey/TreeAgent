namespace Homespun.Features.Commands;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string command, string arguments, string workingDirectory);
}