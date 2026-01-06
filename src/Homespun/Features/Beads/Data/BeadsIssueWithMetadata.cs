namespace Homespun.Features.Beads.Data;

/// <summary>
/// Combines a beads issue with its local Homespun metadata.
/// </summary>
public class BeadsIssueWithMetadata
{
    /// <summary>
    /// The beads issue data from the bd CLI.
    /// </summary>
    public required BeadsIssue Issue { get; set; }
    
    /// <summary>
    /// Local Homespun metadata for this issue.
    /// May be null if no metadata has been stored yet.
    /// </summary>
    public BeadsIssueMetadata? Metadata { get; set; }
    
    /// <summary>
    /// Calculated time value for graph positioning.
    /// Similar to the previous FutureChangeWithTime.
    /// </summary>
    public int Time { get; set; }
    
    /// <summary>
    /// Depth in the dependency tree (0 = no blockers).
    /// </summary>
    public int Depth { get; set; }
}
