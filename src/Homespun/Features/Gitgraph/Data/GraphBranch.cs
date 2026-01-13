namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Branch metadata for graph rendering.
/// </summary>
public class GraphBranch
{
    /// <summary>
    /// Branch name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional color for the branch line.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Parent branch name (for branching visualization).
    /// </summary>
    public string? ParentBranch { get; init; }

    /// <summary>
    /// Commit ID where this branch diverges from parent.
    /// </summary>
    public string? ParentCommitId { get; init; }
}
