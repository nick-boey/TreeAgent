namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// The output of scanning a branch clone's <c>openspec/changes/</c> directory for change state.
/// </summary>
public class BranchScanResult
{
    /// <summary>
    /// The branch's fleece-id suffix (parsed from the branch name), kept for telemetry.
    /// </summary>
    public required string BranchFleeceId { get; init; }

    /// <summary>
    /// Changes linked to a Fleece issue via an <c>openspec=&lt;name&gt;</c> tag.
    /// Includes both live changes (<c>openspec/changes/&lt;name&gt;/</c>) and archived ones
    /// (<c>openspec/changes/archive/&lt;dated&gt;-&lt;name&gt;/</c>).
    /// </summary>
    public List<LinkedChangeInfo> LinkedChanges { get; init; } = new();

    /// <summary>
    /// Always empty — orphan classification has been removed.
    /// Retained for call-site compatibility.
    /// </summary>
    public List<OrphanChangeInfo> OrphanChanges { get; init; } = new();

    /// <summary>
    /// Always empty — inherited-change classification has been removed.
    /// Retained for call-site compatibility.
    /// </summary>
    public List<string> InheritedChangeNames { get; init; } = new();
}

/// <summary>
/// A change linked to a Fleece issue via an <c>openspec=</c> tag.
/// </summary>
public class LinkedChangeInfo
{
    /// <summary>
    /// The change name (the directory name under <c>openspec/changes/</c>, with any
    /// archive date prefix removed when <see cref="IsArchived"/> is true).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Absolute path to the change directory.
    /// </summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Origin label for diagnostic purposes.
    /// </summary>
    public required string CreatedBy { get; init; }

    /// <summary>
    /// True when the change has been archived (lives under <c>openspec/changes/archive/</c>).
    /// </summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// For archived changes, the dated archive folder name (e.g. <c>2026-04-16-my-change</c>).
    /// Null for live changes.
    /// </summary>
    public string? ArchivedFolderName { get; init; }

    /// <summary>
    /// Artifact state from <c>openspec status --change &lt;name&gt; --json</c>.
    /// Null when the CLI could not be invoked successfully (e.g. archived changes).
    /// </summary>
    public ChangeArtifactState? ArtifactState { get; init; }

    /// <summary>
    /// Parsed state of <c>tasks.md</c>. <see cref="TaskStateSummary.Empty"/> when no tasks file exists.
    /// </summary>
    public TaskStateSummary TaskState { get; init; } = TaskStateSummary.Empty;
}

/// <summary>
/// Placeholder — orphan classification has been removed; this type is retained
/// for call-site compatibility only and will not be populated.
/// </summary>
public class OrphanChangeInfo
{
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public bool CreatedOnBranch { get; init; }
}
