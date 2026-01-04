using System.Text.Json.Serialization;

namespace Homespun.Features.OpenCode.Models;

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

    /// <summary>
    /// Tool state - contains status and other details for tool parts.
    /// </summary>
    [JsonPropertyName("state")]
    public ToolState? State { get; set; }

    [JsonPropertyName("input")]
    public object? Input { get; set; }

    [JsonPropertyName("output")]
    public object? Output { get; set; }
    
    // Additional properties for different part types
    [JsonPropertyName("tool")]
    public string? Tool { get; set; }
    
    [JsonPropertyName("callID")]
    public string? CallId { get; set; }
}

/// <summary>
/// Tool execution state from OpenCode.
/// </summary>
public class ToolState
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
    
    [JsonPropertyName("input")]
    public object? Input { get; set; }
    
    [JsonPropertyName("output")]
    public string? Output { get; set; }
    
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }
    
    [JsonPropertyName("time")]
    public ToolStateTime? Time { get; set; }
}

public class ToolStateTime
{
    [JsonPropertyName("start")]
    public long Start { get; set; }
    
    [JsonPropertyName("end")]
    public long? End { get; set; }
}
