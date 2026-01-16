namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// Status of an agent instance.
/// </summary>
public enum AgentInstanceStatus
{
    /// <summary>
    /// Agent is starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Agent is running and ready to accept prompts.
    /// </summary>
    Running,

    /// <summary>
    /// Agent is in the process of stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Agent has stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Agent failed to start or encountered a fatal error.
    /// </summary>
    Failed
}
