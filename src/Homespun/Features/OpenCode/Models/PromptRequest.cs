using System.Text.Json.Serialization;

namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Request body for sending a prompt to an OpenCode session.
/// </summary>
public class PromptRequest
{
    [JsonPropertyName("parts")]
    public List<PromptPart> Parts { get; set; } = [];

    [JsonPropertyName("model")]
    public PromptModel? Model { get; set; }

    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    [JsonPropertyName("noReply")]
    public bool? NoReply { get; set; }

    [JsonPropertyName("system")]
    public string? System { get; set; }

    /// <summary>
    /// Creates a simple text prompt request.
    /// </summary>
    public static PromptRequest FromText(string text, string? model = null)
    {
        var request = new PromptRequest
        {
            Parts = [new PromptPart { Type = "text", Text = text }]
        };

        if (model != null)
        {
            var parts = model.Split('/');
            if (parts.Length == 2)
            {
                request.Model = new PromptModel
                {
                    ProviderId = parts[0],
                    ModelId = parts[1]
                };
            }
        }

        return request;
    }
}

public class PromptPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

public class PromptModel
{
    [JsonPropertyName("providerID")]
    public string ProviderId { get; set; } = "";

    [JsonPropertyName("modelID")]
    public string ModelId { get; set; } = "";
}
