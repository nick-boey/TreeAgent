namespace Homespun.Features.Beads.Data;

/// <summary>
/// Options for creating a new beads issue.
/// </summary>
public class BeadsCreateOptions
{
    /// <summary>
    /// The title of the issue (required).
    /// </summary>
    public required string Title { get; set; }
    
    /// <summary>
    /// Description/body of the issue.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Type of the issue.
    /// </summary>
    public BeadsIssueType Type { get; set; } = BeadsIssueType.Task;
    
    /// <summary>
    /// Priority level (0-4).
    /// </summary>
    public int? Priority { get; set; }
    
    /// <summary>
    /// Labels to attach to the issue.
    /// </summary>
    public List<string>? Labels { get; set; }
    
    /// <summary>
    /// Parent issue ID for hierarchical issues.
    /// </summary>
    public string? ParentId { get; set; }
    
    /// <summary>
    /// Issue IDs that this issue depends on (blocks relationship).
    /// </summary>
    public List<string>? BlockedBy { get; set; }
}
