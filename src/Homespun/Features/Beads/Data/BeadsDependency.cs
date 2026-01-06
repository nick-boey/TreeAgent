namespace Homespun.Features.Beads.Data;

/// <summary>
/// Represents a dependency relationship between beads issues.
/// </summary>
public class BeadsDependency
{
    /// <summary>
    /// The issue that depends on another (the "child" or "blocked" issue).
    /// </summary>
    public required string FromIssueId { get; set; }
    
    /// <summary>
    /// The issue being depended upon (the "parent" or "blocking" issue).
    /// </summary>
    public required string ToIssueId { get; set; }
    
    /// <summary>
    /// The type of dependency relationship.
    /// </summary>
    public BeadsDependencyType Type { get; set; } = BeadsDependencyType.Blocks;
}
