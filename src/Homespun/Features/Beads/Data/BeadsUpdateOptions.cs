namespace Homespun.Features.Beads.Data;

/// <summary>
/// Options for updating a beads issue.
/// </summary>
public class BeadsUpdateOptions
{
    /// <summary>
    /// New status for the issue.
    /// </summary>
    public BeadsIssueStatus? Status { get; set; }
    
    /// <summary>
    /// New priority for the issue.
    /// </summary>
    public int? Priority { get; set; }
    
    /// <summary>
    /// New assignee for the issue.
    /// </summary>
    public string? Assignee { get; set; }
}
