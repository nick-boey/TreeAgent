namespace Homespun.Features.Beads.Data;

/// <summary>
/// Local metadata for a beads issue that is not stored in beads itself.
/// This includes Homespun-specific data like worktree paths and agent state.
/// Stored in homespun-data.json.
/// </summary>
public class BeadsIssueMetadata
{
    /// <summary>
    /// The beads issue ID (e.g., "bd-a3f8").
    /// </summary>
    public required string IssueId { get; set; }
    
    /// <summary>
    /// The Homespun project ID this issue belongs to.
    /// </summary>
    public required string ProjectId { get; set; }
    
    /// <summary>
    /// The group for branch naming (e.g., "core", "frontend", "api").
    /// </summary>
    public required string Group { get; set; }
    
    /// <summary>
    /// Path to the git worktree for this issue when in development.
    /// </summary>
    public string? WorktreePath { get; set; }
    
    /// <summary>
    /// The branch name created for this issue.
    /// Format: {group}/{type}/{sanitized-title}+{beads-id}
    /// </summary>
    public string? BranchName { get; set; }
    
    /// <summary>
    /// The server ID of the active OpenCode agent working on this issue.
    /// Null when no agent is active.
    /// </summary>
    public string? ActiveAgentServerId { get; set; }
    
    /// <summary>
    /// When the agent was started for this issue.
    /// </summary>
    public DateTime? AgentStartedAt { get; set; }
    
    /// <summary>
    /// When this metadata was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this metadata was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
