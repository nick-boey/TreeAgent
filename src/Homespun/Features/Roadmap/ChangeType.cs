using System.Text.Json.Serialization;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Type of change.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeType
{
    [JsonPropertyName("feature")]
    Feature,

    [JsonPropertyName("bug")]
    Bug,

    [JsonPropertyName("refactor")]
    Refactor,

    [JsonPropertyName("docs")]
    Docs,

    [JsonPropertyName("test")]
    Test,

    [JsonPropertyName("chore")]
    Chore
}