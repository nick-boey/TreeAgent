namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Defines the operational mode for an agent session.
/// </summary>
public enum AgentMode
{
    /// <summary>
    /// Planning mode: Agent focuses on asking clarifying questions and creating implementation plans.
    /// </summary>
    Planning,
    
    /// <summary>
    /// Building mode: Agent implements changes, writes code, and creates pull requests.
    /// </summary>
    Building
}
