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

    public Graph(
        IReadOnlyList<IGraphNode> nodes,
        IReadOnlyDictionary<string, GraphBranch> branches,
        string mainBranchName = "main")
    {
        Nodes = nodes;
        Branches = branches;
        MainBranchName = mainBranchName;
    }
}
