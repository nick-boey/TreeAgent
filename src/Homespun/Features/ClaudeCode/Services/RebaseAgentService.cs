using System.Text;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.PullRequests;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for starting and managing rebase agents.
/// </summary>
public class RebaseAgentService : IRebaseAgentService
{
    private readonly IClaudeSessionService _sessionService;
    private readonly ILogger<RebaseAgentService> _logger;
    private const int MaxPRBodyLength = 200;

    public RebaseAgentService(
        IClaudeSessionService sessionService,
        ILogger<RebaseAgentService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClaudeSession> StartRebaseAgentAsync(
        string projectId,
        string worktreePath,
        string branchName,
        string defaultBranch,
        string model,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting rebase agent for branch {BranchName} onto {DefaultBranch} in {WorktreePath}",
            branchName, defaultBranch, worktreePath);

        var entityId = $"rebase-{branchName}";
        var systemPrompt = GenerateRebaseSystemPrompt(branchName, defaultBranch);

        // Start the session with Build mode (needs full Bash access for git operations)
        var session = await _sessionService.StartSessionAsync(
            entityId,
            projectId,
            worktreePath,
            SessionMode.Build,
            model,
            systemPrompt,
            cancellationToken);

        _logger.LogInformation("Created rebase session {SessionId} for branch {BranchName}",
            session.Id, branchName);

        // Send the initial message to kick off the rebase process
        var initialMessage = GenerateRebaseInitialMessage(branchName, defaultBranch, recentMergedPRs);
        await _sessionService.SendMessageAsync(session.Id, initialMessage, cancellationToken);

        return session;
    }

    /// <inheritdoc />
    public string GenerateRebaseSystemPrompt(string branchName, string defaultBranch)
    {
        return $"""
            You are a specialized Git Rebase Agent. Your task is to safely rebase the current branch onto the latest {defaultBranch} branch.

            ## Current Context
            - Branch: {branchName}
            - Target: {defaultBranch}

            ## Your Workflow

            ### Step 1: Fetch and Update
            1. Run `git fetch origin` to get the latest changes
            2. Check the current branch status with `git status`
            3. Verify there are no uncommitted changes (if there are, stash them first with `git stash`)

            ### Step 2: Analyze the Rebase
            1. Run `git log --oneline {defaultBranch}..HEAD` to see commits to be rebased
            2. Run `git log --oneline HEAD..origin/{defaultBranch}` to see new commits on {defaultBranch}
            3. Identify potential conflict areas by comparing changed files

            ### Step 3: Perform the Rebase
            1. Run `git rebase origin/{defaultBranch}`
            2. If conflicts occur, DO NOT ABORT - proceed to resolve them

            ### Step 4: Resolve Conflicts (if any)
            When conflicts occur:
            1. Run `git status` to see which files have conflicts
            2. Read the conflicting files using the Read tool
            3. Understand the changes from both sides:
               - The incoming changes from {defaultBranch} (marked with <<<<<<< HEAD)
               - Your branch's changes (marked with >>>>>>> {branchName})
            4. Review the recently merged PRs provided in the initial message for context on what changed
            5. Make intelligent conflict resolution decisions - keep functionality from both sides when possible
            6. Edit files to resolve conflicts using the Edit tool
            7. Run `git add <resolved-file>` for each resolved file
            8. Run `git rebase --continue`
            9. Repeat until rebase completes

            ### Step 5: Verify and Test
            1. Run the project's test suite to ensure no regressions:
               - For .NET projects: `dotnet test`
               - For Node.js projects: `npm test` or `yarn test`
               - Check for a Makefile with test targets
            2. If tests fail, investigate and fix the issues
            3. Run a build to ensure compilation succeeds:
               - For .NET projects: `dotnet build`
               - For Node.js projects: `npm run build` or `yarn build`

            ### Step 6: Push Changes
            1. Once tests pass, push with `git push --force-with-lease origin {branchName}`
            2. Report the final status including:
               - Number of commits rebased
               - Any conflicts that were resolved
               - Test results
               - Whether the push was successful

            ## Important Guidelines
            - NEVER use `git rebase --abort` unless explicitly instructed by the user
            - Use `--force-with-lease` (not `--force`) for safe force pushing - this protects against overwriting others' work
            - If you encounter complex conflicts you cannot resolve, explain the situation clearly and ask for guidance
            - Always verify the codebase compiles and tests pass before pushing
            - If tests fail after rebase, try to fix the issues - they may be due to API changes in the merged PRs
            """;
    }

    /// <inheritdoc />
    public string GenerateRebaseInitialMessage(
        string branchName,
        string defaultBranch,
        IEnumerable<PullRequestInfo>? recentMergedPRs = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Rebase Request");
        sb.AppendLine();
        sb.AppendLine($"Please rebase branch `{branchName}` onto the latest `{defaultBranch}`.");
        sb.AppendLine();
        sb.AppendLine("Follow the workflow in your system prompt:");
        sb.AppendLine("1. Fetch the latest changes");
        sb.AppendLine("2. Analyze the commits to be rebased");
        sb.AppendLine("3. Perform the rebase");
        sb.AppendLine("4. Resolve any conflicts using the context below");
        sb.AppendLine("5. Run tests to verify no regressions");
        sb.AppendLine("6. Push with --force-with-lease when ready");

        var prList = recentMergedPRs?.ToList();
        if (prList != null && prList.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Recently Merged PRs to {defaultBranch}");
            sb.AppendLine();
            sb.AppendLine("Use this context to understand recent changes when resolving conflicts:");
            sb.AppendLine();

            foreach (var pr in prList.Take(10)) // Limit to 10 most recent
            {
                sb.AppendLine($"### PR #{pr.Number}: {pr.Title}");

                if (!string.IsNullOrWhiteSpace(pr.Body))
                {
                    var truncatedBody = TruncateBody(pr.Body, MaxPRBodyLength);
                    sb.AppendLine(truncatedBody);
                }

                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("## Context");
            sb.AppendLine();
            sb.AppendLine("No recently merged PRs available for context. If you encounter conflicts,");
            sb.AppendLine("use `git log` and `git show` to understand what changed on the target branch.");
        }

        return sb.ToString();
    }

    private static string TruncateBody(string body, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        // Clean up the body - remove excessive whitespace
        var cleaned = body.Trim();

        if (cleaned.Length <= maxLength)
            return cleaned;

        // Find a good break point (end of word or sentence)
        var truncated = cleaned[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated + "...";
    }
}
