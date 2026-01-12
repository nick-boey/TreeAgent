using Homespun.Features.Beads.Data;

namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Adapts a BeadsIssue to the IGraphNode interface for graph visualization.
/// </summary>
public class BeadsIssueNode : IGraphNode
{
    private readonly BeadsIssue _issue;
    private readonly IReadOnlyList<string> _parentIds;
    private readonly int _timeDimension;
    private readonly bool _isOrphan;

    public BeadsIssueNode(
        BeadsIssue issue,
        IReadOnlyList<string> parentIds,
        int timeDimension,
        bool isOrphan = false)
    {
        _issue = issue;
        _parentIds = parentIds;
        _timeDimension = timeDimension;
        _isOrphan = isOrphan;
    }

    public string Id => $"issue-{_issue.Id}";

    public string Title => _issue.Title;

    public GraphNodeType NodeType => _isOrphan
        ? GraphNodeType.OrphanIssue
        : GraphNodeType.Issue;

    public GraphNodeStatus Status => _issue.Status switch
    {
        BeadsIssueStatus.Closed => GraphNodeStatus.Completed,
        BeadsIssueStatus.InProgress => GraphNodeStatus.InProgress,
        BeadsIssueStatus.Blocked => GraphNodeStatus.Blocked,
        BeadsIssueStatus.Deferred => GraphNodeStatus.Abandoned,
        BeadsIssueStatus.Tombstone => GraphNodeStatus.Abandoned,
        BeadsIssueStatus.Open => GraphNodeStatus.Open,
        BeadsIssueStatus.Pinned => GraphNodeStatus.Pending,
        _ => GraphNodeStatus.Open
    };

    public IReadOnlyList<string> ParentIds => _parentIds;

    public string BranchName => _isOrphan
        ? "orphan-issues"
        : $"issue-{_issue.Id}";

    public DateTime SortDate => _issue.Status == BeadsIssueStatus.Closed
        ? _issue.ClosedAt ?? _issue.UpdatedAt
        : _issue.CreatedAt;

    public int TimeDimension => _timeDimension;

    public string? Url => null;

    public string? Color => GetTypeColor(_issue.Type);

    public string? Tag => _issue.Type.ToString();

    public int? PullRequestNumber => null;

    public string? IssueId => _issue.Id;

    /// <summary>
    /// Original BeadsIssue for access to additional properties.
    /// </summary>
    public BeadsIssue Issue => _issue;

    /// <summary>
    /// Priority level for sorting within the same time dimension.
    /// </summary>
    public int? Priority => _issue.Priority;

    /// <summary>
    /// Whether this is an orphan issue (no dependencies).
    /// </summary>
    public bool IsOrphan => _isOrphan;

    private static string GetTypeColor(BeadsIssueType type) => type switch
    {
        BeadsIssueType.Bug => "#ef4444",      // Red
        BeadsIssueType.Feature => "#a855f7",  // Purple
        BeadsIssueType.Task => "#3b82f6",     // Blue
        BeadsIssueType.Epic => "#f97316",     // Orange
        BeadsIssueType.Chore => "#6b7280",    // Gray
        _ => "#6b7280"                        // Gray
    };
}
