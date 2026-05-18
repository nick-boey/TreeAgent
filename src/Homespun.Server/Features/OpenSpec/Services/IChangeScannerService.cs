using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Scans a clone's <c>openspec/changes/</c> directory, matching changes to Fleece issues
/// via <c>openspec=&lt;change-name&gt;</c> tags on the per-clone Fleece projection.
/// </summary>
public interface IChangeScannerService
{
    /// <summary>
    /// Scans the clone for changes linked to Fleece issues via <c>openspec=</c> tags.
    /// </summary>
    /// <param name="clonePath">
    /// Absolute path to the clone root (the directory containing <c>openspec/</c>).
    /// </param>
    /// <param name="branchFleeceId">The fleece-id suffix parsed from the branch name (kept for telemetry).</param>
    /// <param name="baseBranch">Unused; kept for call-site compatibility.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<BranchScanResult> ScanBranchAsync(
        string clonePath,
        string branchFleeceId,
        string? baseBranch = null,
        CancellationToken ct = default);

    /// <summary>
    /// Shells out to <c>openspec status --change &lt;name&gt; --json</c> in the given clone.
    /// Returns null when the CLI fails or the output cannot be parsed.
    /// </summary>
    Task<ChangeArtifactState?> GetArtifactStateAsync(
        string clonePath,
        string changeName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads and parses <c>tasks.md</c> under the change directory. Returns
    /// <see cref="TaskStateSummary.Empty"/> when no tasks.md exists.
    /// </summary>
    Task<TaskStateSummary> ParseTasksAsync(
        string changeDirectory,
        CancellationToken ct = default);
}
