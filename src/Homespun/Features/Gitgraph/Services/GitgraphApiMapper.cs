using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.Gitgraph.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Converts a Graph to JSON format for the Gitgraph.js visualization.
/// </summary>
public class GitgraphApiMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Converts a Graph to JSON data for the Gitgraph.js visualization.
    /// </summary>
    public GitgraphJsonData ToJson(Graph graph)
    {
        var branches = graph.Branches.Values
            .Select(b => new GitgraphBranchData
            {
                Name = b.Name,
                Color = b.Color,
                ParentBranch = b.ParentBranch,
                ParentCommitId = b.ParentCommitId
            })
            .ToList();

        var commits = graph.Nodes
            .Select(n => new GitgraphCommitData
            {
                Hash = n.Id,
                Subject = n.Title,
                Branch = n.BranchName,
                ParentIds = n.ParentIds.ToList(),
                Color = n.Color,
                Tag = n.Tag,
                NodeType = n.NodeType.ToString(),
                Status = n.Status.ToString(),
                Url = n.Url,
                TimeDimension = n.TimeDimension,
                PullRequestNumber = n.PullRequestNumber,
                IssueId = n.IssueId
            })
            .ToList();

        return new GitgraphJsonData
        {
            MainBranchName = graph.MainBranchName,
            Branches = branches,
            Commits = commits
        };
    }

    /// <summary>
    /// Serializes the graph data to a JSON string.
    /// </summary>
    public string ToJsonString(Graph graph)
    {
        var data = ToJson(graph);
        return JsonSerializer.Serialize(data, JsonOptions);
    }
}

/// <summary>
/// Root JSON data structure for the Gitgraph.js visualization.
/// </summary>
public class GitgraphJsonData
{
    public string MainBranchName { get; set; } = "main";
    public List<GitgraphBranchData> Branches { get; set; } = [];
    public List<GitgraphCommitData> Commits { get; set; } = [];
}

/// <summary>
/// Branch data for JSON serialization.
/// </summary>
public class GitgraphBranchData
{
    public required string Name { get; set; }
    public string? Color { get; set; }
    public string? ParentBranch { get; set; }
    public string? ParentCommitId { get; set; }
}

/// <summary>
/// Commit/node data for JSON serialization.
/// </summary>
public class GitgraphCommitData
{
    public required string Hash { get; set; }
    public required string Subject { get; set; }
    public required string Branch { get; set; }
    public List<string> ParentIds { get; set; } = [];
    public string? Color { get; set; }
    public string? Tag { get; set; }
    public required string NodeType { get; set; }
    public required string Status { get; set; }
    public string? Url { get; set; }
    public int TimeDimension { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? IssueId { get; set; }
}
