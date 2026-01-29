using Fleece.Core.Models;
using Homespun.Features.PullRequests;

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
    private readonly PullRequestStatus? _prStatus;

    public IssueNode(
        Issue issue,
        IReadOnlyList<string> parentIds,
        int timeDimension,
        bool isOrphan = false,
        string? customBranchName = null,
        PullRequestStatus? prStatus = null)
    {
        _issue = issue;
        _parentIds = parentIds;
        _timeDimension = timeDimension;
        _isOrphan = isOrphan;
        _customBranchName = customBranchName;
        _prStatus = prStatus;
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

    public string? Color => _prStatus.HasValue ? GetPrStatusColor(_prStatus.Value) : GetTypeColor(_issue.Type);

    public string? Tag => _prStatus.HasValue ? GetPrStatusTag(_prStatus.Value) : _issue.Type.ToString();

    /// <summary>
    /// The PR status for this issue, if a linked PR exists.
    /// </summary>
    public PullRequestStatus? LinkedPrStatus => _prStatus;

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

    private static string GetPrStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.ChecksFailing => "#ef4444",   // Red - attention needed
        PullRequestStatus.Conflict => "#f97316",        // Orange - conflicts
        PullRequestStatus.InProgress => "#3b82f6",      // Blue - work in progress
        PullRequestStatus.ReadyForReview => "#eab308",  // Yellow - awaiting review
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green - ready to merge
        PullRequestStatus.Merged => "#a855f7",          // Purple - merged
        PullRequestStatus.Closed => "#6b7280",          // Gray - closed
        _ => "#6b7280"                                  // Gray
    };

    private static string GetPrStatusTag(PullRequestStatus status) => status switch
    {
        PullRequestStatus.ChecksFailing => "Checks Failing",
        PullRequestStatus.Conflict => "Conflicts",
        PullRequestStatus.InProgress => "In Progress",
        PullRequestStatus.ReadyForReview => "Ready for Review",
        PullRequestStatus.ReadyForMerging => "Ready to Merge",
        PullRequestStatus.Merged => "Merged",
        PullRequestStatus.Closed => "Closed",
        _ => "PR"
    };
}
