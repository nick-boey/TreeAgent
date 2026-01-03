namespace TreeAgent.Web.Features.GitHub;

/// <summary>
/// Represents a GitHub pull request
/// </summary>
public class GitHubPullRequest
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public required string State { get; set; } // "open", "closed"
    public bool Merged { get; set; }
    public required string BranchName { get; set; }
    public string? HtmlUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}