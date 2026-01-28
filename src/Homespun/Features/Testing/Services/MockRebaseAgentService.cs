using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IRebaseAgentService.
/// </summary>
public class MockRebaseAgentService : IRebaseAgentService
{
    private readonly IClaudeSessionService _sessionService;
    private readonly ILogger<MockRebaseAgentService> _logger;

    public MockRebaseAgentService(
        IClaudeSessionService sessionService,
        ILogger<MockRebaseAgentService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<ClaudeSession> StartRebaseAgentAsync(
        string projectId,
        string worktreePath,
        string branchName,
        string defaultBranch,
        string model,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StartRebaseAgent for branch {BranchName} in {WorktreePath}",
            branchName, worktreePath);

        var systemPrompt = GenerateRebaseSystemPrompt(branchName, defaultBranch);
        var initialMessage = GenerateRebaseInitialMessage(branchName, defaultBranch, recentMergedPRs);

        // Use a unique entity ID for the rebase session
        var entityId = $"rebase-{branchName}-{Guid.NewGuid():N}"[..20];

        var session = await _sessionService.StartSessionAsync(
            entityId,
            projectId,
            worktreePath,
            SessionMode.Build,
            model,
            systemPrompt,
            cancellationToken);

        // Send the initial rebase message
        await _sessionService.SendMessageAsync(session.Id, initialMessage, cancellationToken);

        return session;
    }

    public string GenerateRebaseSystemPrompt(string branchName, string defaultBranch)
    {
        return $"""
            You are a rebase agent responsible for rebasing the branch '{branchName}' onto '{defaultBranch}'.

            Your task is to:
            1. Fetch the latest changes from the remote
            2. Rebase the current branch onto {defaultBranch}
            3. Resolve any merge conflicts that arise
            4. Run the test suite to ensure everything works
            5. Push the rebased branch to the remote

            If you encounter any issues, report them clearly and stop the process.

            [MOCK MODE] This is a simulated session - no actual git operations will be performed.
            """;
    }

    public string GenerateRebaseInitialMessage(
        string branchName,
        string defaultBranch,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null)
    {
        var message = $"""
            Please rebase the branch '{branchName}' onto '{defaultBranch}'.

            Follow these steps:
            1. Run `git fetch origin`
            2. Run `git rebase origin/{defaultBranch}`
            3. If there are conflicts, resolve them
            4. Run the test suite
            5. Push the changes with `git push --force-with-lease`
            """;

        if (recentMergedPRs?.Any() == true)
        {
            message += "\n\nRecent merged PRs that may have caused conflicts:\n";
            foreach (var pr in recentMergedPRs.Take(5))
            {
                message += $"- #{pr.Number}: {pr.Title}\n";
            }
        }

        return message;
    }
}
