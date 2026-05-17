namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Validates that all phases prior to the requested phase are complete
/// before a phase-specific dispatch is allowed.
/// </summary>
public interface IPhaseDispatchGuard
{
    /// <summary>
    /// Returns names of phases that must be completed before <paramref name="phaseName"/>
    /// can be dispatched. An empty list means the dispatch is allowed.
    /// Returns an empty list when <c>tasks.md</c> cannot be found or the phase is not listed.
    /// </summary>
    Task<IReadOnlyList<string>> GetBlockingPhasesAsync(
        string clonePath,
        string changeName,
        string phaseName,
        CancellationToken ct = default);
}
