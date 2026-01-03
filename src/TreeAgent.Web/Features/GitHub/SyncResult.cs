namespace TreeAgent.Web.Features.GitHub;

/// <summary>
/// Result of a sync operation
/// </summary>
public class SyncResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; set; } = [];
}