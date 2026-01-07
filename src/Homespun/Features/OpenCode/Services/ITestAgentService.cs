namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Service for running test agents to debug OpenCode integration.
/// Only available in DEBUG builds.
/// </summary>
public interface ITestAgentService
{
    /// <summary>
    /// Starts a test agent in a hsp/test worktree.
    /// Creates the worktree, starts an OpenCode server, and sends a simple test prompt.
    /// </summary>
    Task<TestAgentResult> StartTestAgentAsync(string projectId, CancellationToken ct = default);
    
    /// <summary>
    /// Stops the test agent and cleans up the worktree and branch.
    /// </summary>
    Task StopTestAgentAsync(string projectId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the current test agent status for a project.
    /// </summary>
    TestAgentStatus? GetTestAgentStatus(string projectId);
    
    /// <summary>
    /// Verifies the session is visible in the OpenCode session list.
    /// </summary>
    Task<SessionVerificationResult> VerifySessionVisibilityAsync(string projectId, CancellationToken ct = default);
}

/// <summary>
/// Result of starting a test agent.
/// </summary>
public class TestAgentResult
{
    public bool Success { get; init; }
    public string? ServerUrl { get; init; }
    public string? SessionId { get; init; }
    public string? WorktreePath { get; init; }
    public string? Error { get; init; }
    
    public static TestAgentResult Ok(string serverUrl, string sessionId, string worktreePath) 
        => new() { Success = true, ServerUrl = serverUrl, SessionId = sessionId, WorktreePath = worktreePath };
    
    public static TestAgentResult Fail(string error) 
        => new() { Success = false, Error = error };
}

/// <summary>
/// Status of a running test agent.
/// </summary>
public class TestAgentStatus
{
    public required string ProjectId { get; init; }
    public required string ServerUrl { get; init; }
    public required string SessionId { get; init; }
    public required string WorktreePath { get; init; }
    public DateTime StartedAt { get; init; }
}

/// <summary>
/// Result of verifying session visibility.
/// </summary>
public class SessionVerificationResult
{
    public bool SessionFound { get; init; }
    public int TotalSessions { get; init; }
    public string? SessionId { get; init; }
    public string? SessionTitle { get; init; }
    public List<string> AllSessionIds { get; init; } = [];
    public string? Error { get; init; }
}
