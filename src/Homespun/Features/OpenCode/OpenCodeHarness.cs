using System.Runtime.CompilerServices;
using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenCode;

/// <summary>
/// OpenCode implementation of the agent harness abstraction.
/// Wraps the existing OpenCode server manager, client, and config generator.
/// </summary>
public class OpenCodeHarness : IAgentHarness
{
    private readonly IOpenCodeServerManager _serverManager;
    private readonly IOpenCodeClient _client;
    private readonly IOpenCodeConfigGenerator _configGenerator;
    private readonly ILogger<OpenCodeHarness> _logger;

    public OpenCodeHarness(
        IOpenCodeServerManager serverManager,
        IOpenCodeClient client,
        IOpenCodeConfigGenerator configGenerator,
        ILogger<OpenCodeHarness> logger)
    {
        _serverManager = serverManager;
        _client = client;
        _configGenerator = configGenerator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HarnessType => "opencode";

    /// <inheritdoc />
    public async Task<AgentInstance> StartAgentAsync(AgentStartOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting OpenCode agent for entity {EntityId} in {WorkingDirectory}",
            options.EntityId, options.WorkingDirectory);

        // Generate config
        var config = _configGenerator.CreateDefaultConfig(options.Model);
        await _configGenerator.GenerateConfigAsync(options.WorkingDirectory, config, ct);

        // Start server (entityId == agentId for OpenCode)
        var server = await _serverManager.StartServerAsync(
            options.EntityId,
            options.WorkingDirectory,
            options.ContinueSession,
            ct);

        // Create session
        var session = await _client.CreateSessionAsync(server.BaseUrl, options.SessionTitle, ct);
        server.ActiveSessionId = session.Id;

        _logger.LogInformation(
            "OpenCode agent started for entity {EntityId}, session {SessionId}, port {Port}",
            options.EntityId, session.Id, server.Port);

        // Send initial prompt if provided
        if (options.InitialPrompt != null)
        {
            var request = MapToPromptRequest(options.InitialPrompt);
            await _client.SendPromptAsyncNoWait(server.BaseUrl, session.Id, request, ct);
        }

        return MapToAgentInstance(server, session);
    }

    /// <inheritdoc />
    public async Task StopAgentAsync(string agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping OpenCode agent {AgentId}", agentId);
        await _serverManager.StopServerAsync(agentId, ct);
    }

    /// <inheritdoc />
    public async Task<AgentMessage> SendPromptAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        if (server.ActiveSessionId == null)
            throw new InvalidOperationException($"Agent {agentId} has no active session");

        var request = MapToPromptRequest(prompt);
        var response = await _client.SendPromptAsync(server.BaseUrl, server.ActiveSessionId, request, ct);

        return MapToAgentMessage(agentId, response);
    }

    /// <inheritdoc />
    public async Task SendPromptNoWaitAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        if (server.ActiveSessionId == null)
            throw new InvalidOperationException($"Agent {agentId} has no active session");

        var request = MapToPromptRequest(prompt);
        await _client.SendPromptAsyncNoWait(server.BaseUrl, server.ActiveSessionId, request, ct);
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

        return MapToAgentInstance(server, null);
    }

    /// <inheritdoc />
    public IReadOnlyList<AgentInstance> GetRunningAgents()
    {
        return _serverManager.GetRunningServers()
            .Select(s => MapToAgentInstance(s, null))
            .ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> SubscribeToEventsAsync(
        string agentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var server = _serverManager.GetServerForEntity(agentId)
            ?? throw new InvalidOperationException($"No agent running for entity {agentId}");

        await foreach (var evt in _client.SubscribeToEventsAsync(server.BaseUrl, ct))
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
        // Server manager cleanup is handled by its own IDisposable implementation
        return ValueTask.CompletedTask;
    }

    #region Mapping Methods

    private static PromptRequest MapToPromptRequest(AgentPrompt prompt)
    {
        var parts = new List<PromptPart>
        {
            new() { Type = "text", Text = prompt.Text }
        };

        // Add file paths if any
        foreach (var path in prompt.FilePaths)
        {
            parts.Add(new PromptPart { Type = "file", Path = path });
        }

        var request = new PromptRequest
        {
            Parts = parts,
            System = prompt.SystemPrompt
        };

        if (prompt.Model != null)
        {
            var modelParts = prompt.Model.Split('/');
            if (modelParts.Length == 2)
            {
                request.Model = new PromptModel
                {
                    ProviderId = modelParts[0],
                    ModelId = modelParts[1]
                };
            }
        }

        return request;
    }

    private AgentInstance MapToAgentInstance(OpenCodeServer server, OpenCodeSession? session)
    {
        return new AgentInstance
        {
            AgentId = server.EntityId, // For OpenCode, agentId == entityId
            EntityId = server.EntityId,
            HarnessType = HarnessType,
            Status = MapStatus(server.Status),
            WebViewUrl = server.WebViewUrl,
            ApiBaseUrl = server.BaseUrl,
            ActiveSessionId = session?.Id ?? server.ActiveSessionId,
            WorkingDirectory = server.WorktreePath,
            StartedAt = server.StartedAt,
            Metadata = new Dictionary<string, object>
            {
                ["port"] = server.Port,
                ["serverId"] = server.Id,
                ["continueSession"] = server.ContinueSession
            }
        };
    }

    private static AgentInstanceStatus MapStatus(OpenCodeServerStatus status)
    {
        return status switch
        {
            OpenCodeServerStatus.Starting => AgentInstanceStatus.Starting,
            OpenCodeServerStatus.Running => AgentInstanceStatus.Running,
            OpenCodeServerStatus.Stopped => AgentInstanceStatus.Stopped,
            OpenCodeServerStatus.Failed => AgentInstanceStatus.Failed,
            _ => AgentInstanceStatus.Failed
        };
    }

    private static AgentMessage MapToAgentMessage(string agentId, OpenCodeMessage message)
    {
        return new AgentMessage
        {
            Id = message.Info.Id,
            AgentId = agentId,
            Role = message.Info.Role,
            CreatedAt = message.Info.CreatedAt,
            Parts = message.Parts.Select(MapToAgentMessagePart).ToList()
        };
    }

    private static AgentMessagePart MapToAgentMessagePart(OpenCodeMessagePart part)
    {
        return new AgentMessagePart
        {
            Type = part.Type,
            Text = part.Text,
            ToolName = part.ToolUseName ?? part.Tool,
            ToolUseId = part.ToolUseId ?? part.CallId,
            ToolInput = part.Input ?? part.State?.Input,
            ToolOutput = part.Output ?? part.State?.Output,
            ToolState = part.State != null ? new ToolExecutionState
            {
                Status = part.State.Status,
                Error = part.State.Error,
                Title = part.State.Title,
                StartTime = part.State.Time?.Start,
                EndTime = part.State.Time?.End
            } : null
        };
    }

    private static AgentEvent MapToAgentEvent(string agentId, OpenCodeEvent evt)
    {
        return new AgentEvent
        {
            Type = MapEventType(evt.Type),
            AgentId = agentId,
            SessionId = evt.Properties?.SessionId,
            MessageId = evt.Properties?.MessageId,
            Content = evt.Properties?.Content,
            ToolName = evt.Properties?.ToolName,
            Status = evt.Properties?.StatusValue,
            Error = evt.Properties?.Error,
            Properties = new Dictionary<string, object>
            {
                ["originalType"] = evt.Type,
                ["partId"] = evt.Properties?.PartId ?? ""
            }
        };
    }

    private static string MapEventType(string openCodeType)
    {
        return openCodeType switch
        {
            OpenCodeEventTypes.ServerConnected => AgentEventTypes.Connected,
            OpenCodeEventTypes.SessionCreated => AgentEventTypes.SessionCreated,
            OpenCodeEventTypes.SessionUpdated => AgentEventTypes.SessionUpdated,
            OpenCodeEventTypes.MessageCreated => AgentEventTypes.MessageCreated,
            OpenCodeEventTypes.MessageUpdated => AgentEventTypes.MessageUpdated,
            OpenCodeEventTypes.ToolStart => AgentEventTypes.ToolStarted,
            OpenCodeEventTypes.ToolComplete => AgentEventTypes.ToolCompleted,
            _ => openCodeType // Pass through unknown types
        };
    }

    #endregion
}
