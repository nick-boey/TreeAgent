using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.OpenCode.Models;

/// <summary>
/// Represents the opencode.json configuration file structure.
/// </summary>
public class OpenCodeConfig
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://opencode.ai/config.json";

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("small_model")]
    public string? SmallModel { get; set; }

    [JsonPropertyName("permission")]
    public Dictionary<string, string>? Permission { get; set; }

    [JsonPropertyName("instructions")]
    public List<string>? Instructions { get; set; }

    [JsonPropertyName("provider")]
    public Dictionary<string, OpenCodeProviderConfig>? Provider { get; set; }

    [JsonPropertyName("tools")]
    public Dictionary<string, bool>? Tools { get; set; }

    [JsonPropertyName("autoupdate")]
    public bool? Autoupdate { get; set; }

    [JsonPropertyName("compaction")]
    public OpenCodeCompactionConfig? Compaction { get; set; }
}

public class OpenCodeProviderConfig
{
    [JsonPropertyName("options")]
    public Dictionary<string, object>? Options { get; set; }
}

public class OpenCodeCompactionConfig
{
    [JsonPropertyName("auto")]
    public bool? Auto { get; set; }

    [JsonPropertyName("prune")]
    public bool? Prune { get; set; }
}
