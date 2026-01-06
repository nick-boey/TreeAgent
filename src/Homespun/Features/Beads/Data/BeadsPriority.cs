namespace Homespun.Features.Beads.Data;

/// <summary>
/// Priority levels for beads issues. Maps to bd CLI priorities (0-4).
/// </summary>
public enum BeadsPriority
{
    /// <summary>Critical (security, data loss, broken builds).</summary>
    P0 = 0,
    
    /// <summary>High (major features, important bugs).</summary>
    P1 = 1,
    
    /// <summary>Medium (nice-to-have features, minor bugs).</summary>
    P2 = 2,
    
    /// <summary>Low (polish, optimization).</summary>
    P3 = 3,
    
    /// <summary>Backlog (future ideas).</summary>
    P4 = 4
}
