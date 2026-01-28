using Homespun.Features.Commands;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of ICommandRunner that returns simulated results for common commands.
/// </summary>
public class MockCommandRunner : ICommandRunner
{
    private readonly ILogger<MockCommandRunner> _logger;

    public MockCommandRunner(ILogger<MockCommandRunner> logger)
    {
        _logger = logger;
    }

    public Task<CommandResult> RunAsync(string command, string arguments, string workingDirectory)
    {
        _logger.LogDebug("[Mock] {Command} {Arguments} in {WorkingDirectory}", command, arguments, workingDirectory);

        var result = command.ToLowerInvariant() switch
        {
            "git" => GetMockGitResult(arguments),
            "gh" => GetMockGhResult(arguments),
            "claude" => GetMockClaudeResult(arguments),
            _ => new CommandResult { Success = true, Output = "[Mock command output]", ExitCode = 0 }
        };

        return Task.FromResult(result);
    }

    private static CommandResult GetMockGitResult(string arguments)
    {
        var args = arguments.ToLowerInvariant();

        if (args.StartsWith("status"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "On branch main\nnothing to commit, working tree clean",
                ExitCode = 0
            };
        }

        if (args.StartsWith("branch"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "* main\n  feature/demo-feature\n  feature/another-feature",
                ExitCode = 0
            };
        }

        if (args.StartsWith("push"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "Everything up-to-date",
                ExitCode = 0
            };
        }

        if (args.StartsWith("pull"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "Already up to date.",
                ExitCode = 0
            };
        }

        if (args.StartsWith("fetch"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "",
                ExitCode = 0
            };
        }

        if (args.StartsWith("checkout"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "Switched to branch 'main'",
                ExitCode = 0
            };
        }

        if (args.StartsWith("worktree"))
        {
            if (args.Contains("list"))
            {
                return new CommandResult
                {
                    Success = true,
                    Output = "/mock/repo  abc1234 [main]\n/mock/repo-feature  def5678 [feature/demo]",
                    ExitCode = 0
                };
            }

            return new CommandResult
            {
                Success = true,
                Output = "",
                ExitCode = 0
            };
        }

        if (args.StartsWith("rev-parse"))
        {
            if (args.Contains("--show-toplevel"))
            {
                return new CommandResult
                {
                    Success = true,
                    Output = "/mock/repo",
                    ExitCode = 0
                };
            }

            return new CommandResult
            {
                Success = true,
                Output = "abc1234567890",
                ExitCode = 0
            };
        }

        if (args.StartsWith("log"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "abc1234 Initial commit",
                ExitCode = 0
            };
        }

        if (args.StartsWith("remote"))
        {
            if (args.Contains("get-url"))
            {
                return new CommandResult
                {
                    Success = true,
                    Output = "https://github.com/demo-org/demo-repo.git",
                    ExitCode = 0
                };
            }

            return new CommandResult
            {
                Success = true,
                Output = "origin",
                ExitCode = 0
            };
        }

        if (args.StartsWith("config"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "main",
                ExitCode = 0
            };
        }

        return new CommandResult
        {
            Success = true,
            Output = "",
            ExitCode = 0
        };
    }

    private static CommandResult GetMockGhResult(string arguments)
    {
        var args = arguments.ToLowerInvariant();

        if (args.Contains("auth status"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "github.com\n  Logged in to github.com as demo-user",
                ExitCode = 0
            };
        }

        if (args.Contains("pr create"))
        {
            return new CommandResult
            {
                Success = true,
                Output = "https://github.com/demo-org/demo-repo/pull/42",
                ExitCode = 0
            };
        }

        return new CommandResult
        {
            Success = true,
            Output = "",
            ExitCode = 0
        };
    }

    private static CommandResult GetMockClaudeResult(string arguments)
    {
        return new CommandResult
        {
            Success = true,
            Output = "[Mock Claude output]",
            ExitCode = 0
        };
    }
}
