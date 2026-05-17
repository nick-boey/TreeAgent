using System.Text.RegularExpressions;
using Homespun.Shared.Models.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Features.Git;

public class GitCloneService(ICommandRunner commandRunner, ILogger<GitCloneService> logger) : IGitCloneService
{
    public GitCloneService() : this(
        new CommandRunner(
            new NullGitHubEnvironmentService(),
            NullLogger<CommandRunner>.Instance),
        NullLogger<GitCloneService>.Instance)
    {
    }

    /// <summary>
    /// A no-op implementation of IGitHubEnvironmentService for testing scenarios
    /// where GitHub authentication is not needed.
    /// </summary>
    private class NullGitHubEnvironmentService : IGitHubEnvironmentService
    {
        public bool IsConfigured => false;
        public IDictionary<string, string> GetGitHubEnvironment() => new Dictionary<string, string>();
        public string? GetMaskedToken() => null;
        public Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new GitHubAuthStatus { IsAuthenticated = false, AuthMethod = GitHubAuthMethod.None });
        public string GetGitAuthorName() => "Unknown";
        public string GetGitAuthorEmail() => "unknown@localhost";
    }

    public async Task<string?> CreateCloneAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null)
    {
        var flattenedName = SanitizeBranchNameForClone(branchName);
        // Create clone inside .clones directory as sibling of the main repo
        // e.g., ~/.homespun/src/repo/main -> ~/.homespun/src/repo/.clones/<flattened-branch-name>
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            logger.LogError("Cannot determine parent directory of {RepoPath}", repoPath);
            throw new InvalidOperationException($"Cannot determine parent directory of {repoPath}");
        }

        // Create .clones directory if it doesn't exist
        var clonesDir = Path.Combine(parentDir, ".clones");
        if (!Directory.Exists(clonesDir))
        {
            Directory.CreateDirectory(clonesDir);
        }

        // Normalize the path to use platform-native separators
        var clonePath = Path.GetFullPath(Path.Combine(clonesDir, flattenedName));

        // If clone directory already exists, remove it first
        if (Directory.Exists(clonePath))
        {
            ForceDeleteDirectory(clonePath);
        }

        // Create the clone directory structure:
        // - {clonePath}/.claude : Claude state directory (mounted as /home/homespun/.claude)
        // - {clonePath}/workdir : Git repository (mounted as /workdir)
        Directory.CreateDirectory(clonePath);
        var claudeDir = Path.Combine(clonePath, ".claude");
        Directory.CreateDirectory(claudeDir);

        var workdirPath = GetWorkdirPath(clonePath);

        // Get the real remote URL from the main repo before cloning
        var remoteUrlResult = await commandRunner.RunAsync("git", "remote get-url origin", repoPath);
        var remoteUrl = remoteUrlResult is { Success: true } ? remoteUrlResult.Output.Trim() : null;

        if (createBranch)
        {
            var baseRef = baseBranch ?? "HEAD";

            // If using a non-default base branch, ensure it's available locally
            if (baseBranch != null)
            {
                await EnsureBranchAvailableAsync(repoPath, baseBranch);
            }

            var branchResult = await commandRunner.RunAsync("git", $"branch \"{branchName}\" \"{baseRef}\"", repoPath);
            if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
            {
                return null;
            }
        }

        // Clone the main repo locally into workdir subdirectory (uses hardlinks for efficiency)
        var cloneResult = await commandRunner.RunAsync("git", $"clone --local \"{repoPath}\" \"{workdirPath}\"", repoPath);

        if (!cloneResult.Success)
        {
            logger.LogWarning("Failed to create clone at {ClonePath} for branch {BranchName}: {Error}",
                workdirPath, branchName, cloneResult.Error);
            // Clean up the failed clone directory
            ForceDeleteDirectory(clonePath);
            return null;
        }

        // Fix remote URL - after clone --local, origin points to local path
        if (!string.IsNullOrEmpty(remoteUrl))
        {
            await commandRunner.RunAsync("git", $"remote set-url origin \"{remoteUrl}\"", workdirPath);
        }

        // Checkout the target branch in the clone
        var checkoutResult = await commandRunner.RunAsync("git", $"checkout \"{branchName}\"", workdirPath);

        if (!checkoutResult.Success)
        {
            logger.LogWarning("Failed to checkout branch {BranchName} in clone at {ClonePath}: {Error}",
                branchName, workdirPath, checkoutResult.Error);
            // Clean up the failed clone
            ForceDeleteDirectory(clonePath);
            return null;
        }

        // After checkout succeeds, copy unstaged .fleece changes
        try
        {
            await CopyFleeceChangesAsync(repoPath, workdirPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error copying .fleece changes to clone");
            // Don't fail the clone creation
        }

        // Wire the fleece pre-commit hook + gitignore entries inside the clone so the
        // agent's commits stage .fleece/changes/ correctly and the per-clone
        // .fleece/.active-change / .replay-cache files don't leak into git.
        var installResult = await commandRunner.RunAsync("fleece", "install", workdirPath);
        if (installResult is null || !installResult.Success)
        {
            logger.LogWarning("`fleece install` failed in clone {ClonePath}: {Error}", workdirPath, installResult?.Error);
        }

        logger.LogInformation("Created clone at {ClonePath} for branch {BranchName}", clonePath, branchName);

        // Return the workdir path (the actual git repository) for Docker mounting
        return workdirPath;
    }

    public Task<bool> RemoveCloneAsync(string repoPath, string clonePath)
    {
        try
        {
            // Handle both workdir path and clone root path formats
            // If clonePath ends with "workdir", we need to delete the parent directory
            var normalizedPath = clonePath.TrimEnd('/', '\\');
            var pathToDelete = normalizedPath.EndsWith("workdir", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(normalizedPath) ?? normalizedPath
                : normalizedPath;

            if (!Directory.Exists(pathToDelete))
            {
                logger.LogWarning("Clone directory does not exist at {ClonePath}", pathToDelete);
                return Task.FromResult(false);
            }

            ForceDeleteDirectory(pathToDelete);
            logger.LogInformation("Removed clone {ClonePath}", pathToDelete);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove clone at {ClonePath}", clonePath);
            return Task.FromResult(false);
        }
    }

    public async Task<List<CloneInfo>> ListClonesAsync(string repoPath)
    {
        var clones = await ListClonesRawAsync(repoPath);

        // Populate ExpectedBranch for branch mismatch detection
        var branches = await ListLocalBranchesAsync(repoPath);
        foreach (var clone in clones.Where(w => !w.IsBare))
        {
            var expectedBranch = DetermineExpectedBranch(clone, branches);
            if (expectedBranch != null)
            {
                var currentBranch = clone.Branch?.Replace("refs/heads/", "") ?? "";
                if (!string.Equals(currentBranch, expectedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    clone.ExpectedBranch = expectedBranch;
                }
            }
        }

        return clones;
    }

    /// <summary>
    /// Lists clones by scanning the .clones/ directory and querying each for branch/commit info.
    /// This is the raw version that does NOT enrich with branch mismatch detection,
    /// avoiding mutual recursion with ListLocalBranchesAsync.
    /// </summary>
    private async Task<List<CloneInfo>> ListClonesRawAsync(string repoPath)
    {
        var clones = new List<CloneInfo>();

        // Always include the main repo itself
        var mainBranchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath);
        var mainCommitResult = await commandRunner.RunAsync("git", "rev-parse HEAD", repoPath);

        clones.Add(new CloneInfo
        {
            Path = repoPath,
            Branch = mainBranchResult is { Success: true }
                ? $"refs/heads/{mainBranchResult.Output.Trim()}"
                : null,
            HeadCommit = mainCommitResult is { Success: true }
                ? mainCommitResult.Output.Trim()
                : null,
            IsBare = false,
            IsDetached = false
        });

        // Scan .clones directory for clone directories
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            return clones;
        }

        var clonesDir = Path.Combine(parentDir, ".clones");
        if (!Directory.Exists(clonesDir))
        {
            return clones;
        }

        foreach (var dir in Directory.GetDirectories(clonesDir))
        {
            // Check for new directory structure: {dir}/workdir/.git
            var workdirPath = GetWorkdirPath(dir);
            var workdirGitPath = Path.Combine(workdirPath, ".git");

            // Fall back to legacy structure: {dir}/.git
            var legacyGitPath = Path.Combine(dir, ".git");

            string gitRepoPath;
            if (File.Exists(workdirGitPath) || Directory.Exists(workdirGitPath))
            {
                // New structure: git repo is in workdir subdirectory
                gitRepoPath = workdirPath;
            }
            else if (File.Exists(legacyGitPath) || Directory.Exists(legacyGitPath))
            {
                // Legacy structure: git repo is directly in clone directory
                gitRepoPath = dir;
            }
            else
            {
                // Not a valid clone
                continue;
            }

            var branchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", gitRepoPath);
            var commitResult = await commandRunner.RunAsync("git", "rev-parse HEAD", gitRepoPath);

            var branchName = branchResult is { Success: true } ? branchResult.Output.Trim() : null;
            var commitHash = commitResult is { Success: true } ? commitResult.Output.Trim() : null;

            clones.Add(new CloneInfo
            {
                Path = Path.GetFullPath(dir),
                WorkdirPath = Path.GetFullPath(gitRepoPath),
                Branch = branchName != null ? $"refs/heads/{branchName}" : null,
                HeadCommit = commitHash,
                IsBare = false,
                IsDetached = branchName == "HEAD"
            });
        }

        return clones;
    }

    /// <summary>
    /// Determines the expected branch for a clone based on its folder name.
    /// Checks both sanitized and unsanitized branch names.
    /// </summary>
    private string? DetermineExpectedBranch(CloneInfo clone, List<BranchInfo> branches)
    {
        var folderName = clone.FolderName;
        if (string.IsNullOrEmpty(folderName)) return null;

        // First check for exact match (unsanitized)
        var exactMatch = branches.FirstOrDefault(b =>
            string.Equals(b.ShortName, folderName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null) return exactMatch.ShortName;

        // Then check for sanitized match using clone sanitization (flattened with + instead of /)
        foreach (var branch in branches)
        {
            var sanitizedBranchName = SanitizeBranchNameForClone(branch.ShortName);
            if (string.Equals(sanitizedBranchName, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return branch.ShortName;
            }
        }

        return null;
    }

    public Task PruneClonesAsync(string repoPath)
    {
        // Scan .clones/ directory for broken clones (missing .git) and delete them
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            return Task.CompletedTask;
        }

        var clonesDir = Path.Combine(parentDir, ".clones");
        if (!Directory.Exists(clonesDir))
        {
            return Task.CompletedTask;
        }

        foreach (var dir in Directory.GetDirectories(clonesDir))
        {
            // Check for new structure: {dir}/workdir/.git
            var workdirGitPath = Path.Combine(GetWorkdirPath(dir), ".git");
            // Check for legacy structure: {dir}/.git
            var legacyGitPath = Path.Combine(dir, ".git");

            var hasNewStructure = File.Exists(workdirGitPath) || Directory.Exists(workdirGitPath);
            var hasLegacyStructure = File.Exists(legacyGitPath) || Directory.Exists(legacyGitPath);

            if (!hasNewStructure && !hasLegacyStructure)
            {
                try
                {
                    logger.LogInformation("Pruning broken clone at {Path}", dir);
                    ForceDeleteDirectory(dir);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to prune broken clone at {Path}", dir);
                }
            }
        }

        return Task.CompletedTask;
    }

    public async Task<bool> CloneExistsAsync(string repoPath, string branchName)
    {
        var clones = await ListClonesAsync(repoPath);
        return clones.Any(w => w.Branch?.EndsWith(branchName) == true);
    }

    public async Task<string?> GetClonePathForBranchAsync(string repoPath, string branchName)
    {
        var clones = await ListClonesAsync(repoPath);

        // First, try direct branch match (handles most cases where branch name hasn't been sanitized)
        var directMatch = clones.FirstOrDefault(w =>
            w.Branch?.EndsWith(branchName) == true ||
            w.Branch == $"refs/heads/{branchName}");

        if (directMatch != null)
        {
            // Return WorkdirPath for Docker mounting
            var resultPath = directMatch.WorkdirPath ?? directMatch.Path;
            logger.LogDebug("Found clone for branch {BranchName} via direct match at {Path}", branchName, resultPath);
            return resultPath;
        }

        // Fall back to path-based matching using flattened name in .clones directory
        // This handles cases where the branch name contains special characters
        var flattenedName = SanitizeBranchNameForClone(branchName);
        var parentDir = Path.GetDirectoryName(repoPath);

        if (string.IsNullOrEmpty(parentDir))
        {
            return null;
        }

        var expectedPath = Path.GetFullPath(Path.Combine(parentDir, ".clones", flattenedName));
        var pathMatch = clones.FirstOrDefault(w =>
            Path.GetFullPath(w.Path).Equals(expectedPath, StringComparison.OrdinalIgnoreCase));

        if (pathMatch != null)
        {
            // Return WorkdirPath for Docker mounting
            var resultPath = pathMatch.WorkdirPath ?? pathMatch.Path;
            logger.LogDebug("Found clone for branch {BranchName} via flattened path match at {Path}", branchName, resultPath);
            return resultPath;
        }

        return null;
    }

    public async Task<bool> PullLatestAsync(string clonePath)
    {
        // Determine the git repo path (new structure uses workdir subdirectory)
        var gitRepoPath = GetGitRepoPath(clonePath);

        // First fetch from remote (may fail for local-only branches, which is OK)
        await commandRunner.RunAsync("git", "fetch origin", gitRepoPath);

        // Try to pull with rebase to get latest changes
        var pullResult = await commandRunner.RunAsync("git", "pull --rebase --autostash", gitRepoPath);

        // Pull might fail if no upstream is set, which is fine for new branches
        return pullResult.Success ||
               pullResult.Error.Contains("no tracking information") ||
               pullResult.Error.Contains("There is no tracking information");
    }

    /// <summary>
    /// Gets the git repository path for a clone directory.
    /// Handles both new structure (workdir subdirectory) and legacy structure (direct).
    /// </summary>
    private string GetGitRepoPath(string clonePath)
    {
        var workdirPath = GetWorkdirPath(clonePath);
        var workdirGitPath = Path.Combine(workdirPath, ".git");

        // Check for new structure first
        if (File.Exists(workdirGitPath) || Directory.Exists(workdirGitPath))
        {
            return workdirPath;
        }

        // Fall back to legacy structure (git repo directly in clone path)
        return clonePath;
    }

    public async Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName)
    {
        // Fetch the specific branch from origin
        var fetchResult = await commandRunner.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath);

        if (!fetchResult.Success)
        {
            // Try a simple fetch if the branch update fails (might be checked out)
            var simpleFetchResult = await commandRunner.RunAsync("git", "fetch origin", repoPath);

            if (!simpleFetchResult.Success)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sanitizes a branch name for use in git commands while preserving slashes.
    /// </summary>
    public static string SanitizeBranchName(string branchName)
    {
        // Normalize path separators to forward slashes
        var sanitized = branchName.Replace('\\', '/');
        // Replace special characters (except forward slash and plus) with dashes
        sanitized = Regex.Replace(sanitized, @"[@#\s]+", "-");
        // Remove any remaining invalid characters (keep forward slashes and plus)
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-_./+]", "-");
        // Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        // Remove consecutive slashes
        sanitized = Regex.Replace(sanitized, @"/+", "/");
        // Trim dashes and slashes from ends
        return sanitized.Trim('-', '/');
    }

    /// <summary>
    /// Gets the workdir path for a clone directory.
    /// In the new structure, git repo is cloned into {clonePath}/workdir.
    /// </summary>
    public static string GetWorkdirPath(string clonePath)
    {
        var trimmed = clonePath.TrimEnd('/', '\\');
        return $"{trimmed}/workdir";
    }

    /// <summary>
    /// Sanitizes a branch name for use as a clone folder name.
    /// Converts slashes to plus signs to create a flat folder structure.
    /// Example: "feature/my-branch+abc123" -> "feature+my-branch+abc123"
    /// </summary>
    public static string SanitizeBranchNameForClone(string branchName)
    {
        // Normalize path separators and convert to plus for flat structure
        var sanitized = branchName.Replace('\\', '+').Replace('/', '+');
        // Replace special characters (except plus) with dashes
        sanitized = Regex.Replace(sanitized, @"[@#\s]+", "-");
        // Remove any remaining invalid characters (keep plus for the flattened structure)
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-_+.]", "-");
        // Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        // Remove consecutive plus signs
        sanitized = Regex.Replace(sanitized, @"\++", "+");
        // Trim dashes and plus from ends
        return sanitized.Trim('-', '+');
    }

    public async Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath)
    {
        // Get list of clones first to map branches to their clone paths
        // Use the raw version to avoid mutual recursion with ListClonesAsync
        var clones = await ListClonesRawAsync(repoPath);
        var cloneByBranch = new Dictionary<string, string>();
        foreach (var clone in clones.Where(w => !string.IsNullOrEmpty(w.Branch)))
        {
            var branchName = clone.Branch!.Replace("refs/heads/", "");
            cloneByBranch.TryAdd(branchName, clone.Path);
        }

        // Get branch info with format: branch name, commit sha, upstream, and tracking info
        var result = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)|%(objectname:short)|%(upstream:short)|%(upstream:track)|%(committerdate:iso8601)|%(subject)' refs/heads/",
            repoPath);

        if (result == null || !result.Success)
        {
            logger.LogWarning("Failed to list local branches: {Error}", result?.Error);
            return [];
        }

        var branches = new List<BranchInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Get the current branch
        var currentBranchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath);
        var currentBranch = currentBranchResult is { Success: true } ? currentBranchResult.Output.Trim() : "";

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim().Trim('\'');
            var parts = trimmedLine.Split('|');
            if (parts.Length < 6) continue;

            var branchName = parts[0];
            var commitSha = parts[1];
            var upstream = string.IsNullOrEmpty(parts[2]) ? null : parts[2];
            var trackingInfo = parts[3];
            var dateStr = parts[4];
            var subject = parts[5];

            // Parse tracking info like "[ahead 2, behind 1]" or "[ahead 2]" or "[behind 1]"
            var (ahead, behind) = ParseTrackingInfo(trackingInfo);

            var branch = new BranchInfo
            {
                Name = $"refs/heads/{branchName}",
                ShortName = branchName,
                IsCurrent = branchName == currentBranch,
                CommitSha = commitSha,
                Upstream = upstream,
                AheadCount = ahead,
                BehindCount = behind,
                LastCommitMessage = subject,
                LastCommitDate = DateTime.TryParse(dateStr, out var date) ? date : null
            };

            // Check if branch has a clone
            if (cloneByBranch.TryGetValue(branchName, out var clonePath))
            {
                branch.HasClone = true;
                branch.ClonePath = clonePath;
            }

            branches.Add(branch);
        }

        return branches;
    }

    public async Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath)
    {
        // First fetch to ensure we have the latest remote refs
        await commandRunner.RunAsync("git", "fetch --prune", repoPath);

        // Get all remote branches
        var remoteResult = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)' refs/remotes/origin/",
            repoPath);

        if (!remoteResult.Success)
        {
            logger.LogWarning("Failed to list remote branches: {Error}", remoteResult.Error);
            return [];
        }

        var remoteBranches = remoteResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().Trim('\''))
            .Where(b => !b.EndsWith("/HEAD")) // Skip the HEAD pointer
            .Select(b => b.Replace("origin/", ""))
            .ToHashSet();

        // Get all local branches
        var localResult = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)' refs/heads/",
            repoPath);

        if (!localResult.Success)
        {
            return remoteBranches.ToList();
        }

        var localBranches = localResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().Trim('\''))
            .ToHashSet();

        // Return remote branches that don't have a local counterpart
        return remoteBranches.Except(localBranches).ToList();
    }

    public async Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        // Check if the branch is an ancestor of the target branch
        var result = await commandRunner.RunAsync(
            "git",
            $"merge-base --is-ancestor \"{branchName}\" \"{targetBranch}\"",
            repoPath);

        // Exit code 0 means it's an ancestor (merged), non-zero means not
        return result.Success;
    }

    public async Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false)
    {
        var flag = force ? "-D" : "-d";
        var result = await commandRunner.RunAsync("git", $"branch {flag} \"{branchName}\"", repoPath);

        if (result.Success)
        {
            logger.LogInformation("Deleted local branch {BranchName}", branchName);
        }
        else
        {
            logger.LogWarning("Failed to delete local branch {BranchName}: {Error}", branchName, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName)
    {
        // First check if the remote branch exists
        var exists = await RemoteBranchExistsAsync(repoPath, branchName);

        if (!exists)
        {
            logger.LogInformation("Remote branch {BranchName} does not exist, nothing to delete", branchName);
            return true; // Return true since the end state (branch not on remote) is achieved
        }

        var result = await commandRunner.RunAsync("git", $"push origin --delete \"{branchName}\"", repoPath);

        if (result.Success)
        {
            logger.LogInformation("Deleted remote branch {BranchName}", branchName);
        }
        else
        {
            logger.LogWarning("Failed to delete remote branch {BranchName}: {Error}", branchName, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> RemoteBranchExistsAsync(string repoPath, string branchName)
    {
        // Use git ls-remote to check if branch exists on remote
        // This is more reliable than checking local refs
        var result = await commandRunner.RunAsync(
            "git",
            $"ls-remote --heads origin \"{branchName}\"",
            repoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to check remote branch existence for {BranchName}: {Error}", branchName, result.Error);
            return false;
        }

        // If output contains the branch name, it exists
        return !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch)
    {
        // The remoteBranch parameter is expected to be the branch name without "origin/" prefix
        // e.g., "feature/test" not "origin/feature/test"
        var localBranchName = remoteBranch;

        // Create local branch tracking the remote
        var result = await commandRunner.RunAsync(
            "git",
            $"checkout -b \"{localBranchName}\" \"origin/{localBranchName}\"",
            repoPath);

        if (!result.Success)
        {
            // Try an alternative approach - just create the branch without checking out
            result = await commandRunner.RunAsync(
                "git",
                $"branch \"{localBranchName}\" \"origin/{localBranchName}\"",
                repoPath);
        }

        if (result.Success)
        {
            logger.LogInformation("Created local branch {BranchName} from remote", localBranchName);
        }
        else
        {
            logger.LogWarning("Failed to create local branch from remote {RemoteBranch}: {Error}", remoteBranch, result.Error);
        }

        return result.Success;
    }

    public async Task<(int ahead, int behind)> GetBranchDivergenceAsync(string repoPath, string branchName, string targetBranch)
    {
        var result = await commandRunner.RunAsync(
            "git",
            $"rev-list --left-right --count \"{targetBranch}...{branchName}\"",
            repoPath);

        if (!result.Success)
        {
            return (0, 0);
        }

        var parts = result.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        // Output format is: behind<tab>ahead
        int.TryParse(parts[0], out var behind);
        int.TryParse(parts[1], out var ahead);

        return (ahead, behind);
    }

    public async Task<bool> FetchAllAsync(string repoPath)
    {
        var result = await commandRunner.RunAsync("git", "fetch --all --prune", repoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to fetch all: {Error}", result.Error);
        }

        return result.Success;
    }

    /// <summary>
    /// Syncs the .fleece/ directory from the main repo to a clone.
    /// Always deletes and copies the full .fleece directory to ensure consistency.
    /// </summary>
    private Task CopyFleeceChangesAsync(string repoPath, string clonePath)
    {
        var sourceFleecePath = Path.Combine(repoPath, ".fleece");
        var destFleecePath = Path.Combine(clonePath, ".fleece");

        // Skip if source .fleece doesn't exist
        if (!Directory.Exists(sourceFleecePath))
        {
            logger.LogDebug("No .fleece directory to copy from {RepoPath}", repoPath);
            return Task.CompletedTask;
        }

        logger.LogInformation("Syncing .fleece directory from {RepoPath} to clone at {ClonePath}",
            repoPath, clonePath);

        // Delete existing .fleece in clone (from git checkout)
        if (Directory.Exists(destFleecePath))
        {
            ForceDeleteDirectory(destFleecePath);
            logger.LogDebug("Deleted existing .fleece directory from clone");
        }

        // Copy the full .fleece directory from main
        CopyDirectory(sourceFleecePath, destFleecePath, name => name == ".git");
        logger.LogInformation("Successfully synced .fleece directory to clone");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Recursively copies a directory and its contents.
    /// </summary>
    /// <param name="sourceDir">The source directory path</param>
    /// <param name="destDir">The destination directory path</param>
    /// <param name="shouldExclude">Optional predicate to exclude files/directories by name</param>
    private static void CopyDirectory(string sourceDir, string destDir, Func<string, bool>? shouldExclude = null)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        // Create the destination directory
        Directory.CreateDirectory(destDir);

        // Copy all files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            if (shouldExclude?.Invoke(fileName) == true)
            {
                continue;
            }

            var destFile = Path.Combine(destDir, fileName);
            // Clear read-only attribute if the destination file exists
            if (File.Exists(destFile))
            {
                File.SetAttributes(destFile, FileAttributes.Normal);
            }
            File.Copy(file, destFile, overwrite: true);
        }

        // Recursively copy subdirectories
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            if (shouldExclude?.Invoke(dirName) == true)
            {
                continue;
            }

            var destSubDir = Path.Combine(destDir, dirName);
            CopyDirectory(dir, destSubDir, shouldExclude);
        }
    }

    /// <summary>
    /// Deletes a directory recursively, clearing read-only file attributes first.
    /// Required on Windows where git creates read-only files in .git/objects.
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    private static (int ahead, int behind) ParseTrackingInfo(string trackingInfo)
    {
        var ahead = 0;
        var behind = 0;

        if (string.IsNullOrEmpty(trackingInfo))
            return (ahead, behind);

        // Parse formats like "[ahead 2]", "[behind 1]", "[ahead 2, behind 1]"
        var aheadMatch = Regex.Match(trackingInfo, @"ahead (\d+)");
        var behindMatch = Regex.Match(trackingInfo, @"behind (\d+)");

        if (aheadMatch.Success)
            int.TryParse(aheadMatch.Groups[1].Value, out ahead);
        if (behindMatch.Success)
            int.TryParse(behindMatch.Groups[1].Value, out behind);

        return (ahead, behind);
    }

    public async Task<CloneStatus> GetCloneStatusAsync(string clonePath)
    {
        var gitRepoPath = GetGitRepoPath(clonePath);
        var result = await commandRunner.RunAsync("git", "status --porcelain", gitRepoPath);

        var status = new CloneStatus();

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return status;
        }

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 2) continue;

            var indexStatus = line[0];
            var workingTreeStatus = line[1];

            // Staged files: any non-space/? in first position
            if (indexStatus != ' ' && indexStatus != '?')
            {
                status.StagedCount++;
            }

            // Modified files: any non-space in second position (except ?)
            if (workingTreeStatus != ' ' && workingTreeStatus != '?')
            {
                status.ModifiedCount++;
            }

            // Untracked files: ??
            if (indexStatus == '?' && workingTreeStatus == '?')
            {
                status.UntrackedCount++;
            }
        }

        return status;
    }

    public async Task<List<LostCloneInfo>> FindLostCloneFoldersAsync(string repoPath)
    {
        var lostClones = new List<LostCloneInfo>();

        // Get the parent directory where clones are siblings
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
        {
            return lostClones;
        }

        // Get all tracked clone paths (use raw version to avoid unnecessary branch enrichment)
        var clones = await ListClonesRawAsync(repoPath);
        var trackedPaths = clones.Select(w => Path.GetFullPath(w.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Get all local branches for matching
        var branches = await ListLocalBranchesAsync(repoPath);
        var branchNames = branches.Select(b => b.ShortName).ToHashSet();

        // Scan directories to check: .clones/ contents, .worktrees/ contents (legacy), and sibling directories
        var dirsToScan = new List<string>();

        // Scan .clones/ directory
        var clonesDir = Path.Combine(parentDir, ".clones");
        if (Directory.Exists(clonesDir))
        {
            dirsToScan.AddRange(Directory.GetDirectories(clonesDir));
        }

        // Scan legacy .worktrees/ directory (backward compatibility)
        var legacyWorktreesDir = Path.Combine(parentDir, ".worktrees");
        if (Directory.Exists(legacyWorktreesDir))
        {
            dirsToScan.AddRange(Directory.GetDirectories(legacyWorktreesDir));
        }

        // Scan sibling directories
        dirsToScan.AddRange(Directory.GetDirectories(parentDir));

        foreach (var dir in dirsToScan.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(dir);

            // Skip if it's a tracked clone
            if (trackedPaths.Contains(fullPath))
            {
                continue;
            }

            // Skip the .clones and .worktrees directories themselves
            var dirName = Path.GetFileName(dir);
            if (dirName == ".clones" || dirName == ".worktrees")
            {
                continue;
            }

            // Check for new structure: {dir}/workdir/.git
            var workdirGitPath = Path.Combine(GetWorkdirPath(dir), ".git");
            // Check for legacy structure: {dir}/.git
            var legacyGitPath = Path.Combine(dir, ".git");

            var isGitRepo = File.Exists(workdirGitPath) || Directory.Exists(workdirGitPath) ||
                           File.Exists(legacyGitPath) || Directory.Exists(legacyGitPath);

            if (!isGitRepo)
            {
                continue;
            }

            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName))
            {
                continue;
            }

            var lostClone = new LostCloneInfo
            {
                Path = fullPath
            };

            // Try to match the folder name to a branch
            // The folder name might be a sanitized version of the branch name
            var matchingBranch = branchNames.FirstOrDefault(b =>
                SanitizeBranchName(b).Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
                SanitizeBranchNameForClone(b).Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
                b.Equals(folderName, StringComparison.OrdinalIgnoreCase));

            if (matchingBranch != null)
            {
                lostClone.MatchingBranchName = matchingBranch;
            }

            // Try to get the status of the lost clone
            try
            {
                lostClone.Status = await GetCloneStatusAsync(fullPath);
            }
            catch
            {
                // Ignore errors getting status
            }

            lostClones.Add(lostClone);
        }

        return lostClones;
    }

    public Task<bool> DeleteCloneFolderAsync(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                return Task.FromResult(false);
            }

            // Delete the directory recursively
            ForceDeleteDirectory(folderPath);
            logger.LogInformation("Deleted clone folder {FolderPath}", folderPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete clone folder {FolderPath}", folderPath);
            return Task.FromResult(false);
        }
    }

    public async Task<string?> GetCurrentBranchAsync(string clonePath)
    {
        var gitRepoPath = GetGitRepoPath(clonePath);
        var result = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", gitRepoPath);

        if (!result.Success)
        {
            return null;
        }

        return result.Output.Trim();
    }

    public async Task<bool> CheckoutBranchAsync(string clonePath, string branchName)
    {
        var gitRepoPath = GetGitRepoPath(clonePath);
        var result = await commandRunner.RunAsync("git", $"checkout \"{branchName}\"", gitRepoPath);

        if (result.Success)
        {
            logger.LogInformation("Checked out branch {BranchName} in clone {ClonePath}", branchName, clonePath);
        }
        else
        {
            logger.LogWarning("Failed to checkout branch {BranchName} in clone {ClonePath}: {Error}",
                branchName, clonePath, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> IsSquashMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        // First check if there are commits in branchName that aren't in targetBranch
        var logResult = await commandRunner.RunAsync(
            "git",
            $"log \"{targetBranch}..{branchName}\" --format=%H",
            repoPath);

        if (!logResult.Success || string.IsNullOrWhiteSpace(logResult.Output))
        {
            // No commits to check, or branch is already merged the normal way
            return false;
        }

        // Use git cherry to check if commits are already applied (squash-merged)
        // Empty output means all commits are equivalent to something in target
        var cherryResult = await commandRunner.RunAsync(
            "git",
            $"cherry \"{targetBranch}\" \"{branchName}\"",
            repoPath);

        if (!cherryResult.Success)
        {
            return false;
        }

        // If cherry output is empty, all commits are already in target (squash-merged)
        // If output has "+" lines, those commits are not in target
        var output = cherryResult.Output.Trim();
        return string.IsNullOrEmpty(output) || !output.Contains('+');
    }

    public async Task<string?> CreateCloneFromRemoteBranchAsync(string repoPath, string remoteBranch)
    {
        // First, fetch from remote to ensure we have the latest refs
        await commandRunner.RunAsync("git", "fetch origin", repoPath);

        // Create local branch from remote WITHOUT checking it out (use git branch, not checkout)
        // This avoids changing the main repo's checked out branch
        var branchResult = await commandRunner.RunAsync(
            "git",
            $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"",
            repoPath);

        // It's okay if branch already exists
        if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
        {
            logger.LogWarning("Failed to create local branch from remote {RemoteBranch}: {Error}",
                remoteBranch, branchResult.Error);
            // Continue anyway - the branch might exist
        }

        // Reuse the clone-based CreateCloneAsync
        return await CreateCloneAsync(repoPath, remoteBranch);
    }

    /// <summary>
    /// Repairs a lost clone by re-cloning if needed.
    /// For valid clones, this is a no-op. For broken clones, it re-creates them.
    /// </summary>
    public async Task<bool> RepairCloneAsync(string repoPath, string folderPath, string branchName)
    {
        logger.LogInformation(
            "Attempting to repair clone at {FolderPath} for branch {BranchName}",
            folderPath, branchName);

        // Check if the branch exists locally
        var branchCheck = await commandRunner.RunAsync(
            "git",
            $"rev-parse --verify \"{branchName}\"",
            repoPath);

        if (!branchCheck.Success)
        {
            logger.LogWarning("Branch {BranchName} does not exist locally", branchName);
            return false;
        }

        // Check for new structure: {folderPath}/workdir/.git
        var workdirGitPath = Path.Combine(GetWorkdirPath(folderPath), ".git");
        // Check for legacy structure: {folderPath}/.git
        var legacyGitPath = Path.Combine(folderPath, ".git");

        var gitRepoPath = (File.Exists(workdirGitPath) || Directory.Exists(workdirGitPath))
            ? GetWorkdirPath(folderPath)
            : (File.Exists(legacyGitPath) || Directory.Exists(legacyGitPath))
                ? folderPath
                : null;

        if (gitRepoPath != null)
        {
            // Try a simple git status to verify the clone is valid
            var statusResult = await commandRunner.RunAsync("git", "status", gitRepoPath);
            if (statusResult.Success)
            {
                logger.LogInformation("Clone at {FolderPath} is valid, no repair needed", folderPath);
                return true;
            }
        }

        // Clone is broken - remove and re-create
        logger.LogInformation("Re-cloning broken clone at {FolderPath}", folderPath);

        if (Directory.Exists(folderPath))
        {
            ForceDeleteDirectory(folderPath);
        }

        var newPath = await CreateCloneAsync(repoPath, branchName);
        if (newPath != null)
        {
            logger.LogInformation("Successfully re-cloned at {FolderPath}", newPath);
            return true;
        }

        logger.LogError("Failed to repair clone at {FolderPath}", folderPath);
        return false;
    }

    public async Task<List<FileChangeInfo>> GetChangedFilesAsync(string clonePath, string targetBranch)
    {
        var gitRepoPath = GetGitRepoPath(clonePath);
        var result = await commandRunner.RunAsync("git", $"diff --numstat {targetBranch}...HEAD", gitRepoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to get changed files in {ClonePath}: {Error}", clonePath, result.Error);
            return [];
        }

        var files = new List<FileChangeInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t', 3);
            if (parts.Length < 3) continue;

            // Parse additions and deletions (may be "-" for binary files)
            var additionsStr = parts[0].Trim();
            var deletionsStr = parts[1].Trim();
            var filePath = parts[2].Trim();

            var additions = additionsStr == "-" ? 0 : int.TryParse(additionsStr, out var a) ? a : 0;
            var deletions = deletionsStr == "-" ? 0 : int.TryParse(deletionsStr, out var d) ? d : 0;

            // Determine file status
            var status = DetermineFileStatus(additions, deletions, filePath, additionsStr == "-");

            files.Add(new FileChangeInfo
            {
                FilePath = filePath,
                Additions = additions,
                Deletions = deletions,
                Status = status
            });
        }

        return files;
    }

    private static FileChangeStatus DetermineFileStatus(int additions, int deletions, string filePath, bool isBinary)
    {
        // Check for renamed files (path contains "=>")
        if (filePath.Contains("=>"))
        {
            return FileChangeStatus.Renamed;
        }

        // Binary files with no explicit line changes are considered modified
        if (isBinary)
        {
            return FileChangeStatus.Modified;
        }

        // Only additions means new file
        if (additions > 0 && deletions == 0)
        {
            return FileChangeStatus.Added;
        }

        // Only deletions means deleted file
        if (additions == 0 && deletions > 0)
        {
            return FileChangeStatus.Deleted;
        }

        // Both additions and deletions means modified
        return FileChangeStatus.Modified;
    }

    public async Task<SessionBranchInfo?> GetSessionBranchInfoAsync(string clonePath)
    {
        var gitRepoPath = GetGitRepoPath(clonePath);

        // Get current branch name
        var branchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", gitRepoPath);
        if (!branchResult.Success)
        {
            logger.LogWarning("Failed to get branch name for {ClonePath}: {Error}", clonePath, branchResult.Error);
            return null;
        }

        var branchName = branchResult.Output.Trim();

        // Get commit info: sha, subject, and date
        var commitResult = await commandRunner.RunAsync("git", "log -1 --format=%h|%s|%ci", gitRepoPath);
        string? commitSha = null;
        string? commitMessage = null;
        DateTime? commitDate = null;

        if (commitResult.Success)
        {
            var parts = commitResult.Output.Trim().Split('|', 3);
            if (parts.Length >= 1) commitSha = parts[0];
            if (parts.Length >= 2) commitMessage = parts[1];
            if (parts.Length >= 3 && DateTime.TryParse(parts[2], out var date))
            {
                commitDate = date;
            }
        }

        // Get ahead/behind count relative to upstream
        var divergenceResult = await commandRunner.RunAsync(
            "git",
            "rev-list --left-right --count @{upstream}...HEAD",
            gitRepoPath);

        int aheadCount = 0;
        int behindCount = 0;

        if (divergenceResult.Success)
        {
            var divergenceParts = divergenceResult.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (divergenceParts.Length >= 2)
            {
                int.TryParse(divergenceParts[0], out behindCount);
                int.TryParse(divergenceParts[1], out aheadCount);
            }
        }

        // Check for uncommitted changes
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain", gitRepoPath);
        var hasUncommittedChanges = statusResult.Success && !string.IsNullOrWhiteSpace(statusResult.Output);

        return new SessionBranchInfo
        {
            BranchName = branchName == "HEAD" ? null : branchName, // Detached HEAD returns "HEAD"
            CommitSha = commitSha,
            CommitMessage = commitMessage,
            CommitDate = commitDate,
            AheadCount = aheadCount,
            BehindCount = behindCount,
            HasUncommittedChanges = hasUncommittedChanges
        };
    }

    public async Task EnsureBranchAvailableAsync(string repoPath, string branchName)
    {
        // Check if branch exists locally
        var localBranches = await ListLocalBranchesAsync(repoPath);
        if (localBranches.Any(b => b.ShortName == branchName))
        {
            return;
        }

        // Fetch the branch from remote
        await commandRunner.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath);
    }
}
