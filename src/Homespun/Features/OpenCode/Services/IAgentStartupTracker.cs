namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Represents the state of an agent startup operation.
/// </summary>
public enum AgentStartupState
{
    /// <summary>
    /// Agent has not been started or state was cleared.
    /// </summary>
    NotStarted,
    
    /// <summary>
    /// Agent startup is in progress.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Agent has successfully started.
    /// </summary>
    Started,
    
    /// <summary>
    /// Agent startup failed.
    /// </summary>
    Failed
}

/// <summary>
/// Information about an agent's startup state.
/// </summary>
/// <param name="EntityId">The ID of the entity (issue or PR) the agent is for.</param>
/// <param name="State">The current startup state.</param>
/// <param name="ErrorMessage">Error message if the startup failed.</param>
public record AgentStartupInfo(
    string EntityId,
    AgentStartupState State,
    string? ErrorMessage = null);

/// <summary>
/// Tracks agent startup state across UI components, enabling non-blocking agent creation.
/// </summary>
public interface IAgentStartupTracker
{
    /// <summary>
    /// Event fired when an agent's startup state changes.
    /// </summary>
    event Action<AgentStartupInfo>? StateChanged;
    
    /// <summary>
    /// Gets the current startup state for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID (issue or PR ID).</param>
    /// <returns>The current startup info, or NotStarted if not tracked.</returns>
    AgentStartupInfo GetState(string entityId);
    
    /// <summary>
    /// Gets all currently tracked startup states.
    /// </summary>
    /// <returns>List of all tracked startup states.</returns>
    IReadOnlyList<AgentStartupInfo> GetAllStates();
    
    /// <summary>
    /// Marks an entity as starting agent startup.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    void MarkAsStarting(string entityId);
    
    /// <summary>
    /// Marks an entity's agent as successfully started.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    void MarkAsStarted(string entityId);
    
    /// <summary>
    /// Marks an entity's agent startup as failed.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="errorMessage">The error message.</param>
    void MarkAsFailed(string entityId, string errorMessage);
    
    /// <summary>
    /// Clears the startup state for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    void ClearState(string entityId);
    
    /// <summary>
    /// Checks if an entity is currently in the Starting state.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if the entity is starting.</returns>
    bool IsStarting(string entityId);
    
    /// <summary>
    /// Checks if an entity's startup has failed.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if the startup failed.</returns>
    bool HasFailed(string entityId);
}
