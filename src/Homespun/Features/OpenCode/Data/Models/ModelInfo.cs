namespace Homespun.Features.OpenCode.Data.Models;

public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public ModelApi? Api { get; set; }
    public string Status { get; set; } = "active";
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, object> Options { get; set; } = new();
    public ModelCost? Cost { get; set; }
    public ModelLimit? Limit { get; set; }
    public ModelCapabilities? Capabilities { get; set; }
    public string ReleaseDate { get; set; } = string.Empty;
    public Dictionary<string, object>? Variants { get; set; }

    public string FullId => $"{ProviderId}/{Id}";
    public bool IsActive => Status == "active";
}

public class ModelApi
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Npm { get; set; } = string.Empty;
}
