namespace Homespun.Features.Beads.Data;

/// <summary>
/// Options for listing beads issues.
/// </summary>
public class BeadsListOptions
{
    /// <summary>
    /// Filter by status (e.g., "open", "closed", "in_progress").
    /// </summary>
    public string? Status { get; set; }
    
    /// <summary>
    /// Filter by priority (0-4).
    /// </summary>
    public int? Priority { get; set; }
    
    /// <summary>
    /// Filter by issue type.
    /// </summary>
    public BeadsIssueType? Type { get; set; }
    
    /// <summary>
    /// Filter by assignee.
    /// </summary>
    public string? Assignee { get; set; }
    
    /// <summary>
    /// Filter by label (must have all specified labels).
    /// </summary>
    public List<string>? Labels { get; set; }
    
    /// <summary>
    /// Filter by label (has any of the specified labels).
    /// </summary>
    public List<string>? LabelAny { get; set; }
    
    /// <summary>
    /// Search in title (substring match).
    /// </summary>
    public string? TitleContains { get; set; }
}
