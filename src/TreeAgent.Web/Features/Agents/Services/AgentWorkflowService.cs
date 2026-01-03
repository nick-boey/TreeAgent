using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using TreeAgent.Web.Features.Roadmap;

namespace TreeAgent.Web.Features.Agents.Services;

/// <summary>
/// Service for managing agent workflow and PR status transitions.
/// </summary>
public class AgentWorkflowService(
    TreeAgentDbContext db,
    ICommandRunner commandRunner,
    IRoadmapService roadmapService)
{
    private readonly ICommandRunner _commandRunner = commandRunner;

    #region 5.1 Agent Status Updates

    /// <summary>
    /// Called when an agent starts working. Updates the pull request to InDevelopment.
    /// </summary>
    public async Task OnAgentStartedAsync(string agentId)
    {
        var agent = await db.Agents
            .Include(a => a.PullRequest)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.PullRequest == null) return;

        if (agent.PullRequest.Status == OpenPullRequestStatus.ReadyForReview)
        {
            agent.PullRequest.Status = OpenPullRequestStatus.InDevelopment;
            agent.PullRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Called when an agent completes its work. Updates the pull request to ReadyForReview.
    /// </summary>
    public async Task OnAgentCompletedAsync(string agentId)
    {
        var agent = await db.Agents
            .Include(a => a.PullRequest)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.PullRequest == null) return;

        if (agent.PullRequest.Status == OpenPullRequestStatus.InDevelopment)
        {
            agent.PullRequest.Status = OpenPullRequestStatus.ReadyForReview;
            agent.PullRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Checks if the agent's work only modified ROADMAP.json (plan update only).
    /// </summary>
    public async Task<bool> IsPlanUpdateOnlyAsync(string agentId)
    {
        var agent = await db.Agents
            .Include(a => a.PullRequest)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.PullRequest == null) return false;

        return await roadmapService.IsPlanUpdateOnlyAsync(agent.PullRequest.Id);
    }

    #endregion

    #region 5.2 Review Comment Handling

    /// <summary>
    /// Called when review comments are received on a PR.
    /// Transitions the pull request back to InDevelopment.
    /// </summary>
    public async Task OnReviewCommentsReceivedAsync(string pullRequestId)
    {
        var pullRequest = await db.PullRequests.FindAsync(pullRequestId);
        if (pullRequest == null) return;

        if (pullRequest.Status == OpenPullRequestStatus.ReadyForReview)
        {
            pullRequest.Status = OpenPullRequestStatus.InDevelopment;
            pullRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Spawns a new agent to address review comments.
    /// </summary>
    public async Task<Agent?> SpawnAgentForReviewAsync(string pullRequestId, string reviewComments)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == pullRequestId);

        if (pullRequest == null) return null;

        // Build system prompt with review context
        var systemPrompt = BuildReviewSystemPrompt(pullRequest, reviewComments);

        var agent = new Agent
        {
            PullRequestId = pullRequestId,
            SystemPrompt = systemPrompt,
            Status = AgentStatus.Idle
        };

        db.Agents.Add(agent);

        // Update pull request status
        pullRequest.Status = OpenPullRequestStatus.InDevelopment;
        pullRequest.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return agent;
    }

    private static string BuildReviewSystemPrompt(PullRequest pullRequest, string reviewComments)
    {
        return $"""
            You are working on the pull request: {pullRequest.Title}

            This pull request has received review comments that need to be addressed.

            ## Review Comments
            {reviewComments}

            ## Instructions
            1. Read and understand the review comments
            2. Make the necessary changes to address the feedback
            3. Ensure tests pass after your changes
            4. Commit your changes with a descriptive message

            Please address all the review comments.
            """;
    }

    #endregion

    #region Starting Work on Future Changes

    /// <summary>
    /// Starts work on a future change from the roadmap.
    /// Creates a pull request, worktree, and agent.
    /// </summary>
    public async Task<StartWorkResult> StartWorkOnFutureChangeAsync(string projectId, string changeId)
    {
        // Get the change details for instructions
        var change = await roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return new StartWorkResult(null, null);
        }

        // Promote the change (creates pull request and worktree)
        var pullRequest = await roadmapService.PromoteChangeAsync(projectId, changeId);
        if (pullRequest == null)
        {
            return new StartWorkResult(null, null);
        }

        // Reload to get full pull request with relationships
        pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .FirstOrDefaultAsync(pr => pr.Id == pullRequest.Id);

        if (pullRequest == null)
        {
            return new StartWorkResult(null, null);
        }

        // Create agent with instructions from the change
        var systemPrompt = BuildWorkSystemPrompt(pullRequest, change);

        var agent = new Agent
        {
            PullRequestId = pullRequest.Id,
            SystemPrompt = systemPrompt,
            Status = AgentStatus.Idle
        };

        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        return new StartWorkResult(pullRequest, agent);
    }

    private static string BuildWorkSystemPrompt(PullRequest pullRequest, RoadmapChange change)
    {
        var prompt = $"""
            You are working on: {change.Title}

            ## Description
            {change.Description ?? "No description provided."}

            ## Type
            {change.Type}

            ## Group
            {change.Group}

            """;

        if (!string.IsNullOrEmpty(change.Instructions))
        {
            prompt += $"""

            ## Implementation Instructions
            {change.Instructions}

            """;
        }

        prompt += """

            ## General Guidelines
            1. Follow TDD practices where applicable
            2. Write clean, maintainable code
            3. Ensure all tests pass before completing
            4. Commit your changes with descriptive messages
            """;

        return prompt;
    }

    #endregion
}
