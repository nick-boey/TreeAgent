namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Type of node in the Git-graph visualization.
/// </summary>
public enum GraphNodeType
{
    /// <summary>A merged PR (appears on main branch timeline).</summary>
    MergedPullRequest,

    /// <summary>A closed (not merged) PR.</summary>
    ClosedPullRequest,

    /// <summary>An open PR (branches off main).</summary>
    OpenPullRequest,

    /// <summary>A beads issue with dependencies.</summary>
    Issue,

    /// <summary>An orphan issue with no dependencies.</summary>
    OrphanIssue
}

/// <summary>
/// Normalized status for graph nodes, mapped from PR/Issue statuses.
/// </summary>
public enum GraphNodeStatus
{
    /// <summary>Work completed and integrated (merged PR, closed issue).</summary>
    Completed,

    /// <summary>Actively being worked on.</summary>
    InProgress,

    /// <summary>Ready for review/action.</summary>
    Pending,

    /// <summary>Has problems (failing checks, conflicts, blocked).</summary>
    Blocked,

    /// <summary>Canceled/closed without completion.</summary>
    Abandoned,

    /// <summary>Not started yet (open issue).</summary>
    Open
}
