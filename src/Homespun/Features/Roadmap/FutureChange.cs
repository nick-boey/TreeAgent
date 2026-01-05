using System.Text.Json.Serialization;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Represents a planned future change in the roadmap.
/// The schema uses a flat list with parent references (DAG) instead of nested children.
/// The Id is the full branch name: {group}/{type}/{shortTitle}
/// </summary>
public class FutureChange
{
    /// <summary>
    /// The full branch name serving as the unique identifier.
    /// Format: {group}/{type}/{shortTitle}
    /// Example: "core/feature/add-auth"
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Short URL-safe identifier used in the branch name.
    /// Must be lowercase alphanumeric with hyphens only.
    /// Example: "add-auth"
    /// </summary>
    [JsonPropertyName("shortTitle")]
    public required string ShortTitle { get; set; }

    [JsonPropertyName("group")]
    public required string Group { get; set; }

    /// <summary>
    /// The type of change. Serialized to lowercase (e.g., "feature" not "Feature").
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(LowercaseEnumConverter<ChangeType>))]
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

    /// <summary>
    /// List of parent change IDs that this change depends on.
    /// Empty list means no dependencies (root level or depends on current PRs).
    /// Supports DAG structure with multiple parents.
    /// </summary>
    [JsonPropertyName("parents")]
    public List<string> Parents { get; set; } = [];

    /// <summary>
    /// Current status of the future change in the workflow.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(FutureChangeStatusConverter))]
    public FutureChangeStatus Status { get; set; } = FutureChangeStatus.Pending;

    /// <summary>
    /// The server ID of the active OpenCode agent working on this change.
    /// Null when no agent is active.
    /// </summary>
    [JsonPropertyName("activeAgentServerId")]
    public string? ActiveAgentServerId { get; set; }

    /// <summary>
    /// Path to the worktree for this change when in InProgress/AwaitingPR status.
    /// </summary>
    [JsonPropertyName("worktreePath")]
    public string? WorktreePath { get; set; }
}
