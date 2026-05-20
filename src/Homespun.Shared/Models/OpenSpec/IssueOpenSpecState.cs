namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// Branch existence state for an issue row in the graph.
/// </summary>
public enum BranchPresence
{
    /// <summary>No clone/branch exists for this issue.</summary>
    None,
    /// <summary>A branch/clone exists but has no linked OpenSpec change.</summary>
    Exists,
    /// <summary>A branch/clone exists and has a linked OpenSpec change.</summary>
    WithChange
}

/// <summary>
/// Lifecycle state of an OpenSpec change mapped to the graph's five-state indicator.
/// </summary>
public enum ChangePhase
{
    /// <summary>No change linked to the issue.</summary>
    None,
    /// <summary>Change exists, required artifacts incomplete.</summary>
    Incomplete,
    /// <summary>All required artifacts exist; ready for <c>openspec-apply-change</c>.</summary>
    ReadyToApply,
    /// <summary>All tasks checked; ready for <c>openspec-archive-change</c>.</summary>
    ReadyToArchive,
    /// <summary>Change has been archived.</summary>
    Archived
}

/// <summary>
/// Per-issue OpenSpec state projection rendered alongside the issue graph.
/// </summary>
public class IssueOpenSpecState
{
    public required BranchPresence BranchState { get; init; }
    public required ChangePhase ChangeState { get; init; }

    /// <summary>
    /// The linked change name, if any. Null when <see cref="ChangeState"/> is <see cref="ChangePhase.None"/>.
    /// </summary>
    public string? ChangeName { get; init; }

    /// <summary>
    /// Active OpenSpec schema name (e.g. <c>spec-driven</c>) on the linked change's branch.
    /// Null when no change is linked or the schema could not be determined.
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Per-phase roll-up parsed from tasks.md (only populated for linked changes).
    /// </summary>
    public List<PhaseSummary> Phases { get; init; } = new();
}

/// <summary>
/// Compact phase summary surfaced to the graph UI. Leaf tasks are included so
/// the phase detail modal can render them without a second round-trip.
/// </summary>
public class PhaseSummary
{
    public required string Name { get; init; }
    public int Done { get; init; }
    public int Total { get; init; }

    /// <summary>
    /// Per-task rollup for the phase detail modal.
    /// </summary>
    public List<PhaseTaskSummary> Tasks { get; init; } = new();
}

/// <summary>
/// Individual tasks.md checkbox surfaced in the phase detail modal.
/// </summary>
public class PhaseTaskSummary
{
    public required string Description { get; init; }
    public bool Done { get; init; }
}
