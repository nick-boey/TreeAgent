using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Enriches a <see cref="TaskGraphResponse"/> with OpenSpec state for each issue.
/// </summary>
public interface IIssueGraphOpenSpecEnricher
{
    /// <summary>
    /// Populates <c>OpenSpecStates</c> on the given response.
    /// Swallows errors per-issue and logs at debug level — the graph must render regardless.
    ///
    /// <paramref name="branchContext"/> is optional — when omitted the enricher
    /// builds a one-off context, paying a single <see cref="IGitCloneService.ListClonesAsync"/>
    /// + a single <see cref="IDataStore.GetPullRequestsByProject"/> instead of
    /// re-scanning per visible node.
    /// </summary>
    Task EnrichAsync(
        string projectId,
        TaskGraphResponse response,
        BranchResolutionContext? branchContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the per-issue OpenSpec state map for <paramref name="issueIds"/>.
    /// Issues whose branch is missing or whose change has no scan result are mapped
    /// to <c>BranchPresence.None</c>; per-issue scan errors are swallowed and logged.
    /// </summary>
    Task<Dictionary<string, IssueOpenSpecState>> GetOpenSpecStatesAsync(
        string projectId,
        IReadOnlyCollection<string> issueIds,
        BranchResolutionContext? branchContext = null,
        CancellationToken ct = default);
}
