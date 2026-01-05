using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Roadmap;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// High-level orchestration service for agent workflows.
/// </summary>
public class AgentWorkflowService : IAgentWorkflowService
{
    private readonly IOpenCodeServerManager _serverManager;
    private readonly IOpenCodeClient _client;
    private readonly IOpenCodeConfigGenerator _configGenerator;
    private readonly PullRequestDataService _pullRequestService;
    private readonly IDataStore _dataStore;
    private readonly IRoadmapService _roadmapService;
    private readonly IFutureChangeTransitionService _transitionService;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IAgentCompletionMonitor _completionMonitor;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<AgentWorkflowService> _logger;

    // Track which changes have active monitoring tasks (changeId -> (projectId, cts))
    private readonly Dictionary<string, (string ProjectId, CancellationTokenSource Cts)> _monitoringTasks = new();

    public AgentWorkflowService(
        IOpenCodeServerManager serverManager,
        IOpenCodeClient client,
        IOpenCodeConfigGenerator configGenerator,
        PullRequestDataService pullRequestService,
        IDataStore dataStore,
        IRoadmapService roadmapService,
        IFutureChangeTransitionService transitionService,
        IGitWorktreeService worktreeService,
        IAgentCompletionMonitor completionMonitor,
        IGitHubService gitHubService,
        ILogger<AgentWorkflowService> logger)
    {
        _serverManager = serverManager;
        _client = client;
        _configGenerator = configGenerator;
        _pullRequestService = pullRequestService;
        _dataStore = dataStore;
        _roadmapService = roadmapService;
        _transitionService = transitionService;
        _worktreeService = worktreeService;
        _completionMonitor = completionMonitor;
        _gitHubService = gitHubService;
        _logger = logger;
    }

    public async Task<AgentStatus> StartAgentForPullRequestAsync(
        string pullRequestId, 
        string? model = null, 
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
                _logger.LogError(
                    "Failed to create worktree for PR {PullRequestId} branch {BranchName}. " +
                    "Project: {ProjectId}, LocalPath: {LocalPath}",
                    pullRequestId, 
                    pullRequest.BranchName,
                    pullRequest.ProjectId,
                    pullRequest.Project?.LocalPath ?? "unknown");
                
                throw new InvalidOperationException(
                    $"Failed to create worktree for pull request {pullRequestId} " +
                    $"(branch: {pullRequest.BranchName}). Check logs for git error details.");
            }

            // Refresh pull request to get the worktree path
            pullRequest = await _pullRequestService.GetByIdAsync(pullRequestId)
                ?? throw new InvalidOperationException($"Pull request {pullRequestId} not found after creating worktree");
        }

        // Check if server already running
        var existingServer = _serverManager.GetServerForEntity(pullRequestId);
        if (existingServer != null && existingServer.Status == OpenCodeServerStatus.Running)
        {
            _logger.LogInformation("Server already running for PR {PullRequestId}", pullRequestId);
            return await BuildAgentStatusAsync(existingServer, ct);
        }

        // Pull latest changes before starting agent
        _logger.LogInformation("Pulling latest changes for PR {PullRequestId}", pullRequestId);
        var pullSuccess = await _worktreeService.PullLatestAsync(pullRequest.WorktreePath!);
        if (!pullSuccess)
        {
            _logger.LogWarning("Failed to pull latest changes for PR {PullRequestId}, continuing anyway", pullRequestId);
        }

        // Generate config with model from: parameter -> project -> global default
        var effectiveModel = model ?? pullRequest.Project.DefaultModel;
        var config = _configGenerator.CreateDefaultConfig(effectiveModel);
        await _configGenerator.GenerateConfigAsync(pullRequest.WorktreePath!, config, ct);

        // Start server
        var server = await _serverManager.StartServerAsync(pullRequestId, pullRequest.WorktreePath!, continueSession: false, ct);

        // Get or create session
        var status = await BuildAgentStatusAsync(server, ct);

        // If there's an existing session, use it. Otherwise create new.
        if (status.Sessions.Count == 0)
        {
            var session = await _client.CreateSessionAsync(server.BaseUrl, pullRequest.Title, ct);
            status.ActiveSession = session;
            status.Sessions.Add(session);
            server.ActiveSessionId = session.Id;
        }
        else
        {
            // Use most recent session
            var mostRecent = status.Sessions.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).First();
            status.ActiveSession = mostRecent;
            server.ActiveSessionId = mostRecent.Id;
        }

        _logger.LogInformation(
            "Agent started for PR {PullRequestId} on port {Port}, session {SessionId}",
            pullRequestId, server.Port, status.ActiveSession?.Id);

        return status;
    }

    public async Task<AgentStatus> StartAgentForFutureChangeAsync(
        string projectId, 
        string changeId, 
        string? model = null, 
        CancellationToken ct = default)
    {
        // Find the change in the roadmap
        var change = await _roadmapService.FindChangeByIdAsync(projectId, changeId)
            ?? throw new InvalidOperationException($"Roadmap change {changeId} not found in project {projectId}");

        // Transition to InProgress status
        var transitionResult = await _transitionService.TransitionToInProgressAsync(projectId, changeId);
        if (!transitionResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to transition change {changeId} to InProgress: {transitionResult.Error}");
        }

        try
        {
            // Create worktree WITHOUT promoting to PR
            var worktreePath = change.WorktreePath;
            if (string.IsNullOrEmpty(worktreePath))
            {
                worktreePath = await _roadmapService.CreateWorktreeForChangeAsync(projectId, changeId)
                    ?? throw new InvalidOperationException($"Failed to create worktree for change {changeId}");
            }

            // Check if server already running
            var existingServer = _serverManager.GetServerForEntity(changeId);
            if (existingServer != null && existingServer.Status == OpenCodeServerStatus.Running)
            {
                _logger.LogInformation("Server already running for change {ChangeId}", changeId);
                return await BuildAgentStatusAsync(existingServer, ct);
            }

            // Pull latest changes before starting agent
            _logger.LogInformation("Pulling latest changes for change {ChangeId}", changeId);
            var pullSuccess = await _worktreeService.PullLatestAsync(worktreePath);
            if (!pullSuccess)
            {
                _logger.LogWarning("Failed to pull latest changes for change {ChangeId}, continuing anyway", changeId);
            }

            // Generate config
            var config = _configGenerator.CreateDefaultConfig(model);
            await _configGenerator.GenerateConfigAsync(worktreePath, config, ct);

            // Start server using changeId as the entity ID
            var server = await _serverManager.StartServerAsync(changeId, worktreePath, continueSession: false, ct);

            // Create session
            var session = await _client.CreateSessionAsync(server.BaseUrl, change.Title, ct);
            server.ActiveSessionId = session.Id;

            // Store agent server ID on the change
            await _roadmapService.UpdateChangeAgentAsync(projectId, changeId, server.Id);

            // Send initial prompt with change instructions
            var initialPrompt = BuildInitialPrompt(change);
            await _client.SendPromptAsyncNoWait(
                server.BaseUrl, 
                session.Id, 
                PromptRequest.FromText(initialPrompt, model), 
                ct);

            _logger.LogInformation(
                "Sent initial prompt for change {ChangeId} to session {SessionId}",
                changeId, session.Id);

            // Start background monitoring for PR creation (fire and forget)
            StartMonitoringForPrCreation(projectId, changeId, server.BaseUrl, changeId);

            // Build and return status
            var status = new AgentStatus
            {
                EntityId = changeId,
                Server = server,
                ActiveSession = session,
                Sessions = [session]
            };

            _logger.LogInformation(
                "Agent started for change {ChangeId} on port {Port}, session {SessionId}. Server URL: {ServerUrl}",
                changeId, server.Port, session.Id, server.BaseUrl);

            return status;
        }
        catch (Exception ex)
        {
            // Handle agent failure - revert status to Pending
            await _transitionService.HandleAgentFailureAsync(projectId, changeId, ex.Message);
            throw;
        }
    }

    public async Task StopAgentAsync(string entityId, CancellationToken ct = default)
    {
        // Cancel any monitoring task for this entity and get the projectId
        string? projectId = null;
        if (_monitoringTasks.TryGetValue(entityId, out var monitoringInfo))
        {
            projectId = monitoringInfo.ProjectId;
            monitoringInfo.Cts.Cancel();
            _monitoringTasks.Remove(entityId);
        }

        // Stop the server
        await _serverManager.StopServerAsync(entityId, ct);
        _logger.LogInformation("Agent stopped for entity {EntityId}", entityId);

        // Handle agent completion if we have the projectId from monitoring
        if (projectId != null)
        {
            await HandleAgentCompletionAsync(projectId, entityId, ct);
        }
        else
        {
            // Try to find the project by looking up the change
            await TryHandleAgentCompletionForEntityAsync(entityId, ct);
        }
    }

    public async Task<AgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(pullRequestId);
        if (server == null || server.Status != OpenCodeServerStatus.Running)
        {
            return null;
        }

        return await BuildAgentStatusAsync(server, ct);
    }

    public async Task<AgentStatus?> GetAgentStatusForChangeAsync(string changeId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(changeId);
        if (server == null || server.Status != OpenCodeServerStatus.Running)
        {
            return null;
        }

        return await BuildAgentStatusAsync(server, ct);
    }

    public async Task<OpenCodeMessage> SendPromptAsync(string entityId, string prompt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SendPromptAsync called for entity {EntityId}. Prompt length: {PromptLength} chars. Preview: {PromptPreview}",
            entityId,
            prompt.Length,
            prompt.Length > 100 ? prompt[..100] + "..." : prompt);
        
        var server = _serverManager.GetServerForEntity(entityId)
            ?? throw new InvalidOperationException($"No agent running for entity {entityId}");

        if (string.IsNullOrEmpty(server.ActiveSessionId))
        {
            throw new InvalidOperationException($"No active session for entity {entityId}");
        }

        _logger.LogInformation(
            "Sending prompt to agent at {BaseUrl}, session {SessionId}",
            server.BaseUrl, server.ActiveSessionId);

        var request = PromptRequest.FromText(prompt);
        var response = await _client.SendPromptAsync(server.BaseUrl, server.ActiveSessionId, request, ct);
        
        _logger.LogInformation(
            "Prompt sent successfully to entity {EntityId}. Response message ID: {MessageId}",
            entityId, response.Info.Id);
        
        return response;
    }

    public async Task HandleAgentCompletionAsync(string projectId, string changeId, CancellationToken ct = default)
    {
        var change = await _roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            _logger.LogWarning("Change {ChangeId} not found when handling agent completion", changeId);
            return;
        }

        if (change.Status != FutureChangeStatus.InProgress)
        {
            _logger.LogDebug("Change {ChangeId} is not InProgress (status: {Status}), skipping completion handling", 
                changeId, change.Status);
            return;
        }

        _logger.LogInformation("Handling agent completion for change {ChangeId}", changeId);

        // Check GitHub for PR on this branch
        try
        {
            var prs = await _gitHubService.GetOpenPullRequestsAsync(projectId);
            var matchingPr = prs.FirstOrDefault(pr => 
                pr.BranchName?.Equals(changeId, StringComparison.OrdinalIgnoreCase) == true);

            if (matchingPr != null && matchingPr.Number > 0)
            {
                // PR exists - promote change to tracked PR
                _logger.LogInformation("Found GitHub PR #{PrNumber} for change {ChangeId}, promoting to tracked PR",
                    matchingPr.Number, changeId);
                
                await PromoteChangeWithPrAsync(projectId, changeId, matchingPr.Number);
            }
            else
            {
                // No PR found - transition to AwaitingPR
                _logger.LogInformation("No GitHub PR found for change {ChangeId}, transitioning to AwaitingPR", changeId);
                await _transitionService.TransitionToAwaitingPRAsync(projectId, changeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking GitHub for PR on change {ChangeId}", changeId);
            // Still transition to AwaitingPR on error
            await _transitionService.TransitionToAwaitingPRAsync(projectId, changeId);
        }

        // Clear agent server ID
        await _roadmapService.UpdateChangeAgentAsync(projectId, changeId, null);
    }

    private void StartMonitoringForPrCreation(string projectId, string changeId, string serverBaseUrl, string branchName)
    {
        var cts = new CancellationTokenSource();
        _monitoringTasks[changeId] = (projectId, cts);

        _ = MonitorAgentForCompletionAsync(projectId, changeId, serverBaseUrl, branchName, cts.Token);
    }

    private async Task MonitorAgentForCompletionAsync(
        string projectId,
        string changeId,
        string serverBaseUrl,
        string branchName,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Starting SSE monitoring for change {ChangeId}", changeId);

            // Monitor SSE events until server stops (no timeout)
            var result = await _completionMonitor.MonitorForPrCreationAsync(
                serverBaseUrl, projectId, branchName, ct);
            
            if (result.Success && result.PrNumber.HasValue)
            {
                // PR detected via SSE - promote change immediately
                _logger.LogInformation(
                    "PR #{PrNumber} detected via SSE for change {ChangeId}",
                    result.PrNumber.Value, changeId);
                
                await PromoteChangeWithPrAsync(projectId, changeId, result.PrNumber.Value);
                await _roadmapService.UpdateChangeAgentAsync(projectId, changeId, null);
            }
            else
            {
                _logger.LogInformation(
                    "SSE monitoring ended without PR detection for change {ChangeId}: {Error}",
                    changeId, result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SSE monitoring cancelled for change {ChangeId}", changeId);
        }
        catch (HttpRequestException ex)
        {
            // Server stopped - this is expected when the agent finishes
            _logger.LogInformation(
                "Agent server stopped for change {ChangeId} (HTTP error: {Message})",
                changeId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SSE monitoring for change {ChangeId}", changeId);
        }
        finally
        {
            _monitoringTasks.Remove(changeId);
        }
    }

    private async Task PromoteChangeWithPrAsync(string projectId, string changeId, int prNumber)
    {
        try
        {
            // Promote change to PR record
            var pullRequest = await _roadmapService.PromoteCompletedChangeAsync(projectId, changeId, prNumber);
            
            if (pullRequest != null)
            {
                // Sync GitHub data to populate PR details
                await _gitHubService.SyncPullRequestsAsync(projectId);
                
                _logger.LogInformation(
                    "Change {ChangeId} promoted to PR #{PrNumber}", 
                    changeId, prNumber);
            }
            else
            {
                _logger.LogWarning("Failed to promote change {ChangeId} to PR", changeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting change {ChangeId} to PR #{PrNumber}", changeId, prNumber);
        }
    }

    private async Task TryHandleAgentCompletionForEntityAsync(string entityId, CancellationToken ct)
    {
        // Try to find a FutureChange with this ID in any project
        // This is a bit inefficient but necessary since we don't know the project ID
        var projects = _dataStore.Projects;
        
        foreach (var project in projects)
        {
            var change = await _roadmapService.FindChangeByIdAsync(project.Id, entityId);
            if (change != null && change.Status == FutureChangeStatus.InProgress)
            {
                await HandleAgentCompletionAsync(project.Id, entityId, ct);
                return;
            }
        }

        // If we get here, it's either a PR (not a FutureChange) or the change wasn't found
        _logger.LogDebug("No InProgress FutureChange found for entity {EntityId}", entityId);
    }

    private async Task<AgentStatus> BuildAgentStatusAsync(OpenCodeServer server, CancellationToken ct)
    {
        var sessions = await _client.ListSessionsAsync(server.BaseUrl, ct);
        var activeSession = sessions.FirstOrDefault(s => s.Id == server.ActiveSessionId);

        return new AgentStatus
        {
            EntityId = server.EntityId,
            Server = server,
            ActiveSession = activeSession,
            Sessions = sessions
        };
    }

    internal static string BuildInitialPrompt(FutureChange change)
    {
        // Determine the base branch for the PR
        var baseBranch = change.Parents.Count > 0 ? change.Parents[0] : "main";
        
        var prompt = $"""
            Please implement the following change:

            **Title:** {change.Title}
            **Branch:** {change.Id}
            """;

        if (!string.IsNullOrEmpty(change.Description))
        {
            prompt += $"\n\n**Description:** {change.Description}";
        }

        if (!string.IsNullOrEmpty(change.Instructions))
        {
            prompt += $"\n\n**Instructions:**\n{change.Instructions}";
        }

        prompt += $"""


            ## Workflow Instructions

            1. Implement the change described above
            2. Write tests for your implementation where appropriate
            3. Commit your changes to the current branch ({change.Id})
            4. When complete, create a pull request using the following command:
               ```
               gh pr create --base {baseBranch} --title "{change.Title}" --body "Implements {change.ShortTitle}"
               ```

            **Important:** After creating the PR, signal completion so the system can verify and track the PR.
            """;

        return prompt;
    }
}
