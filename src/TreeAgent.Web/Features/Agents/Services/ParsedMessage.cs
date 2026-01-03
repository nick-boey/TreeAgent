namespace TreeAgent.Web.Features.Agents.Services;

public class ParsedMessage
{
    public string Type { get; set; } = "unknown";
    public string? Content { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? ErrorMessage { get; set; }
    public string RawJson { get; set; } = "";
}