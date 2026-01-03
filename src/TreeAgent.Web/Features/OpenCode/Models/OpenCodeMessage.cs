using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.OpenCode.Models;

/// <summary>
/// Represents a message from the OpenCode server API.
/// </summary>
public class OpenCodeMessage
{
    [JsonPropertyName("info")]
    public OpenCodeMessageInfo Info { get; set; } = new();

    [JsonPropertyName("parts")]
    public List<OpenCodeMessagePart> Parts { get; set; } = [];
}

public class OpenCodeMessageInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionID")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class OpenCodeMessagePart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("toolUseID")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("toolUseName")]
    public string? ToolUseName { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("output")]
    public object? Output { get; set; }
}
