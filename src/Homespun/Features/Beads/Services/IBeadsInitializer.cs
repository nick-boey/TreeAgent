namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service interface for initializing beads in a repository.
/// </summary>
public interface IBeadsInitializer
{
    /// <summary>
    /// Initializes beads in the specified directory.
    /// </summary>
    /// <param name="projectPath">The path to the git repository.</param>
    /// <param name="syncBranch">The branch to use for syncing beads data (default: "beads-sync").</param>
    /// <returns>True if initialization succeeded.</returns>
    Task<bool> InitializeAsync(string projectPath, string? syncBranch = "beads-sync");
    
    /// <summary>
    /// Checks if beads is initialized in the given directory.
    /// </summary>
    /// <param name="projectPath">The path to check.</param>
    /// <returns>True if beads is initialized.</returns>
    Task<bool> IsInitializedAsync(string projectPath);
    
    /// <summary>
    /// Configures the sync branch for an existing beads installation.
    /// </summary>
    /// <param name="projectPath">The path to the git repository.</param>
    /// <param name="syncBranch">The branch name to use for syncing.</param>
    /// <returns>True if configuration succeeded.</returns>
    Task<bool> ConfigureSyncBranchAsync(string projectPath, string syncBranch);
}
