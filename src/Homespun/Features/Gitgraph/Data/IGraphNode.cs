namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Common interface for items that can be rendered in a Git-graph visualization.
/// Provides properties needed to position and render nodes in the graph.
/// </summary>
public interface IGraphNode
{
    /// <summary>
    /// Unique identifier for the node. Used as commit hash in Gitgraph.
    /// Format: "pr-{Number}" for PRs, "issue-{Id}" for issues.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display title for the node (commit subject in Gitgraph).
    /// </summary>
    string Title { get; }

    /// <summary>
    /// The type of node for visual differentiation.
    /// </summary>
    GraphNodeType NodeType { get; }

    /// <summary>
    /// Current status for color coding and filtering.
    /// </summary>
    GraphNodeStatus Status { get; }

    /// <summary>
    /// Parent node IDs (dependencies/blocking items).
    /// In graph terms: commits this node merges from.
    /// </summary>
    IReadOnlyList<string> ParentIds { get; }

    /// <summary>
    /// Branch name this node belongs to (for grouping in visualization).
    /// </summary>
    string BranchName { get; }

    /// <summary>
    /// Sort key for ordering within the graph.
    /// </summary>
    DateTime SortDate { get; }

    /// <summary>
    /// Time dimension value for positioning.
    /// Negative = past (merged/closed), 0 = HEAD, positive = future (open PRs/issues).
    /// </summary>
    int TimeDimension { get; }

    /// <summary>
    /// URL to navigate to the item (GitHub PR URL or null for issues).
    /// </summary>
    string? Url { get; }

    /// <summary>
    /// Color for the node dot.
    /// </summary>
    string? Color { get; }

    /// <summary>
    /// Optional tag/label to display (e.g., PR status, issue type).
    /// </summary>
    string? Tag { get; }

    /// <summary>
    /// The original PR number (for PRs) or null for issues.
    /// </summary>
    int? PullRequestNumber { get; }

    /// <summary>
    /// The original issue ID (for issues) or null for PRs.
    /// </summary>
    string? IssueId { get; }
}
