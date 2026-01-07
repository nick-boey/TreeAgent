using Homespun.Features.Beads;
using Homespun.Features.Beads.Data;
using Homespun.Features.Beads.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

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
    private readonly IGitWorktreeService _worktreeService;
    private readonly IAgentCompletionMonitor _completionMonitor;
    private readonly IGitHubService _gitHubService;
    private readonly IBeadsService _beadsService;
    private readonly IBeadsIssueTransitionService _beadsTransitionService;
    private readonly ILogger<AgentWorkflowService> _logger;

    // Track which changes have active monitoring tasks (entityId -> (projectId, cts))
    private readonly Dictionary<string, (string ProjectId, CancellationTokenSource Cts)> _monitoringTasks = new();

    public AgentWorkflowService(
        IOpenCodeServerManager serverManager,
        IOpenCodeClient client,
        IOpenCodeConfigGenerator configGenerator,
        PullRequestDataService pullRequestService,
        IDataStore dataStore,
        IGitWorktreeService worktreeService,
        IAgentCompletionMonitor completionMonitor,
        IGitHubService gitHubService,
        IBeadsService beadsService,
        IBeadsIssueTransitionService beadsTransitionService,
        ILogger<AgentWorkflowService> logger)
    {
        _serverManager = serverManager;
        _client = client;
        _configGenerator = configGenerator;
        _pullRequestService = pullRequestService;
        _dataStore = dataStore;
        _worktreeService = worktreeService;
        _completionMonitor = completionMonitor;
        _gitHubService = gitHubService;
        _beadsService = beadsService;
        _beadsTransitionService = beadsTransitionService;
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

        // Handle agent completion
        await TryHandleAgentCompletionForEntityAsync(entityId, ct);
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

    private void StartMonitoringForPrCreation(string projectId, string changeId, string serverBaseUrl, string branchName)
    {
        var cts = new CancellationTokenSource();
        _monitoringTasks[changeId] = (projectId, cts);

        _ = MonitorAgentForCompletionAsync(projectId, changeId, serverBaseUrl, branchName, cts.Token);
    }

    private async Task MonitorAgentForCompletionAsync(
        string projectId,
        string issueId,
        string serverBaseUrl,
        string branchName,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Starting SSE monitoring for issue {IssueId}", issueId);

            // Monitor SSE events until server stops (no timeout)
            var result = await _completionMonitor.MonitorForPrCreationAsync(
                serverBaseUrl, projectId, branchName, ct);
            
            if (result.Success && result.PrNumber.HasValue)
            {
                // PR detected via SSE - promote issue immediately
                _logger.LogInformation(
                    "PR #{PrNumber} detected via SSE for issue {IssueId}",
                    result.PrNumber.Value, issueId);
                
                await PromoteBeadsIssueWithPrAsync(projectId, issueId, result.PrNumber.Value);
            }
            else
            {
                _logger.LogInformation(
                    "SSE monitoring ended without PR detection for issue {IssueId}: {Error}",
                    issueId, result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SSE monitoring cancelled for issue {IssueId}", issueId);
        }
        catch (HttpRequestException ex)
        {
            // Server stopped - this is expected when the agent finishes
            _logger.LogInformation(
                "Agent server stopped for issue {IssueId} (HTTP error: {Message})",
                issueId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SSE monitoring for issue {IssueId}", issueId);
        }
        finally
        {
            _monitoringTasks.Remove(issueId);
        }
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

        // If we get here, it's either a PR (not a beads issue) or the entity wasn't found
        _logger.LogDebug("No beads issue found for entity {EntityId}", entityId);
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

    #region Beads Issue Methods
    
    public async Task<AgentStatus> StartAgentForBeadsIssueAsync(
        string projectId, 
        string issueId, 
        AgentMode agentMode = AgentMode.Building,
        string? model = null, 
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
                
                // Store metadata (group is now in hsp: label, not stored here)
                metadata = new BeadsIssueMetadata
                {
                    IssueId = issueId,
                    ProjectId = projectId,
                    BranchName = branchName,
                    WorktreePath = worktreePath
                };
                await _dataStore.AddBeadsIssueMetadataAsync(metadata);
            }
            
            // Check if server already running
            var existingServer = _serverManager.GetServerForEntity(issueId);
            if (existingServer != null && existingServer.Status == OpenCodeServerStatus.Running)
            {
                _logger.LogInformation("Server already running for issue {IssueId}", issueId);
                return await BuildAgentStatusAsync(existingServer, ct);
            }
            
            // Pull latest changes before starting agent
            _logger.LogInformation("Pulling latest changes for issue {IssueId}", issueId);
            var pullSuccess = await _worktreeService.PullLatestAsync(worktreePath!);
            if (!pullSuccess)
            {
                _logger.LogWarning("Failed to pull latest changes for issue {IssueId}, continuing anyway", issueId);
            }
            
            // Generate config
            var effectiveModel = model ?? project.DefaultModel;
            var config = _configGenerator.CreateDefaultConfig(effectiveModel);
            await _configGenerator.GenerateConfigAsync(worktreePath, config, ct);
            
            // Start server using issueId as the entity ID
            var server = await _serverManager.StartServerAsync(issueId, worktreePath, continueSession: false, ct);
            
            // Create session
            var session = await _client.CreateSessionAsync(server.BaseUrl, issue.Title, ct);
            server.ActiveSessionId = session.Id;
            
            // Update metadata with agent server ID
            metadata.ActiveAgentServerId = server.Id;
            metadata.AgentStartedAt = DateTime.UtcNow;
            await _dataStore.UpdateBeadsIssueMetadataAsync(metadata);
            
            // Send initial prompt with issue instructions
            var initialPrompt = BuildInitialPromptForBeadsIssue(issue, branchName, project.DefaultBranch, agentMode);
            await _client.SendPromptAsyncNoWait(
                server.BaseUrl, 
                session.Id, 
                PromptRequest.FromText(initialPrompt, model), 
                ct);
            
            _logger.LogInformation(
                "Sent initial prompt for issue {IssueId} to session {SessionId}",
                issueId, session.Id);
            
            // Start background monitoring for PR creation (fire and forget)
            StartMonitoringForPrCreation(projectId, issueId, server.BaseUrl, branchName);
            
            // Build and return status
            var status = new AgentStatus
            {
                EntityId = issueId,
                Server = server,
                ActiveSession = session,
                Sessions = [session]
            };
            
            _logger.LogInformation(
                "Agent started for issue {IssueId} on port {Port}, session {SessionId}. Server URL: {ServerUrl}",
                issueId, server.Port, session.Id, server.BaseUrl);
            
            return status;
        }
        catch (Exception ex)
        {
            // Handle agent failure - revert status to Open
            await _beadsTransitionService.HandleAgentFailureAsync(projectId, issueId, ex.Message);
            throw;
        }
    }
    
    public async Task<AgentStatus?> GetAgentStatusForBeadsIssueAsync(string issueId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(issueId);
        if (server == null || server.Status != OpenCodeServerStatus.Running)
        {
            return null;
        }
        
        return await BuildAgentStatusAsync(server, ct);
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
    
    internal static string BuildInitialPromptForBeadsIssue(BeadsIssue issue, string branchName, string baseBranch, AgentMode agentMode)
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
