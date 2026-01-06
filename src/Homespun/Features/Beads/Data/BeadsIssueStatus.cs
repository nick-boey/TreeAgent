namespace Homespun.Features.Beads.Data;

/// <summary>
/// Status of a beads issue. Maps to bd CLI statuses.
/// </summary>
public enum BeadsIssueStatus
{
    /// <summary>Ready to be worked on.</summary>
    Open,
    
    /// <summary>Currently being worked on.</summary>
    InProgress,
    
    /// <summary>Cannot proceed (waiting on dependencies).</summary>
    Blocked,
    
    /// <summary>Deliberately put on ice for later.</summary>
    Deferred,
    
    /// <summary>Work completed.</summary>
    Closed,
    
    /// <summary>Deleted issue (suppresses resurrections).</summary>
    Tombstone,
    
    /// <summary>Stays open indefinitely (used for hooks, anchors).</summary>
    Pinned
}
