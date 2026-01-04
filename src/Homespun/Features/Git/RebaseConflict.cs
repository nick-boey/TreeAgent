namespace Homespun.Features.Git;

/// <summary>
/// Information about a rebase conflict.
/// </summary>
public record RebaseConflict(string BranchName, string FeatureId, string ErrorMessage);