using System.Text.Json.Serialization;
using TreeAgent.Web.Features.PullRequests;

namespace TreeAgent.Web.Features.Roadmap;

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