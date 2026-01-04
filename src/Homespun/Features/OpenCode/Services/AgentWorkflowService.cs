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
    private readonly IRoadmapService _roadmapService;
    private readonly ILogger<AgentWorkflowService> _logger;

    public AgentWorkflowService(
        IOpenCodeServerManager serverManager,
        IOpenCodeClient client,
        IOpenCodeConfigGenerator configGenerator,
        PullRequestDataService pullRequestService,
        IRoadmapService roadmapService,
        ILogger<AgentWorkflowService> logger)
    {
        _serverManager = serverManager;
        _client = client;
        _configGenerator = configGenerator;
        _pullRequestService = pullRequestService;
        _roadmapService = roadmapService;
        _logger = logger;
    }

    public async Task<AgentStatus> StartAgentForPullRequestAsync(
        string pullRequestId, 
        string? model = null, 
        CancellationToken ct = default)
    {
        var pullRequest = await _pullRequestService.GetByIdAsync(pullRequestId)
            ?? throw new InvalidOperationException($"Pull request {pullRequestId} not found");

        if (string.IsNullOrEmpty(pullRequest.WorktreePath))
        {
            throw new InvalidOperationException(
                $"Pull request {pullRequestId} does not have a worktree. Start development first.");
        }

        // Check if server already running
        var existingServer = _serverManager.GetServerForPullRequest(pullRequestId);
        if (existingServer != null && existingServer.Status == OpenCodeServerStatus.Running)
        {
            _logger.LogInformation("Server already running for PR {PullRequestId}", pullRequestId);
            return await BuildAgentStatusAsync(existingServer, ct);
        }

        // Generate config with model from: parameter -> project -> global default
        var effectiveModel = model ?? pullRequest.Project.DefaultModel;
        var config = _configGenerator.CreateDefaultConfig(effectiveModel);
        await _configGenerator.GenerateConfigAsync(pullRequest.WorktreePath, config, ct);

        // Start server
        var server = await _serverManager.StartServerAsync(pullRequestId, pullRequest.WorktreePath, ct);

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

        // Promote to pull request (creates branch and worktree)
        var pullRequest = await _roadmapService.PromoteChangeAsync(projectId, changeId)
            ?? throw new InvalidOperationException($"Failed to promote change {changeId} to pull request");

        // Start agent
        var status = await StartAgentForPullRequestAsync(pullRequest.Id, model, ct);

        // Send initial prompt with change instructions
        if (status.ActiveSession != null)
        {
            var initialPrompt = BuildInitialPrompt(change);
            await _client.SendPromptAsyncNoWait(
                status.Server.BaseUrl, 
                status.ActiveSession.Id, 
                PromptRequest.FromText(initialPrompt, model), 
                ct);

            _logger.LogInformation(
                "Sent initial prompt for change {ChangeId} to session {SessionId}",
                changeId, status.ActiveSession.Id);
        }

        return status;
    }

    public async Task StopAgentAsync(string pullRequestId, CancellationToken ct = default)
    {
        await _serverManager.StopServerAsync(pullRequestId, ct);
        _logger.LogInformation("Agent stopped for PR {PullRequestId}", pullRequestId);
    }

    public async Task<AgentStatus?> GetAgentStatusAsync(string pullRequestId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForPullRequest(pullRequestId);
        if (server == null || server.Status != OpenCodeServerStatus.Running)
        {
            return null;
        }

        return await BuildAgentStatusAsync(server, ct);
    }

    public async Task<OpenCodeMessage> SendPromptAsync(string pullRequestId, string prompt, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForPullRequest(pullRequestId)
            ?? throw new InvalidOperationException($"No agent running for PR {pullRequestId}");

        if (string.IsNullOrEmpty(server.ActiveSessionId))
        {
            throw new InvalidOperationException($"No active session for PR {pullRequestId}");
        }

        var request = PromptRequest.FromText(prompt);
        return await _client.SendPromptAsync(server.BaseUrl, server.ActiveSessionId, request, ct);
    }

    private async Task<AgentStatus> BuildAgentStatusAsync(OpenCodeServer server, CancellationToken ct)
    {
        var sessions = await _client.ListSessionsAsync(server.BaseUrl, ct);
        var activeSession = sessions.FirstOrDefault(s => s.Id == server.ActiveSessionId);

        return new AgentStatus
        {
            PullRequestId = server.PullRequestId,
            Server = server,
            ActiveSession = activeSession,
            Sessions = sessions
        };
    }

    private static string BuildInitialPrompt(RoadmapChange change)
    {
        var prompt = $"""
            Please implement the following change:

            **Title:** {change.Title}
            """;

        if (!string.IsNullOrEmpty(change.Description))
        {
            prompt += $"\n\n**Description:** {change.Description}";
        }

        if (!string.IsNullOrEmpty(change.Instructions))
        {
            prompt += $"\n\n**Instructions:**\n{change.Instructions}";
        }

        return prompt;
    }
}
