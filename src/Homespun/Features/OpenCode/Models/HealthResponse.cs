using System.Text.Json.Serialization;

namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Response from the OpenCode health endpoint.
/// </summary>
public class HealthResponse
{
    [JsonPropertyName("healthy")]
    public bool Healthy { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}
