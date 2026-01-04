using System.Text.Json.Serialization;

namespace Homespun.Features.Roadmap;

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