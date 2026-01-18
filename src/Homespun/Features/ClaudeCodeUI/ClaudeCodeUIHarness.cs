using System.Runtime.CompilerServices;
using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.ClaudeCodeUI.Models;
using Homespun.Features.ClaudeCodeUI.Services;

namespace Homespun.Features.ClaudeCodeUI;

/// <summary>
/// Claude Code UI implementation of the agent harness abstraction.
/// </summary>
public class ClaudeCodeUIHarness : IAgentHarness
{
    private readonly ClaudeCodeUIServerManager _serverManager;
    private readonly IClaudeCodeUIClient _client;
    private readonly ILogger<ClaudeCodeUIHarness> _logger;

    public ClaudeCodeUIHarness(
        ClaudeCodeUIServerManager serverManager,
        IClaudeCodeUIClient client,
        ILogger<ClaudeCodeUIHarness> logger)
    {
        _serverManager = serverManager;
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HarnessType => "claudeui";

    /// <inheritdoc />
    public async Task<AgentInstance> StartAgentAsync(AgentStartOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting Claude Code UI agent for entity {EntityId} in {WorkingDirectory}",
            options.EntityId, options.WorkingDirectory);

        // Start server
        var server = await _serverManager.StartServerAsync(
            options.EntityId,
            options.WorkingDirectory,
            ct);

        _logger.LogInformation(
            "Claude Code UI server started for entity {EntityId} on port {Port}",
            options.EntityId, server.Port);

        // Send initial prompt if provided
        if (options.InitialPrompt != null)
        {
            var request = MapToPromptRequest(options.InitialPrompt, options.WorkingDirectory, null);
            var response = await _client.SendPromptAsync(server.BaseUrl, request, ct);
            server.ActiveSessionId = response.SessionId;
        }

        return MapToAgentInstance(server);
    }

    /// <inheritdoc />
    public async Task StopAgentAsync(string agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping Claude Code UI agent {AgentId}", agentId);
        await _serverManager.StopServerAsync(agentId, ct);
    }

    /// <inheritdoc />
    public async Task<AgentMessage> SendPromptAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        var request = MapToPromptRequest(prompt, server.WorkingDirectory, server.ActiveSessionId);
        var response = await _client.SendPromptAsync(server.BaseUrl, request, ct);

        // Update session ID if this is a new session
        if (!string.IsNullOrEmpty(response.SessionId))
        {
            server.ActiveSessionId = response.SessionId;
        }

        return new AgentMessage
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Role = "assistant",
            CreatedAt = DateTime.UtcNow,
            Parts =
            [
                new AgentMessagePart
                {
                    Type = "text",
                    Text = response.Text
                }
            ]
        };
    }

    /// <inheritdoc />
    public async Task SendPromptNoWaitAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        var request = MapToPromptRequest(prompt, server.WorkingDirectory, server.ActiveSessionId);
        await _client.SendPromptNoWaitAsync(server.BaseUrl, request, ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> SendPromptStreamingAsync(
        string agentId,
        AgentPrompt prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        var request = MapToPromptRequest(prompt, server.WorkingDirectory, server.ActiveSessionId);

        await foreach (var evt in _client.SendPromptStreamingAsync(server.BaseUrl, request, ct))
        {
            // Update session ID if this is a session created event
            if (evt.Type == ClaudeCodeUIEventTypes.SessionCreated && !string.IsNullOrEmpty(evt.SessionId))
            {
                server.ActiveSessionId = evt.SessionId;
            }

            yield return MapToAgentEvent(agentId, evt);
        }
    }

    /// <inheritdoc />
    public Task<AgentInstanceStatus?> GetAgentStatusAsync(string agentId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId);
        if (server == null)
            return Task.FromResult<AgentInstanceStatus?>(null);

        var status = MapStatus(server.Status);
        return Task.FromResult<AgentInstanceStatus?>(status);
    }

    /// <inheritdoc />
    public AgentInstance? GetAgentForEntity(string entityId)
    {
        var server = _serverManager.GetServerForEntity(entityId);
        if (server == null)
            return null;

        return MapToAgentInstance(server);
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentInstance> GetRunningAgents()
    {
        return _serverManager.GetRunningServers()
            .Select(MapToAgentInstance)
            .ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> SubscribeToEventsAsync(
        string agentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        await foreach (var evt in _client.SubscribeToEventsAsync(server.BaseUrl, server.ActiveSessionId, ct))
        {
            yield return MapToAgentEvent(agentId, evt);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(string agentId, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId);
        if (server == null)
            return false;

        return await _serverManager.IsHealthyAsync(server, ct);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _serverManager.Dispose();
        return ValueTask.CompletedTask;
    }

    #region Mapping Methods

    private static ClaudeCodeUIPromptRequest MapToPromptRequest(
        AgentPrompt prompt,
        string workingDirectory,
        string? sessionId)
    {
        return new ClaudeCodeUIPromptRequest
        {
            Message = prompt.Text,
            ProjectPath = workingDirectory,
            Provider = "claude",
            Model = MapModel(prompt.Model),
            Stream = true,
            SessionId = sessionId
        };
    }

    private static string? MapModel(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        // Use full Claude model identifiers
        if (model.Contains("opus", StringComparison.OrdinalIgnoreCase))
            return "claude-opus-4-20250514";
        if (model.Contains("haiku", StringComparison.OrdinalIgnoreCase))
            return "claude-haiku-3-5-20241022";
        return "claude-sonnet-4-20250514"; // Default
    }

    private AgentInstance MapToAgentInstance(ClaudeCodeUIServer server)
    {
        return new AgentInstance
        {
            AgentId = server.EntityId,
            EntityId = server.EntityId,
            HarnessType = HarnessType,
            Status = MapStatus(server.Status),
            WebViewUrl = server.WebViewUrl,
            ApiBaseUrl = server.BaseUrl,
            ActiveSessionId = server.ActiveSessionId,
            WorkingDirectory = server.WorkingDirectory,
            StartedAt = server.StartedAt,
            Metadata = new Dictionary<string, object>
            {
                ["port"] = server.Port,
                ["serverId"] = server.Id
            }
        };
    }

    private static AgentInstanceStatus MapStatus(ClaudeCodeUIServerStatus status)
    {
        return status switch
        {
            ClaudeCodeUIServerStatus.Starting => AgentInstanceStatus.Starting,
            ClaudeCodeUIServerStatus.Running => AgentInstanceStatus.Running,
            ClaudeCodeUIServerStatus.Stopped => AgentInstanceStatus.Stopped,
            ClaudeCodeUIServerStatus.Failed => AgentInstanceStatus.Failed,
            _ => AgentInstanceStatus.Failed
        };
    }

    private static AgentEvent MapToAgentEvent(string agentId, ClaudeCodeUIEvent evt)
    {
        return new AgentEvent
        {
            Type = MapEventType(evt.Type),
            AgentId = agentId,
            SessionId = evt.SessionId,
            Content = evt.Text,
            ToolName = evt.ToolName,
            Error = evt.Error,
            Properties = new Dictionary<string, object>
            {
                ["originalType"] = evt.Type
            }
        };
    }

    private static string MapEventType(string claudeUIType)
    {
        return claudeUIType switch
        {
            ClaudeCodeUIEventTypes.SessionCreated => AgentEventTypes.SessionCreated,
            ClaudeCodeUIEventTypes.ClaudeResponse => AgentEventTypes.MessageUpdated,
            ClaudeCodeUIEventTypes.ClaudeComplete => AgentEventTypes.StatusChanged,
            ClaudeCodeUIEventTypes.ClaudeError => AgentEventTypes.StatusChanged,
            _ => claudeUIType
        };
    }

    #endregion
}
