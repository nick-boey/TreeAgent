namespace Homespun.Features.Git;

/// <summary>
/// Result of a rebase operation across all open PRs.
/// </summary>
public class RebaseResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<RebaseConflict> Conflicts { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}