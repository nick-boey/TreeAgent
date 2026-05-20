using Homespun.Shared.Models.Gitgraph;
using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Shared.Models.Fleece;

public class TaskGraphResponse
{
    public List<TaskGraphNodeResponse> Nodes { get; set; } = [];
    public int TotalLanes { get; set; }
    public List<TaskGraphEdgeResponse> Edges { get; set; } = [];
    public int TotalRows { get; set; }

    /// <summary>
    /// Merged/closed PRs to display at the top of the task graph.
    /// </summary>
    public List<TaskGraphPrResponse> MergedPrs { get; set; } = [];

    /// <summary>
    /// Whether there are more past PRs available to load.
    /// </summary>
    public bool HasMorePastPrs { get; set; }

    /// <summary>
    /// Total number of past PRs currently shown.
    /// </summary>
    public int TotalPastPrsShown { get; set; }

    /// <summary>
    /// Agent status data keyed by issue ID.
    /// </summary>
    public Dictionary<string, AgentStatusData> AgentStatuses { get; set; } = new();

    /// <summary>
    /// Linked PR information keyed by issue ID.
    /// </summary>
    public Dictionary<string, LinkedPr> LinkedPrs { get; set; } = new();

    /// <summary>
    /// OpenSpec state projection keyed by issue ID. Populated for issues whose branch is
    /// present on disk; omitted otherwise.
    /// </summary>
    public Dictionary<string, IssueOpenSpecState> OpenSpecStates { get; set; } = new();

}

public class TaskGraphNodeResponse
{
    public IssueResponse Issue { get; set; } = new();
    public int Lane { get; set; }
    public int Row { get; set; }
    public bool IsActionable { get; set; }
    public int AppearanceIndex { get; set; }
    public int TotalAppearances { get; set; } = 1;
}

/// <summary>
/// Represents a merged/closed PR in the task graph.
/// </summary>
public class TaskGraphPrResponse
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public bool IsMerged { get; set; }
    public bool HasDescription { get; set; }
    public AgentStatusData? AgentStatus { get; set; }
}

/// <summary>
/// Represents a semantic edge between two issues in the task graph layout.
/// Carries the geometry needed for the frontend to draw connectors without re-deriving them.
/// </summary>
public class TaskGraphEdgeResponse
{
    public required string From { get; set; }
    public required string To { get; set; }
    public required string Kind { get; set; }
    public required int StartRow { get; set; }
    public required int StartLane { get; set; }
    public required int EndRow { get; set; }
    public required int EndLane { get; set; }
    public int? PivotLane { get; set; }
    public required string SourceAttach { get; set; }
    public required string TargetAttach { get; set; }
}
