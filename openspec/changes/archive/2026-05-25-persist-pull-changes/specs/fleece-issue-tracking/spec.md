## ADDED Requirements

### Requirement: Clone bootstrap preserves per-session change-file invariant

When `IGitCloneService.CreateCloneAsync` creates a new clone, it SHALL ensure the clone's Fleece state starts a fresh per-session event log rather than inheriting main's open session. Specifically:

1. The clone's `.fleece/` SHALL NOT contain `.active-change` or `.replay-cache` copied from the source repo. Both files are per-clone gitignored runtime state and SHALL be excluded from any directory copy performed during clone creation.
2. After all `.fleece/` contents have been copied from the source repo to the clone, the service SHALL bootstrap a fresh per-session change file in the clone: a new `.fleece/changes/change_<newguid>.jsonl` whose first line is `{"kind":"meta","follows":"<latest-existing-change-id>"}`, plus `.fleece/.active-change` containing the JSON object `{"guid":"<newguid>"}` (the `.active-change` file format is a JSON object with a `guid` field — not a bare hex string).
3. `<newguid>` SHALL be a fresh 32-character lowercase hex GUID with no dashes (matching `Guid.NewGuid().ToString("N")`).
4. `<latest-existing-change-id>` SHALL be the id of the `change_<id>.jsonl` file in the clone's `.fleece/changes/` with the most recent file modification time after the copy completes. If the directory is empty, the `follows` field SHALL be omitted from the meta entry. If the latest existing change file's id has subsequently been compacted away by `fleece project` on the source repo, Fleece tolerates the dangling pointer; no fallback is required.

The bootstrap SHALL occur after `CopyFleeceChangesAsync` and before `fleece install`, so that `fleece install` sees a valid `.active-change` and the agent's first edit appends to the new session-scoped file.

#### Scenario: Clone does not inherit .active-change from main
- **GIVEN** main's `.fleece/.active-change` contains `{"guid":"aaaaaaaa..."}` for some hex value
- **WHEN** `IGitCloneService.CreateCloneAsync` creates a clone
- **THEN** the clone's `.fleece/.active-change` SHALL NOT contain `aaaaaaaa...`
- **AND** the clone's `.fleece/.active-change` SHALL parse as a JSON object whose `guid` field is the bootstrapped fresh GUID

#### Scenario: Clone does not inherit .replay-cache from main
- **GIVEN** main's `.fleece/.replay-cache` exists with any contents
- **WHEN** `IGitCloneService.CreateCloneAsync` creates a clone
- **THEN** the clone's `.fleece/.replay-cache` SHALL NOT exist
- **AND** Fleece SHALL regenerate the cache on first read in the clone

#### Scenario: Clone bootstraps a fresh change file with meta.follows pointing at the most-recent existing change
- **GIVEN** main has `.fleece/changes/change_AAAA.jsonl` (older mtime) and `.fleece/changes/change_BBBB.jsonl` (newer mtime)
- **WHEN** a clone is created
- **THEN** the clone's `.fleece/changes/` SHALL contain a new `change_<newguid>.jsonl`
- **AND** the first line of that new file SHALL parse as `{"kind":"meta","follows":"BBBB"}`
- **AND** the clone's `.fleece/.active-change` SHALL parse as `{"guid":"<newguid>"}`

#### Scenario: Clone bootstrap omits follows when changes directory is empty
- **GIVEN** main has an empty `.fleece/changes/` directory
- **WHEN** a clone is created
- **THEN** the clone's bootstrapped `change_<newguid>.jsonl` first line SHALL parse as `{"kind":"meta"}`
- **AND** SHALL NOT contain a `follows` field

#### Scenario: Two clones created from the same main both bootstrap fresh, distinct change files
- **GIVEN** main has `.fleece/changes/change_AAAA.jsonl` as its only existing change
- **WHEN** two clones (X and Y) are created from main in any order
- **THEN** each clone SHALL have its own `change_<guid>.jsonl` with a unique GUID
- **AND** both clones' meta entries SHALL have `follows: "AAAA"` (multiple children of the same DAG node are valid)

### Requirement: Pull auto-commits uncommitted fleece events before merge

Before `FleeceIssuesSyncService.PullAndMergeFleeceInternalAsync` invokes `git merge --no-edit origin/<default>`, the service SHALL detect uncommitted working-tree changes scoped to `.fleece/` and commit them locally with a synthetic autosave commit. This ensures the subsequent merge is a real three-way merge — git surfaces conflicts to the user rather than silently overwriting working-tree lines that exist nowhere in committed history.

The autosave SHALL be scoped to `.fleece/` paths only — it SHALL NOT stage or commit any non-`.fleece/` working-tree files. The existing "non-fleece changes block sync" policy SHALL remain unchanged.

The autosave commit message SHALL be `chore(fleece): pre-pull autosave [skip ci]` so CI does not run on the synthetic commit.

#### Scenario: Pull with no uncommitted fleece changes performs no autosave
- **GIVEN** the working tree has no uncommitted changes under `.fleece/`
- **WHEN** `PullFleeceOnlyAsync` runs
- **THEN** no autosave commit SHALL be created
- **AND** the merge SHALL proceed as today

#### Scenario: Pull with uncommitted fleece changes commits them before merging
- **GIVEN** the working tree has uncommitted changes to one or more files under `.fleece/`
- **AND** no non-`.fleece/` working-tree files are dirty
- **WHEN** `PullFleeceOnlyAsync` runs
- **THEN** the service SHALL `git add .fleece/` and `git commit -m "chore(fleece): pre-pull autosave [skip ci]"` before merging
- **AND** the subsequent `git merge --no-edit origin/<default>` SHALL run against a clean working tree

#### Scenario: Pull autosave does not touch non-fleece files
- **GIVEN** the working tree has uncommitted changes to both `.fleece/changes/change_AAAA.jsonl` and `README.md`
- **WHEN** `PullFleeceOnlyAsync` runs
- **THEN** the autosave commit SHALL include only `.fleece/changes/change_AAAA.jsonl`
- **AND** the existing non-fleece-changes reporting (`HasNonFleeceChanges` / `NonFleeceChangedFiles`) SHALL behave unchanged

#### Scenario: Uncommitted user edit survives pull of a merged clone's PR
- **GIVEN** main has uncommitted event `e_user` in its working tree (in some `change_<guid>.jsonl`)
- **AND** the remote default branch has new commits from a merged clone's PR that include `change_<otherguid>.jsonl` (a different file by virtue of the clone-bootstrap fix)
- **WHEN** `PullFleeceOnlyAsync` runs
- **THEN** `e_user` SHALL be present in main's git history after the pull (via the autosave commit)
- **AND** the merged clone's events SHALL also be present (via the merge)
- **AND** after `fleece project` compaction the resulting `issues.jsonl` SHALL include the field-level last-writer-wins result of both edit streams

#### Scenario: Pull autosave commit is idempotent across consecutive pulls
- **GIVEN** an autosave commit has just been created and the merge succeeded
- **WHEN** `PullFleeceOnlyAsync` runs again immediately with no further edits
- **THEN** no new autosave commit SHALL be created (the working tree is clean)
