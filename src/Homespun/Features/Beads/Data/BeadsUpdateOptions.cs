namespace Homespun.Features.Beads.Data;

/// <summary>
/// Options for updating a beads issue.
/// </summary>
public class BeadsUpdateOptions
{
    /// <summary>
    /// New title for the issue.
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// New description for the issue.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// New type for the issue.
    /// </summary>
    public BeadsIssueType? Type { get; set; }
    
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
    
    /// <summary>
    /// New parent issue ID.
    /// </summary>
    public string? ParentId { get; set; }
    
    /// <summary>
    /// Labels to add to the issue.
    /// </summary>
    public List<string>? LabelsToAdd { get; set; }
    
    /// <summary>
    /// Labels to remove from the issue.
    /// </summary>
    public List<string>? LabelsToRemove { get; set; }
}
