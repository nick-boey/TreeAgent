using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.OpenCode.Models;

/// <summary>
/// Represents an event from the OpenCode SSE stream.
/// </summary>
public class OpenCodeEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("properties")]
    public OpenCodeEventProperties? Properties { get; set; }
}

public class OpenCodeEventProperties
{
    [JsonPropertyName("sessionID")]
    public string? SessionId { get; set; }

    [JsonPropertyName("messageID")]
    public string? MessageId { get; set; }

    [JsonPropertyName("partID")]
    public string? PartId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Known OpenCode event types.
/// </summary>
public static class OpenCodeEventTypes
{
    public const string ServerConnected = "server.connected";
    public const string SessionCreated = "session.created";
    public const string SessionUpdated = "session.updated";
    public const string SessionDeleted = "session.deleted";
    public const string MessageCreated = "message.created";
    public const string MessageUpdated = "message.updated";
    public const string PartUpdated = "part.updated";
    public const string ToolStart = "tool.start";
    public const string ToolComplete = "tool.complete";
}
