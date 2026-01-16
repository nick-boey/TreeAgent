namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Represents the complete graph structure for visualization.
/// Contains ordered nodes with parent references, ready for Gitgraph.js rendering.
/// </summary>
public class Graph
{
    /// <summary>
    /// All nodes in the graph, ordered for rendering (chronological for timeline).
    /// </summary>
    public IReadOnlyList<IGraphNode> Nodes { get; }

    /// <summary>
    /// Branch definitions for the graph (name -> metadata).
    /// </summary>
    public IReadOnlyDictionary<string, GraphBranch> Branches { get; }

    /// <summary>
    /// The main/trunk branch name.
    /// </summary>
    public string MainBranchName { get; }

    /// <summary>
    /// Indicates whether there are more past PRs available to load.
    /// </summary>
    public bool HasMorePastPRs { get; }

    /// <summary>
    /// The number of past PRs currently shown in the graph.
    /// </summary>
    public int TotalPastPRsShown { get; }

    public Graph(
        IReadOnlyList<IGraphNode> nodes,
        IReadOnlyDictionary<string, GraphBranch> branches,
        string mainBranchName = "main",
        bool hasMorePastPRs = false,
        int totalPastPRsShown = 0)
    {
        Nodes = nodes;
        Branches = branches;
        MainBranchName = mainBranchName;
        HasMorePastPRs = hasMorePastPRs;
        TotalPastPRsShown = totalPastPRsShown;
    }
}
