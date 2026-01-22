using Fleece.Core.Models;

namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Adapts a Fleece Issue to the IGraphNode interface for graph visualization.
/// </summary>
public class IssueNode : IGraphNode
{
    private readonly Issue _issue;
    private readonly IReadOnlyList<string> _parentIds;
    private readonly int _timeDimension;
    private readonly bool _isOrphan;
    private readonly string? _customBranchName;

    public IssueNode(
        Issue issue,
        IReadOnlyList<string> parentIds,
        int timeDimension,
        bool isOrphan = false,
        string? customBranchName = null)
    {
        _issue = issue;
        _parentIds = parentIds;
        _timeDimension = timeDimension;
        _isOrphan = isOrphan;
        _customBranchName = customBranchName;
    }

    public string Id => $"issue-{_issue.Id}";

    public string Title => _issue.Title;

    public GraphNodeType NodeType => _isOrphan
        ? GraphNodeType.OrphanIssue
        : GraphNodeType.Issue;

    public GraphNodeStatus Status => _issue.Status switch
    {
        IssueStatus.Closed => GraphNodeStatus.Completed,
        IssueStatus.Complete => GraphNodeStatus.Completed,
        IssueStatus.Archived => GraphNodeStatus.Abandoned,
        IssueStatus.Deleted => GraphNodeStatus.Abandoned,
        IssueStatus.Idea => GraphNodeStatus.Open,
        IssueStatus.Spec => GraphNodeStatus.Open,
        IssueStatus.Next => GraphNodeStatus.Open,
        IssueStatus.Progress => GraphNodeStatus.Open,
        IssueStatus.Review => GraphNodeStatus.Open,
        _ => GraphNodeStatus.Open
    };

    public IReadOnlyList<string> ParentIds => _parentIds;

    public string BranchName => _customBranchName ?? (_isOrphan
        ? "orphan-issues"
        : $"issue-{_issue.Id}");

    public DateTime SortDate => _issue.Status is IssueStatus.Closed or IssueStatus.Complete
        ? _issue.StatusLastUpdate.DateTime
        : _issue.CreatedAt.DateTime;

    public int TimeDimension => _timeDimension;

    public string? Url => null;

    public string? Color => GetTypeColor(_issue.Type);

    public string? Tag => _issue.Type.ToString();

    public int? PullRequestNumber => null;

    public string? IssueId => _issue.Id;

    /// <summary>
    /// Original Fleece Issue for access to additional properties.
    /// </summary>
    public Issue Issue => _issue;

    /// <summary>
    /// Priority level for sorting within the same time dimension.
    /// </summary>
    public int? Priority => _issue.Priority;

    /// <summary>
    /// Whether this is an orphan issue (no dependencies).
    /// </summary>
    public bool IsOrphan => _isOrphan;

    private static string GetTypeColor(IssueType type) => type switch
    {
        IssueType.Bug => "#ef4444",      // Red
        IssueType.Feature => "#a855f7",  // Purple
        IssueType.Task => "#3b82f6",     // Blue
        IssueType.Chore => "#6b7280",    // Gray
        _ => "#6b7280"                   // Gray
    };
}
