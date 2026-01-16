using Homespun.Features.Agents.Abstractions.Models;

namespace Homespun.Features.Agents.Abstractions;

/// <summary>
/// Core abstraction for AI agent harnesses (OpenCode, Claude Code UI, etc.).
/// Provides a unified interface for managing agent lifecycle and communication.
/// </summary>
public interface IAgentHarness : IAsyncDisposable
{
    /// <summary>
    /// Unique identifier for this harness type (e.g., "opencode", "claudeui").
    /// </summary>
    string HarnessType { get; }

    /// <summary>
    /// Starts an agent for a given context.
    /// </summary>
    /// <param name="options">Options for starting the agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The started agent instance.</returns>
    Task<AgentInstance> StartAgentAsync(AgentStartOptions options, CancellationToken ct = default);

    /// <summary>
    /// Stops a running agent.
    /// </summary>
    /// <param name="agentId">The agent ID to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopAgentAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt/message to an agent and waits for the response.
    /// </summary>
    /// <param name="agentId">The agent ID to send to.</param>
    /// <param name="prompt">The prompt to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent's response message.</returns>
    Task<AgentMessage> SendPromptAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt without waiting for the response.
    /// </summary>
    /// <param name="agentId">The agent ID to send to.</param>
    /// <param name="prompt">The prompt to send.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendPromptNoWaitAsync(string agentId, AgentPrompt prompt, CancellationToken ct = default);

    /// <summary>
    /// Gets the current status of an agent.
    /// </summary>
    /// <param name="agentId">The agent ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent status, or null if not found.</returns>
    Task<AgentInstanceStatus?> GetAgentStatusAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Gets the agent instance for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID (PR ID, issue ID, etc.).</param>
    /// <returns>The agent instance, or null if not running.</returns>
    AgentInstance? GetAgentForEntity(string entityId);

    /// <summary>
    /// Gets all running agents managed by this harness.
    /// </summary>
    /// <returns>List of running agent instances.</returns>
    IReadOnlyList<AgentInstance> GetRunningAgents();

    /// <summary>
    /// Subscribes to real-time events from an agent.
    /// </summary>
    /// <param name="agentId">The agent ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of agent events.</returns>
    IAsyncEnumerable<AgentEvent> SubscribeToEventsAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Checks if an agent is healthy and operational.
    /// </summary>
    /// <param name="agentId">The agent ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if healthy, false otherwise.</returns>
    Task<bool> IsHealthyAsync(string agentId, CancellationToken ct = default);
}
