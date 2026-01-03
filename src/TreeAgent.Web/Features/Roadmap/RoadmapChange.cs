using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.Roadmap;

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