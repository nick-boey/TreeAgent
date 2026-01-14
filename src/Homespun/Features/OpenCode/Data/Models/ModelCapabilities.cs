namespace Homespun.Features.OpenCode.Data.Models;

public class ModelCapabilities
{
    public bool Temperature { get; set; }
    public bool Reasoning { get; set; }
    public bool Attachment { get; set; }
    public bool Toolcall { get; set; }
    public ModelInputOutputCapabilities Input { get; set; } = new();
    public ModelInputOutputCapabilities Output { get; set; } = new();
    public object? Interleaved { get; set; }
}

public class ModelInputOutputCapabilities
{
    public bool Text { get; set; }
    public bool Audio { get; set; }
    public bool Image { get; set; }
    public bool Video { get; set; }
    public bool Pdf { get; set; }
}
