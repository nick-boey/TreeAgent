using System.Text.Json.Serialization;

namespace TreeAgent.Web.Models;

/// <summary>
/// Represents the ROADMAP.json file structure containing future planned changes.
/// </summary>
public class Roadmap
{
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("changes")]
    public List<RoadmapChange> Changes { get; set; } = [];

    /// <summary>
    /// Gets all changes flattened with their calculated time values.
    /// </summary>
    public List<(RoadmapChange Change, int Time, int Depth)> GetAllChangesWithTime()
    {
        var result = new List<(RoadmapChange, int, int)>();
        CollectChanges(Changes, 0, result);
        return result;
    }

    private static void CollectChanges(
        List<RoadmapChange> changes,
        int depth,
        List<(RoadmapChange, int, int)> result)
    {
        foreach (var change in changes)
        {
            var time = PullRequestTimeCalculator.CalculateTimeForFutureChange(depth);
            result.Add((change, time, depth));

            if (change.Children.Count > 0)
            {
                CollectChanges(change.Children, depth + 1, result);
            }
        }
    }
}

/// <summary>
/// Represents a planned future change in the roadmap.
/// </summary>
public class RoadmapChange
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("group")]
    public required string Group { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ChangeType Type { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("priority")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Priority? Priority { get; set; }

    [JsonPropertyName("estimatedComplexity")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Complexity? EstimatedComplexity { get; set; }

    [JsonPropertyName("children")]
    public List<RoadmapChange> Children { get; set; } = [];

    /// <summary>
    /// Generates the branch name following the pattern: {group}/{type}/{id}
    /// </summary>
    public string GetBranchName()
    {
        return $"{Group}/{Type.ToString().ToLowerInvariant()}/{Id}";
    }
}

/// <summary>
/// Type of change.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    [JsonPropertyName("feature")]
    Feature,

    [JsonPropertyName("bug")]
    Bug,

    [JsonPropertyName("refactor")]
    Refactor,

    [JsonPropertyName("docs")]
    Docs,

    [JsonPropertyName("test")]
    Test,

    [JsonPropertyName("chore")]
    Chore
}

/// <summary>
/// Priority level for changes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Priority
{
    [JsonPropertyName("high")]
    High,

    [JsonPropertyName("medium")]
    Medium,

    [JsonPropertyName("low")]
    Low
}

/// <summary>
/// Estimated complexity for changes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Complexity
{
    [JsonPropertyName("small")]
    Small,

    [JsonPropertyName("medium")]
    Medium,

    [JsonPropertyName("large")]
    Large
}
