namespace Homespun.Features.Beads.Data;

/// <summary>
/// Type of dependency relationship between beads issues.
/// </summary>
public enum BeadsDependencyType
{
    /// <summary>Hard dependency (issue X blocks issue Y). Affects the ready work queue.</summary>
    Blocks,
    
    /// <summary>Soft relationship (issues are connected).</summary>
    Related,
    
    /// <summary>Epic/subtask relationship.</summary>
    ParentChild,
    
    /// <summary>Track issues discovered during work.</summary>
    DiscoveredFrom
}
