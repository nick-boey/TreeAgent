using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.PullRequests;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for starting and managing rebase agents.
/// Rebase agents are specialized Claude agents that handle git rebasing operations,
/// including conflict resolution, running tests, and pushing changes.
/// </summary>
public interface IRebaseAgentService
{
    /// <summary>
    /// Start a rebase agent session for the specified worktree.
    /// The agent will fetch the latest changes, rebase onto the target branch,
    /// resolve any conflicts, run tests, and push the changes.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="worktreePath">Path to the git worktree</param>
    /// <param name="branchName">Name of the branch being rebased</param>
    /// <param name="defaultBranch">The default branch to rebase onto (e.g., "main")</param>
    /// <param name="model">The Claude model to use (e.g., "sonnet")</param>
    /// <param name="recentMergedPRs">Optional list of recently merged PRs for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created Claude session</returns>
    Task<ClaudeSession> StartRebaseAgentAsync(
        string projectId,
        string worktreePath,
        string branchName,
        string defaultBranch,
        string model,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate the system prompt for a rebase agent.
    /// </summary>
    /// <param name="branchName">Name of the branch being rebased</param>
    /// <param name="defaultBranch">The default branch to rebase onto</param>
    /// <returns>The system prompt string</returns>
    string GenerateRebaseSystemPrompt(string branchName, string defaultBranch);

    /// <summary>
    /// Generate the initial message to send to the rebase agent.
    /// </summary>
    /// <param name="branchName">Name of the branch being rebased</param>
    /// <param name="defaultBranch">The default branch to rebase onto</param>
    /// <param name="recentMergedPRs">Optional list of recently merged PRs for context</param>
    /// <returns>The initial message string</returns>
    string GenerateRebaseInitialMessage(
        string branchName,
        string defaultBranch,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null);
}
