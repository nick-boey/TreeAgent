namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Information about a single row's lane state for rendering.
/// </summary>
public record RowLaneInfo
{
    /// <summary>
    /// The lane index where the node in this row is positioned.
    /// </summary>
    public required int NodeLane { get; init; }

    /// <summary>
    /// Set of all lanes that have active lines passing through this row.
    /// </summary>
    public required IReadOnlySet<int> ActiveLanes { get; init; }

    /// <summary>
    /// If this node connects from a different lane (branch start/switch),
    /// this is the source lane index. Null if continuing in same lane.
    /// </summary>
    public int? ConnectorFromLane { get; init; }

    /// <summary>
    /// The node ID for this row.
    /// </summary>
    public required string NodeId { get; init; }
}

/// <summary>
/// Complete layout information for rendering the timeline graph.
/// </summary>
public record TimelineLaneLayout
{
    /// <summary>
    /// Map from node ID to assigned lane index.
    /// </summary>
    public required IReadOnlyDictionary<string, int> LaneAssignments { get; init; }

    /// <summary>
    /// Maximum number of lanes used (determines SVG width).
    /// </summary>
    public required int MaxLanes { get; init; }

    /// <summary>
    /// Per-row rendering information in display order.
    /// </summary>
    public required IReadOnlyList<RowLaneInfo> RowInfos { get; init; }
}
