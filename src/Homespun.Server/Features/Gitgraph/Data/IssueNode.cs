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
        IssueStatus.Open => GraphNodeStatus.Open,
        IssueStatus.Progress => GraphNodeStatus.Open,
        IssueStatus.Review => GraphNodeStatus.Open,
        _ => GraphNodeStatus.Open
    };

    public IReadOnlyList<string> ParentIds => _parentIds;

    public string BranchName => _customBranchName ?? (_isOrphan
        ? "orphan-issues"
        : $"issue-{_issue.Id}");

    public DateTime SortDate => _issue.Status is IssueStatus.Closed or IssueStatus.Complete
        ? _issue.LastUpdate.DateTime
        : _issue.CreatedAt.DateTime;

    public int TimeDimension => _timeDimension;

    public string? Url => null;

    public string? Color => _prStatus.HasValue ? GetPrStatusColor(_prStatus.Value) : GetIssueColor(_issue.Type, _issue.Status);

    public string? Tag => _prStatus.HasValue ? GetPrStatusTag(_prStatus.Value) : _issue.Type.ToString();

    /// <summary>
    /// The PR status for this issue, if a linked PR exists.
    /// </summary>
    public PullRequestStatus? LinkedPrStatus => _prStatus;

    public int? PullRequestNumber => null;

    public string? IssueId => _issue.Id;

    /// <summary>
    /// Whether the issue has a non-empty description.
    /// </summary>
    public bool? HasDescription => !string.IsNullOrWhiteSpace(_issue.Description);

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

    /// <summary>
    /// Gets the base color for an issue based on its type.
    /// Bug type shows as red, all other types show as blue.
    /// Agent status may override this color at render time.
    /// </summary>
    private static string GetIssueColor(IssueType type, IssueStatus status)
    {
        // Bug type always shows as red
        if (type == IssueType.Bug) return "#ef4444"; // Red

        // All other types (Task, Feature, Chore) show as blue
        return "#3b82f6"; // Blue
    }

    private static string GetPrStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.ChecksFailing => "#ef4444",   // Red - attention needed
        PullRequestStatus.Conflict => "#f97316",        // Orange - conflicts
        PullRequestStatus.InProgress => "#3b82f6",      // Blue - work in progress
        PullRequestStatus.ReadyForReview => "#eab308",  // Yellow - awaiting review
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green - ready to merge
        PullRequestStatus.Merged => "#9ca3af",          // Light gray (Tailwind gray-400) - merged
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
