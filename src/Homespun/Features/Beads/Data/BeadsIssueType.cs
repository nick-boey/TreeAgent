namespace Homespun.Features.Beads.Data;

/// <summary>
/// Type of a beads issue. Maps to bd CLI issue types.
/// </summary>
public enum BeadsIssueType
{
    /// <summary>Something broken that needs fixing.</summary>
    Bug,
    
    /// <summary>New functionality.</summary>
    Feature,
    
    /// <summary>Work item (tests, docs, refactoring).</summary>
    Task,
    
    /// <summary>Large feature composed of multiple issues (supports hierarchical children).</summary>
    Epic,
    
    /// <summary>Maintenance work (dependencies, tooling).</summary>
    Chore
}
