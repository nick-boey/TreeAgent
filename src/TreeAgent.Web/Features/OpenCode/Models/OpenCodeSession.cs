using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.OpenCode.Models;

/// <summary>
/// Represents a session from the OpenCode server API.
/// </summary>
public class OpenCodeSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("parentID")]
    public string? ParentId { get; set; }

    [JsonPropertyName("share")]
    public string? ShareUrl { get; set; }
}

/// <summary>
/// Represents the status of a session.
/// </summary>
public class OpenCodeSessionStatus
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}
