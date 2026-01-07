using System.Text.Json.Serialization;

namespace Homespun.Features.Beads.Data;

/// <summary>
/// Represents a beads issue. Maps to the JSON output of 'bd show --json'.
/// </summary>
public class BeadsIssue
{
    /// <summary>
    /// Unique identifier for the issue (e.g., "bd-a3f8" or "bd-a3f8.1" for hierarchical).
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    
    /// <summary>
    /// Human-readable title of the issue.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; set; }
    
    /// <summary>
    /// Description/body of the issue. Used for agent implementation instructions.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    /// <summary>
    /// Current status of the issue.
    /// Note: The JsonStringEnumConverter with SnakeCaseLower is configured in BeadsService.JsonOptions
    /// to handle snake_case values like "in_progress" from the bd CLI.
    /// </summary>
    [JsonPropertyName("status")]
    public BeadsIssueStatus Status { get; set; } = BeadsIssueStatus.Open;
    
    /// <summary>
    /// Type of the issue.
    /// Note: The JsonStringEnumConverter with SnakeCaseLower is configured in BeadsService.JsonOptions
    /// to handle snake_case values like "feature" from the bd CLI.
    /// </summary>
    [JsonPropertyName("issue_type")]
    public BeadsIssueType Type { get; set; } = BeadsIssueType.Task;
    
    /// <summary>
    /// Priority level (0-4, where 0 is highest).
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }
    
    /// <summary>
    /// Labels attached to the issue.
    /// </summary>
    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];
    
    /// <summary>
    /// Parent issue ID for hierarchical issues (epics with subtasks).
    /// </summary>
    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }
    
    /// <summary>
    /// Assignee of the issue.
    /// </summary>
    [JsonPropertyName("assignee")]
    public string? Assignee { get; set; }
    
    /// <summary>
    /// When the issue was created.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When the issue was last updated.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// When the issue was closed (if applicable).
    /// </summary>
    [JsonPropertyName("closed_at")]
    public DateTime? ClosedAt { get; set; }
}
