using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.Roadmap;

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