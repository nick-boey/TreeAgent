using Homespun.Features.Commands;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Synchronises the project's `.fleece/` event-sourced storage with the git remote.
/// Under Fleece 3.1 the per-session `change_{guid}.jsonl` files have unique names so
/// divergent edits do not produce file-level conflicts — sync is just `git fetch` +
/// `git merge` + optional `fleece project` compaction on the default branch.
/// </summary>
public class FleeceIssuesSyncService(
    ICommandRunner commandRunner,
    ILogger<FleeceIssuesSyncService> logger) : IFleeceIssuesSyncService
{
    public async Task<BranchStatusResult> CheckBranchStatusAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Checking branch status for {ProjectPath}, expected branch: {Branch}", projectPath, defaultBranch);

        var branchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", projectPath);
        if (!branchResult.Success)
        {
            logger.LogWarning("Failed to get current branch: {Error}", branchResult.Error);
            return new BranchStatusResult(
                Success: false,
                IsOnCorrectBranch: false,
                CurrentBranch: null,
                ErrorMessage: $"Failed to get current branch: {branchResult.Error}",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        var currentBranch = branchResult.Output.Trim();
        var isOnCorrectBranch = currentBranch.Equals(defaultBranch, StringComparison.OrdinalIgnoreCase);

        if (!isOnCorrectBranch)
        {
            logger.LogWarning("Not on expected branch. Current: {Current}, Expected: {Expected}", currentBranch, defaultBranch);
            return new BranchStatusResult(
                Success: true,
                IsOnCorrectBranch: false,
                CurrentBranch: currentBranch,
                ErrorMessage: $"You are on branch '{currentBranch}' but fleece issues can only be synced from the '{defaultBranch}' branch. Please switch to '{defaultBranch}' first.",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", projectPath);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Failed to fetch from origin: {Error}", fetchResult.Error);
            return new BranchStatusResult(
                Success: false,
                IsOnCorrectBranch: true,
                CurrentBranch: currentBranch,
                ErrorMessage: $"Failed to fetch from remote: {fetchResult.Error}",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        var revListResult = await commandRunner.RunAsync("git", $"rev-list --left-right --count origin/{defaultBranch}...HEAD", projectPath);
        int commitsBehind = 0;
        int commitsAhead = 0;

        if (revListResult.Success)
        {
            var parts = revListResult.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out commitsBehind);
                int.TryParse(parts[1], out commitsAhead);
            }
        }

        logger.LogInformation("Branch status: {Branch} is {Behind} commits behind and {Ahead} commits ahead of origin",
            currentBranch, commitsBehind, commitsAhead);

        return new BranchStatusResult(
            Success: true,
            IsOnCorrectBranch: true,
            CurrentBranch: currentBranch,
            ErrorMessage: null,
            IsBehindRemote: commitsBehind > 0,
            CommitsBehind: commitsBehind,
            CommitsAhead: commitsAhead);
    }

    public async Task<FleecePullResult> PullFleeceOnlyAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Starting fleece pull-only for {ProjectPath} from branch {Branch}", projectPath, defaultBranch);

        var branchStatus = await CheckBranchStatusAsync(projectPath, defaultBranch, ct);
        if (!branchStatus.Success || !branchStatus.IsOnCorrectBranch)
        {
            return new FleecePullResult(
                Success: false,
                ErrorMessage: branchStatus.ErrorMessage,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0);
        }

        var nonFleeceChanges = await GetNonFleeceChangesAsync(projectPath);
        if (nonFleeceChanges.Count > 0)
        {
            logger.LogInformation("Found {Count} non-fleece changed files, will attempt pull anyway", nonFleeceChanges.Count);
        }

        if (!branchStatus.IsBehindRemote)
        {
            logger.LogInformation("Already up to date with remote");
            return new FleecePullResult(
                Success: true,
                ErrorMessage: null,
                IssuesMerged: 0,
                WasBehindRemote: false,
                CommitsPulled: 0,
                HasNonFleeceChanges: nonFleeceChanges.Count > 0,
                NonFleeceChangedFiles: nonFleeceChanges.Count > 0 ? nonFleeceChanges : null);
        }

        var pullResult = await PullAndMergeFleeceInternalAsync(projectPath, defaultBranch, branchStatus, ct);

        if (!pullResult.Success && nonFleeceChanges.Count > 0)
        {
            return pullResult with
            {
                HasNonFleeceChanges = true,
                NonFleeceChangedFiles = nonFleeceChanges,
                ErrorMessage = "Pull failed due to conflicting uncommitted changes. You can discard these changes and retry."
            };
        }

        if (nonFleeceChanges.Count > 0)
        {
            return pullResult with
            {
                HasNonFleeceChanges = true,
                NonFleeceChangedFiles = nonFleeceChanges
            };
        }

        return pullResult;
    }

    /// <summary>
    /// `git merge --no-edit origin/&lt;default&gt;` against the freshly-fetched remote
    /// state. v3.1 per-session change files have unique GUIDs so file-level conflicts
    /// don't occur in the normal case. On the default branch we also shell out to
    /// `fleece project` to compact events into the snapshot.
    /// </summary>
    private async Task<FleecePullResult> PullAndMergeFleeceInternalAsync(
        string projectPath,
        string defaultBranch,
        BranchStatusResult branchStatus,
        CancellationToken ct)
    {
        // Pre-merge autosave: turn any dirty `.fleece/` working-tree state into a real
        // commit so `git merge` becomes a true three-way merge instead of silently
        // overwriting WT lines that exist nowhere in committed history.
        var autosaveError = await AutosaveFleeceChangesAsync(projectPath);
        if (autosaveError is not null)
        {
            return new FleecePullResult(
                Success: false,
                ErrorMessage: autosaveError,
                IssuesMerged: 0,
                WasBehindRemote: branchStatus.IsBehindRemote,
                CommitsPulled: 0);
        }

        var mergeResult = await commandRunner.RunAsync("git", $"merge --no-edit origin/{defaultBranch}", projectPath);
        if (!mergeResult.Success)
        {
            logger.LogWarning("git merge of origin/{Branch} failed: {Error}", defaultBranch, mergeResult.Error);
            await commandRunner.RunAsync("git", "merge --abort", projectPath);
            return new FleecePullResult(
                Success: false,
                ErrorMessage: $"Failed to merge origin/{defaultBranch}: {mergeResult.Error}",
                IssuesMerged: 0,
                WasBehindRemote: branchStatus.IsBehindRemote,
                CommitsPulled: 0);
        }
        logger.LogInformation("Merged origin/{Branch} cleanly", defaultBranch);

        var compactionWarning = await TryCompactAsync(projectPath, defaultBranch);

        return new FleecePullResult(
            Success: true,
            ErrorMessage: null,
            IssuesMerged: 0,
            WasBehindRemote: true,
            CommitsPulled: branchStatus.CommitsBehind,
            CompactionWarning: compactionWarning);
    }

    public async Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Starting fleece issues sync for {ProjectPath} to branch {Branch}", projectPath, defaultBranch);

        var branchStatus = await CheckBranchStatusAsync(projectPath, defaultBranch, ct);
        if (!branchStatus.Success || !branchStatus.IsOnCorrectBranch)
        {
            return new FleeceIssueSyncResult(false, branchStatus.ErrorMessage, 0, false);
        }

        var nonFleeceChanges = await GetNonFleeceChangesAsync(projectPath);
        if (nonFleeceChanges.Count > 0)
        {
            logger.LogWarning("Found {Count} non-fleece changed files that block sync", nonFleeceChanges.Count);
            return new FleeceIssueSyncResult(
                Success: false,
                ErrorMessage: $"Cannot sync: found {nonFleeceChanges.Count} uncommitted non-fleece file(s). Please commit or discard these changes first.",
                FilesCommitted: 0,
                PushSucceeded: false,
                HasNonFleeceChanges: true,
                NonFleeceChangedFiles: nonFleeceChanges);
        }

        if (branchStatus.IsBehindRemote)
        {
            var pullResult = await PullAndMergeFleeceInternalAsync(projectPath, defaultBranch, branchStatus, ct);
            if (!pullResult.Success)
            {
                return new FleeceIssueSyncResult(
                    Success: false,
                    ErrorMessage: pullResult.ErrorMessage,
                    FilesCommitted: 0,
                    PushSucceeded: false,
                    RequiresPullFirst: false);
            }
        }

        // Commit any staged .fleece/ changes the pre-commit hook will have collected.
        int filesCount = 0;
        string? compactionWarning = null;

        var fleeceStatus = await commandRunner.RunAsync("git", "status --porcelain .fleece/", projectPath);
        if (fleeceStatus.Success && !string.IsNullOrWhiteSpace(fleeceStatus.Output))
        {
            filesCount = CountChangedFiles(fleeceStatus.Output);
            logger.LogInformation("Committing {Count} .fleece/ file changes", filesCount);

            var addResult = await commandRunner.RunAsync("git", "add .fleece/", projectPath);
            if (!addResult.Success)
            {
                return new FleeceIssueSyncResult(false, $"Failed to stage files: {addResult.Error}", 0, false);
            }

            var commitResult = await commandRunner.RunAsync("git", "commit -m \"Update fleece issues [skip ci]\"", projectPath);
            if (!commitResult.Success
                && !commitResult.Output.Contains("nothing to commit")
                && !commitResult.Error.Contains("nothing to commit"))
            {
                return new FleeceIssueSyncResult(false, $"Failed to commit: {commitResult.Error}", 0, false);
            }
        }

        // Compact on the default branch (we already validated `isOnCorrectBranch` above).
        compactionWarning = await TryCompactAsync(projectPath, defaultBranch);

        // If `fleece project` rewrote the snapshot, commit it.
        var postCompactionStatus = await commandRunner.RunAsync("git", "status --porcelain .fleece/", projectPath);
        if (postCompactionStatus.Success && !string.IsNullOrWhiteSpace(postCompactionStatus.Output))
        {
            await commandRunner.RunAsync("git", "add .fleece/", projectPath);
            var compactCommit = await commandRunner.RunAsync("git", "commit -m \"chore(fleece): compact event log [skip ci]\"", projectPath);
            if (!compactCommit.Success
                && !compactCommit.Output.Contains("nothing to commit")
                && !compactCommit.Error.Contains("nothing to commit"))
            {
                logger.LogWarning("Failed to commit compaction: {Error}", compactCommit.Error);
            }
        }

        var revListCheck = await commandRunner.RunAsync("git", $"rev-list --left-right --count origin/{defaultBranch}...HEAD", projectPath);
        var needsToPush = false;
        if (revListCheck.Success)
        {
            var parts = revListCheck.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var ahead) && ahead > 0)
            {
                needsToPush = true;
            }
        }

        if (!needsToPush)
        {
            logger.LogInformation("No changes to push, sync complete");
            return new FleeceIssueSyncResult(true, null, filesCount, true, CompactionWarning: compactionWarning);
        }

        var pushResult = await commandRunner.RunAsync("git", $"push origin {defaultBranch}", projectPath);
        if (!pushResult.Success)
        {
            if (pushResult.Error.Contains("non-fast-forward") || pushResult.Error.Contains("rejected"))
            {
                return new FleeceIssueSyncResult(
                    Success: false,
                    ErrorMessage: "Push was rejected because the remote has new changes. Please try syncing again.",
                    FilesCommitted: filesCount,
                    PushSucceeded: false,
                    RequiresPullFirst: true);
            }

            return new FleeceIssueSyncResult(false, $"Failed to push: {pushResult.Error}", filesCount, false);
        }

        logger.LogInformation("Successfully pushed fleece issues sync");
        return new FleeceIssueSyncResult(true, null, filesCount, true, CompactionWarning: compactionWarning);
    }

    /// <summary>
    /// Stage and commit any uncommitted working-tree changes under <c>.fleece/</c>
    /// as a synthetic <c>chore(fleece): pre-pull autosave [skip ci]</c> commit. Restricted
    /// to <c>.fleece/</c> paths only — never touches the non-fleece working tree, so the
    /// existing "non-fleece changes block sync" policy is unaffected. Returns
    /// <c>null</c> on success (including the no-op case when nothing under
    /// <c>.fleece/</c> is dirty) or an error message describing why the autosave commit
    /// failed.
    /// </summary>
    private async Task<string?> AutosaveFleeceChangesAsync(string projectPath)
    {
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain .fleece/", projectPath);
        if (!statusResult.Success)
        {
            return $"Failed to inspect .fleece/ status before pull: {statusResult.Error}";
        }
        if (string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return null;
        }

        var fileCount = CountChangedFiles(statusResult.Output);

        var addResult = await commandRunner.RunAsync("git", "add .fleece/", projectPath);
        if (!addResult.Success)
        {
            return $"Failed to stage .fleece/ for pre-pull autosave: {addResult.Error}";
        }

        var commitResult = await commandRunner.RunAsync(
            "git",
            "commit -m \"chore(fleece): pre-pull autosave [skip ci]\"",
            projectPath);
        if (!commitResult.Success
            && !commitResult.Output.Contains("nothing to commit")
            && !commitResult.Error.Contains("nothing to commit"))
        {
            return $"Failed to create pre-pull autosave commit: {commitResult.Error}";
        }

        logger.LogInformation("Pre-pull autosave: committed {Count} .fleece/ file change(s)", fileCount);
        return null;
    }

    /// <summary>
    /// Shell-out to <c>fleece project</c> to compact event files into the snapshot.
    /// Non-zero exit becomes a soft warning, not a hard failure — the snapshot is
    /// still correct, just un-compacted, and the next sync will retry compaction.
    /// </summary>
    private async Task<string?> TryCompactAsync(string projectPath, string defaultBranch)
    {
        logger.LogInformation("Running `fleece project` on {ProjectPath} (branch={Branch})", projectPath, defaultBranch);
        var projectionResult = await commandRunner.RunAsync("fleece", "project", projectPath);
        if (!projectionResult.Success)
        {
            var warning = $"`fleece project` failed (exit={projectionResult.Error}); snapshot left un-compacted";
            logger.LogWarning("{Warning}", warning);
            return warning;
        }

        logger.LogInformation("`fleece project` succeeded");
        return null;
    }

    public async Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Pulling changes from {Branch} for {ProjectPath}", defaultBranch, projectPath);

        var nonFleeceChanges = await GetNonFleeceChangesAsync(projectPath);

        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", projectPath);
        if (!fetchResult.Success)
        {
            return new PullResult(false, false, $"Failed to fetch: {fetchResult.Error}");
        }

        var gitMergeResult = await commandRunner.RunAsync("git", $"merge origin/{defaultBranch} --no-edit", projectPath);
        if (gitMergeResult.Success)
        {
            return new PullResult(true, false, null);
        }

        var hasConflicts = DetectConflict(gitMergeResult.Error, gitMergeResult.Output);
        await commandRunner.RunAsync("git", "merge --abort", projectPath);

        return new PullResult(
            Success: false,
            HasConflicts: hasConflicts,
            ErrorMessage: gitMergeResult.Error,
            HasNonFleeceChanges: nonFleeceChanges.Count > 0,
            NonFleeceChangedFiles: nonFleeceChanges.Count > 0 ? nonFleeceChanges : null);
    }

    public async Task<bool> StashChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Stashing changes for {ProjectPath}", projectPath);

        var result = await commandRunner.RunAsync("git", "stash push -m \"fleece-sync-auto-stash\"", projectPath);
        if (!result.Success)
        {
            logger.LogWarning("Failed to stash: {Error}", result.Error);
            return false;
        }

        return true;
    }

    public async Task<bool> DiscardChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Discarding changes for {ProjectPath}", projectPath);

        await commandRunner.RunAsync("git", "rebase --abort", projectPath);

        var resetResult = await commandRunner.RunAsync("git", "reset HEAD", projectPath);
        if (!resetResult.Success)
        {
            logger.LogWarning("Failed to reset: {Error}", resetResult.Error);
        }

        var checkoutResult = await commandRunner.RunAsync("git", "checkout -- .", projectPath);
        if (!checkoutResult.Success)
        {
            logger.LogWarning("Failed to checkout: {Error}", checkoutResult.Error);
            return false;
        }

        var cleanResult = await commandRunner.RunAsync("git", "clean -fd", projectPath);
        if (!cleanResult.Success)
        {
            logger.LogWarning("Failed to clean: {Error}", cleanResult.Error);
        }

        return true;
    }

    public async Task<bool> DiscardNonFleeceChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Discarding non-fleece changes for {ProjectPath}", projectPath);

        await commandRunner.RunAsync("git", "rebase --abort", projectPath);

        var changedFiles = await GetNonFleeceChangesAsync(projectPath);

        if (changedFiles.Count == 0)
        {
            return true;
        }

        foreach (var file in changedFiles)
        {
            var restoreResult = await commandRunner.RunAsync("git", $"checkout -- \"{file}\"", projectPath);
            if (!restoreResult.Success)
            {
                var cleanResult = await commandRunner.RunAsync("git", $"clean -f -- \"{file}\"", projectPath);
                if (!cleanResult.Success)
                {
                    logger.LogWarning("Failed to discard file {File}: checkout error: {Error1}, clean error: {Error2}",
                        file, restoreResult.Error, cleanResult.Error);
                }
            }
        }

        var cleanAllResult = await commandRunner.RunAsync("git", "clean -fd --exclude=.fleece/", projectPath);
        if (!cleanAllResult.Success)
        {
            logger.LogWarning("Failed to clean untracked files: {Error}", cleanAllResult.Error);
        }

        return true;
    }

    private async Task<IReadOnlyList<string>> GetNonFleeceChangesAsync(string projectPath)
    {
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain", projectPath);
        if (!statusResult.Success || string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return Array.Empty<string>();
        }

        var nonFleeceFiles = new List<string>();
        var lines = statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            var filename = line[3..].Trim();

            if (filename.Contains(" -> "))
            {
                var parts = filename.Split(" -> ");
                filename = parts[^1];
            }

            if (!filename.StartsWith(".fleece/", StringComparison.OrdinalIgnoreCase) &&
                !filename.Equals(".fleece", StringComparison.OrdinalIgnoreCase))
            {
                nonFleeceFiles.Add(filename);
            }
        }

        return nonFleeceFiles;
    }

    private static int CountChangedFiles(string statusOutput)
    {
        if (string.IsNullOrWhiteSpace(statusOutput))
            return 0;
        return statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool DetectConflict(string error, string output)
    {
        var combined = $"{error} {output}".ToLowerInvariant();
        return combined.Contains("conflict")
               || combined.Contains("would be overwritten")
               || combined.Contains("uncommitted changes")
               || combined.Contains("please commit or stash")
               || combined.Contains("cannot pull with rebase")
               || combined.Contains("merge conflict");
    }
}
