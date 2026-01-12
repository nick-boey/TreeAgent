using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Adapts a PullRequestInfo to the IGraphNode interface for graph visualization.
/// </summary>
public class PullRequestNode : IGraphNode
{
    private readonly PullRequestInfo _pr;
    private readonly int _timeDimension;
    private readonly IReadOnlyList<string> _parentIds;

    public PullRequestNode(PullRequestInfo pr, int timeDimension, IReadOnlyList<string>? parentIds = null)
    {
        _pr = pr;
        _timeDimension = timeDimension;
        _parentIds = parentIds ?? [];
    }

    public string Id => $"pr-{_pr.Number}";

    public string Title => $"#{_pr.Number}: {_pr.Title}";

    public GraphNodeType NodeType => _pr.Status switch
    {
        PullRequestStatus.Merged => GraphNodeType.MergedPullRequest,
        PullRequestStatus.Closed => GraphNodeType.ClosedPullRequest,
        _ => GraphNodeType.OpenPullRequest
    };

    public GraphNodeStatus Status => _pr.Status switch
    {
        PullRequestStatus.Merged => GraphNodeStatus.Completed,
        PullRequestStatus.Closed => GraphNodeStatus.Abandoned,
        PullRequestStatus.InProgress => GraphNodeStatus.InProgress,
        PullRequestStatus.ReadyForReview => GraphNodeStatus.Pending,
        PullRequestStatus.ReadyForMerging => GraphNodeStatus.Pending,
        PullRequestStatus.ChecksFailing => GraphNodeStatus.Blocked,
        PullRequestStatus.Conflict => GraphNodeStatus.Blocked,
        _ => GraphNodeStatus.Open
    };

    public IReadOnlyList<string> ParentIds => _parentIds;

    public string BranchName => _pr.Status == PullRequestStatus.Merged
        ? "main"
        : _pr.BranchName ?? $"pr-{_pr.Number}";

    public DateTime SortDate => _pr.Status switch
    {
        PullRequestStatus.Merged => _pr.MergedAt ?? _pr.UpdatedAt,
        PullRequestStatus.Closed => _pr.ClosedAt ?? _pr.UpdatedAt,
        _ => _pr.CreatedAt
    };

    public int TimeDimension => _timeDimension;

    public string? Url => _pr.HtmlUrl;

    public string? Color => GetStatusColor(_pr.Status);

    public string? Tag => _pr.Status.ToString();

    public int? PullRequestNumber => _pr.Number;

    public string? IssueId => null;

    /// <summary>
    /// Original PullRequestInfo for access to additional properties.
    /// </summary>
    public PullRequestInfo PullRequest => _pr;

    private static string GetStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.Merged => "#a855f7",       // Purple
        PullRequestStatus.Closed => "#6b7280",       // Gray
        PullRequestStatus.InProgress => "#3b82f6",   // Blue
        PullRequestStatus.ReadyForReview => "#eab308", // Yellow
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green
        PullRequestStatus.ChecksFailing => "#ef4444",  // Red
        PullRequestStatus.Conflict => "#f97316",      // Orange
        _ => "#6b7280"                                // Gray
    };
}
