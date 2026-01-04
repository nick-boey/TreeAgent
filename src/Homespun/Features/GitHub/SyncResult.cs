namespace Homespun.Features.GitHub;

/// <summary>
/// Result of a sync operation
/// </summary>
public class SyncResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public List<string> Errors { get; set; } = [];
}