using System.Text.Json;
using System.Text.Json.Serialization;

namespace TreeAgent.Web.Features.Agents.Services;

public class MessageParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ParsedMessage? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString() ?? "unknown"
                : "unknown";

            var message = new ParsedMessage
            {
                Type = type,
                RawJson = json
            };

            switch (type)
            {
                case "text":
                    if (root.TryGetProperty("content", out var contentProp))
                        message.Content = contentProp.GetString();
                    break;

                case "tool_use":
                    if (root.TryGetProperty("name", out var nameProp))
                        message.ToolName = nameProp.GetString();
                    if (root.TryGetProperty("input", out var inputProp))
                        message.ToolInput = inputProp.ToString();
                    break;

                case "tool_result":
                    if (root.TryGetProperty("content", out var resultProp))
                        message.Content = resultProp.GetString();
                    break;

                case "system":
                    if (root.TryGetProperty("content", out var sysProp))
                        message.Content = sysProp.GetString();
                    break;

                case "error":
                    if (root.TryGetProperty("message", out var errProp))
                        message.ErrorMessage = errProp.GetString();
                    break;
            }

            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}