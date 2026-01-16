using System.Collections.Concurrent;
using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Beads;
using Homespun.Features.Beads.Data;
using Homespun.Features.Beads.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Agents.Services;

/// <summary>
/// High-level orchestration service for agent workflows.
/// Uses the harness abstraction to support multiple AI backends.
/// </summary>
public class AgentWorkflowService : IAgentWorkflowService
{
    private readonly IAgentHarnessFactory _harnessFactory;
    private readonly PullRequestDataService _pullRequestService;
    private readonly IDataStore _dataStore;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitHubService _gitHubService;
    private readonly IBeadsService _beadsService;
    private readonly IBeadsIssueTransitionService _beadsTransitionService;
    private readonly ILogger<AgentWorkflowService> _logger;

    // Track which harness each entity is using
    private readonly ConcurrentDictionary<string, string> _entityHarnessMap = new();

    public AgentWorkflowService(
        IAgentHarnessFactory harnessFactory,
        PullRequestDataService pullRequestService,
        IDataStore dataStore,
        IGitWorktreeService worktreeService,
        IGitHubService gitHubService,
        IBeadsService beadsService,
        IBeadsIssueTransitionService beadsTransitionService,
        ILogger<AgentWorkflowService> logger)
    {
        _harnessFactory = harnessFactory;
        _pullRequestService = pullRequestService;
        _dataStore = dataStore;
        _worktreeService = worktreeService;
        _gitHubService = gitHubService;
        _beadsService = beadsService;
        _beadsTransitionService = beadsTransitionService;
        _logger = logger;
    }

    public async Task<WorkflowAgentStatus> StartAgentForPullRequestAsync(
        string pullRequestId,
        string? model = null,
        string? harnessType = null,
        CancellationToken ct = default)
    {
        var pullRequest = await _pullRequestService.GetByIdAsync(pullRequestId)
            ?? throw new InvalidOperationException($"Pull request {pullRequestId} not found");

        // Create worktree if it doesn't exist
        if (string.IsNullOrEmpty(pullRequest.WorktreePath))
        {
            if (string.IsNullOrEmpty(pullRequest.BranchName))
            {
                throw new InvalidOperationException(
                    $"Pull request {pullRequestId} does not have a branch name. Cannot create worktree.");
            }

            _logger.LogInformation("Creating worktree for PR {PullRequestId} on branch {BranchName}",
                pullRequestId, pullRequest.BranchName);

            var success = await _pullRequestService.StartDevelopmentAsync(pullRequestId);
            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to create worktree for pull request {pullRequestId} " +
                    $"(branch: {pullRequest.BranchName}). Check logs for git error details.");
            }

            // Refresh pull request to get the worktree path
            pullRequest = await _pullRequestService.GetByIdAsync(pullRequestId)
                ?? throw new InvalidOperationException($"Pull request {pullRequestId} not found after creating worktree");
        }

        // Get the appropriate harness
        var harness = GetHarness(harnessType);

        // Check if already running
        var existingAgent = harness.GetAgentForEntity(pullRequestId);
        if (existingAgent != null && existingAgent.Status == AgentInstanceStatus.Running)
        {
            _logger.LogInformation("Agent already running for PR {PullRequestId}", pullRequestId);
            return new WorkflowAgentStatus
            {
                EntityId = pullRequestId,
                Agent = existingAgent,
                HarnessType = harness.HarnessType
            };
        }

        // Pull latest changes before starting agent
        _logger.LogInformation("Pulling latest changes for PR {PullRequestId}", pullRequestId);
        var pullSuccess = await _worktreeService.PullLatestAsync(pullRequest.WorktreePath!);
        if (!pullSuccess)
        {
            _logger.LogWarning("Failed to pull latest changes for PR {PullRequestId}, continuing anyway", pullRequestId);
        }

        // Determine effective model
        var effectiveModel = model ?? pullRequest.Project.DefaultModel;

        // Start agent using harness
        var options = new AgentStartOptions
        {
            EntityId = pullRequestId,
            WorkingDirectory = pullRequest.WorktreePath!,
            SessionTitle = pullRequest.Title,
            Model = effectiveModel,
            ContinueSession = false
        };

        var agent = await harness.StartAgentAsync(options, ct);

        // Track the harness for this entity
        _entityHarnessMap[pullRequestId] = harness.HarnessType;

        _logger.LogInformation(
            "Agent started for PR {PullRequestId}, session {SessionId}",
            pullRequestId, agent.ActiveSessionId);

        return new WorkflowAgentStatus
        {
            EntityId = pullRequestId,
            Agent = agent,
            HarnessType = harness.HarnessType
        };
    }

    public async Task<WorkflowAgentStatus> StartAgentForBeadsIssueAsync(
        string projectId,
        string issueId,
        AgentMode agentMode = AgentMode.Building,
        string? model = null,
        string? harnessType = null,
        CancellationToken ct = default)
    {
        var project = _dataStore.GetProject(projectId)
            ?? throw new InvalidOperationException($"Project {projectId} not found");

        // Get the beads issue
        var issue = await _beadsService.GetIssueAsync(project.LocalPath, issueId)
            ?? throw new InvalidOperationException($"Beads issue {issueId} not found in project {projectId}");

        // Parse group and branch ID from hsp: label
        var branchInfo = BeadsBranchLabel.Parse(issue.Labels);
        if (branchInfo == null)
        {
            throw new InvalidOperationException(
                $"Issue {issueId} does not have an hsp: label. " +
                "Please add a label like 'hsp:frontend/-/update-page' to specify the group and branch ID.");
        }

        var (group, branchId) = branchInfo.Value;

        // Transition to InProgress status
        var transitionResult = await _beadsTransitionService.TransitionToInProgressAsync(projectId, issueId);
        if (!transitionResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to transition issue {issueId} to InProgress: {transitionResult.Error}");
        }

        try
        {
            // Check for existing metadata or create new
            var metadata = _dataStore.GetBeadsIssueMetadata(issueId);
            string worktreePath;
            string branchName;

            if (metadata != null && !string.IsNullOrEmpty(metadata.WorktreePath))
            {
                worktreePath = metadata.WorktreePath;
                branchName = metadata.BranchName ?? BeadsBranchHelper.GenerateBranchName(
                    group, issue.Type.ToString().ToLowerInvariant(), branchId, issueId);
            }
            else
            {
                // Generate branch name: {group}/{type}/{branch-id}+{beads-id}
                branchName = BeadsBranchHelper.GenerateBranchName(
                    group, issue.Type.ToString().ToLowerInvariant(), branchId, issueId);

                // Create worktree
                _logger.LogInformation("Creating worktree for beads issue {IssueId} on branch {BranchName}",
                    issueId, branchName);

                var createdWorktreePath = await _worktreeService.CreateWorktreeAsync(
                    project.LocalPath,
                    branchName,
                    createBranch: true,
                    baseBranch: project.DefaultBranch);

                worktreePath = createdWorktreePath
                    ?? throw new InvalidOperationException($"Failed to create worktree for issue {issueId}");

                // Store metadata
                metadata = new BeadsIssueMetadata
                {
                    IssueId = issueId,
                    ProjectId = projectId,
                    BranchName = branchName,
                    WorktreePath = worktreePath
                };
                await _dataStore.AddBeadsIssueMetadataAsync(metadata);
            }

            // Get the appropriate harness
            var harness = GetHarness(harnessType);

            // Check if already running
            var existingAgent = harness.GetAgentForEntity(issueId);
            if (existingAgent != null && existingAgent.Status == AgentInstanceStatus.Running)
            {
                _logger.LogInformation("Agent already running for issue {IssueId}", issueId);
                return new WorkflowAgentStatus
                {
                    EntityId = issueId,
                    Agent = existingAgent,
                    HarnessType = harness.HarnessType
                };
            }

            // Pull latest changes before starting agent
            _logger.LogInformation("Pulling latest changes for issue {IssueId}", issueId);
            var pullSuccess = await _worktreeService.PullLatestAsync(worktreePath);
            if (!pullSuccess)
            {
                _logger.LogWarning("Failed to pull latest changes for issue {IssueId}, continuing anyway", issueId);
            }

            // Determine effective model
            var effectiveModel = model ?? project.DefaultModel;

            // Build initial prompt
            var initialPrompt = BuildInitialPromptForBeadsIssue(issue, branchName, project.DefaultBranch, agentMode);

            // Start agent using harness
            var options = new AgentStartOptions
            {
                EntityId = issueId,
                WorkingDirectory = worktreePath,
                SessionTitle = issue.Title,
                Model = effectiveModel,
                ContinueSession = false,
                InitialPrompt = AgentPrompt.FromText(initialPrompt, effectiveModel)
            };

            var agent = await harness.StartAgentAsync(options, ct);

            // Track the harness for this entity
            _entityHarnessMap[issueId] = harness.HarnessType;

            // Update metadata with agent info
            metadata.ActiveAgentServerId = agent.AgentId;
            metadata.AgentStartedAt = DateTime.UtcNow;
            await _dataStore.UpdateBeadsIssueMetadataAsync(metadata);

            _logger.LogInformation(
                "Agent started for issue {IssueId}, session {SessionId}",
                issueId, agent.ActiveSessionId);

            return new WorkflowAgentStatus
            {
                EntityId = issueId,
                Agent = agent,
                HarnessType = harness.HarnessType
            };
        }
        catch (Exception ex)
        {
            // Handle agent failure - revert status to Open
            await _beadsTransitionService.HandleAgentFailureAsync(projectId, issueId, ex.Message);
            throw;
        }
    }

    public async Task StopAgentAsync(string entityId, CancellationToken ct = default)
    {
        // Find the harness for this entity
        var harness = FindHarnessForEntity(entityId);
        if (harness != null)
        {
            await harness.StopAgentAsync(entityId, ct);
            _entityHarnessMap.TryRemove(entityId, out _);
        }

        _logger.LogInformation("Agent stopped for entity {EntityId}", entityId);

        // Handle agent completion
        await TryHandleAgentCompletionForEntityAsync(entityId, ct);
    }

    public async Task<WorkflowAgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default)
    {
        var harness = FindHarnessForEntity(pullRequestId);
        if (harness == null)
            return null;

        var agent = harness.GetAgentForEntity(pullRequestId);
        if (agent == null || agent.Status != AgentInstanceStatus.Running)
            return null;

        return new WorkflowAgentStatus
        {
            EntityId = pullRequestId,
            Agent = agent,
            HarnessType = harness.HarnessType
        };
    }

    public async Task<WorkflowAgentStatus?> GetAgentStatusForBeadsIssueAsync(string issueId, CancellationToken ct = default)
    {
        return await GetAgentStatusAsync(issueId, ct);
    }

    public async Task<AgentMessage> SendPromptAsync(string entityId, string prompt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SendPromptAsync called for entity {EntityId}. Prompt length: {PromptLength} chars",
            entityId, prompt.Length);

        var harness = FindHarnessForEntity(entityId)
            ?? throw new InvalidOperationException($"No agent running for entity {entityId}");

        var agentPrompt = AgentPrompt.FromText(prompt);
        var response = await harness.SendPromptAsync(entityId, agentPrompt, ct);

        _logger.LogInformation(
            "Prompt sent successfully to entity {EntityId}. Response message ID: {MessageId}",
            entityId, response.Id);

        return response;
    }

    public async Task HandleAgentCompletionForBeadsIssueAsync(string projectId, string issueId, CancellationToken ct = default)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found when handling agent completion for issue {IssueId}",
                projectId, issueId);
            return;
        }

        var issue = await _beadsService.GetIssueAsync(project.LocalPath, issueId);
        if (issue == null)
        {
            _logger.LogWarning("Issue {IssueId} not found when handling agent completion", issueId);
            return;
        }

        if (issue.Status != BeadsIssueStatus.InProgress)
        {
            _logger.LogDebug("Issue {IssueId} is not InProgress (status: {Status}), skipping completion handling",
                issueId, issue.Status);
            return;
        }

        var metadata = _dataStore.GetBeadsIssueMetadata(issueId);
        if (metadata == null || string.IsNullOrEmpty(metadata.BranchName))
        {
            _logger.LogWarning("No metadata found for issue {IssueId}", issueId);
            await _beadsTransitionService.TransitionToAwaitingPRAsync(projectId, issueId);
            return;
        }

        _logger.LogInformation("Handling agent completion for issue {IssueId}", issueId);

        // Check GitHub for PR on this branch
        try
        {
            var prs = await _gitHubService.GetOpenPullRequestsAsync(projectId);
            var matchingPr = prs.FirstOrDefault(pr =>
                pr.BranchName?.Equals(metadata.BranchName, StringComparison.OrdinalIgnoreCase) == true);

            if (matchingPr != null && matchingPr.Number > 0)
            {
                // PR exists - close the beads issue and create tracked PR
                _logger.LogInformation("Found GitHub PR #{PrNumber} for issue {IssueId}, closing issue and creating tracked PR",
                    matchingPr.Number, issueId);

                await PromoteBeadsIssueWithPrAsync(projectId, issueId, matchingPr.Number);
            }
            else
            {
                // No PR found - transition to AwaitingPR
                _logger.LogInformation("No GitHub PR found for issue {IssueId}, transitioning to AwaitingPR", issueId);
                await _beadsTransitionService.TransitionToAwaitingPRAsync(projectId, issueId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking GitHub for PR on issue {IssueId}", issueId);
            // Still transition to AwaitingPR on error
            await _beadsTransitionService.TransitionToAwaitingPRAsync(projectId, issueId);
        }

        // Clear agent server ID in metadata
        metadata.ActiveAgentServerId = null;
        metadata.AgentStartedAt = null;
        await _dataStore.UpdateBeadsIssueMetadataAsync(metadata);
    }

    public IReadOnlyList<AgentInstance> GetAllRunningAgents()
    {
        var allAgents = new List<AgentInstance>();
        foreach (var harnessType in _harnessFactory.AvailableHarnessTypes)
        {
            var harness = _harnessFactory.GetHarness(harnessType);
            allAgents.AddRange(harness.GetRunningAgents());
        }
        return allAgents;
    }

    public string? GetHarnessTypeForEntity(string entityId)
    {
        if (_entityHarnessMap.TryGetValue(entityId, out var harnessType))
            return harnessType;

        // Check all harnesses
        foreach (var type in _harnessFactory.AvailableHarnessTypes)
        {
            var harness = _harnessFactory.GetHarness(type);
            if (harness.GetAgentForEntity(entityId) != null)
                return type;
        }

        return null;
    }

    #region Private Methods

    private IAgentHarness GetHarness(string? harnessType)
    {
        return harnessType != null
            ? _harnessFactory.GetHarness(harnessType)
            : _harnessFactory.GetDefaultHarness();
    }

    private IAgentHarness? FindHarnessForEntity(string entityId)
    {
        // First check our tracking map
        if (_entityHarnessMap.TryGetValue(entityId, out var trackedType))
        {
            return _harnessFactory.GetHarness(trackedType);
        }

        // Search all harnesses
        foreach (var type in _harnessFactory.AvailableHarnessTypes)
        {
            var harness = _harnessFactory.GetHarness(type);
            if (harness.GetAgentForEntity(entityId) != null)
            {
                _entityHarnessMap[entityId] = type;
                return harness;
            }
        }

        return null;
    }

    private async Task TryHandleAgentCompletionForEntityAsync(string entityId, CancellationToken ct)
    {
        // Check if this is a beads issue
        var beadsMetadata = _dataStore.GetBeadsIssueMetadata(entityId);
        if (beadsMetadata != null)
        {
            await HandleAgentCompletionForBeadsIssueAsync(beadsMetadata.ProjectId, entityId, ct);
            return;
        }

        _logger.LogDebug("No beads issue found for entity {EntityId}", entityId);
    }

    private async Task PromoteBeadsIssueWithPrAsync(string projectId, string issueId, int prNumber)
    {
        try
        {
            var project = _dataStore.GetProject(projectId);
            if (project == null)
            {
                _logger.LogWarning("Project {ProjectId} not found when promoting issue {IssueId}", projectId, issueId);
                return;
            }

            var issue = await _beadsService.GetIssueAsync(project.LocalPath, issueId);
            var metadata = _dataStore.GetBeadsIssueMetadata(issueId);

            if (issue == null || metadata == null)
            {
                _logger.LogWarning("Issue or metadata not found for {IssueId}", issueId);
                return;
            }

            // Create a PullRequest entity with the beads issue ID
            var pullRequest = new PullRequest
            {
                ProjectId = projectId,
                Title = issue.Title,
                Description = issue.Description,
                BranchName = metadata.BranchName,
                GitHubPRNumber = prNumber,
                WorktreePath = metadata.WorktreePath,
                BeadsIssueId = issueId,
                Status = OpenPullRequestStatus.InDevelopment
            };

            await _dataStore.AddPullRequestAsync(pullRequest);

            // Close the beads issue
            await _beadsTransitionService.TransitionToCompleteAsync(projectId, issueId, prNumber);

            // Sync GitHub data to populate PR details
            await _gitHubService.SyncPullRequestsAsync(projectId);

            _logger.LogInformation(
                "Issue {IssueId} promoted to PR #{PrNumber}",
                issueId, prNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting issue {IssueId} to PR #{PrNumber}", issueId, prNumber);
        }
    }

    internal static string BuildInitialPromptForBeadsIssue(
        BeadsIssue issue,
        string branchName,
        string baseBranch,
        AgentMode agentMode)
    {
        var prompt = $"""
            Please implement the following change:

            **Title:** {issue.Title}
            **Issue ID:** {issue.Id}
            **Branch:** {branchName}
            """;

        if (issue.Priority.HasValue)
        {
            prompt += $"\n**Priority:** P{issue.Priority}";
        }

        if (!string.IsNullOrEmpty(issue.Description))
        {
            prompt += $"\n\n**Description:**\n{issue.Description}";
        }

        if (agentMode == AgentMode.Planning)
        {
            prompt += $"""


            ## Workflow Instructions

            1. Review the change described above carefully
            2. Ask any clarifying questions you may have. For each clarifying question, provide options in order of most recommended (top) to least recommended (bottom).
            3. Once you have sufficient information, create an implementation plan
            4. Wait for approval before implementing

            **Important:** Focus on understanding the requirements thoroughly before suggesting an implementation approach.
            """;
        }
        else // AgentMode.Building
        {
            prompt += $"""


            ## Workflow Instructions

            1. Implement the change described above
            2. Write tests for your implementation where appropriate
            3. Commit your changes to the current branch ({branchName})
            4. When complete, create a pull request using the following command:
               ```
               gh pr create --base {baseBranch} --title "{issue.Title}" --body "Implements {issue.Id}"
               ```

            **Important:** After creating the PR, signal completion so the system can verify and track the PR.
            """;
        }

        return prompt;
    }

    #endregion
}
