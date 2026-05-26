## Why

When a user edits issues on the main repo and then creates an agent clone whose PR is later merged, the user's uncommitted Fleece events can be silently lost on `git pull`. The root cause is that `GitCloneService.CreateCloneAsync` copies the entire `.fleece/` directory — including the gitignored, per-clone runtime state `.active-change` and `.replay-cache` — so the clone inherits main's "currently open" change file and both repos start appending events to the same `change_<guid>.jsonl`. When the clone's branch is merged back, git's text-level reconciliation can drop lines that exist only in main's working tree. The Fleece 3.1 event-sourced design assumes one session = one change file with a fresh GUID; Homespun's clone path silently violates that invariant.

A secondary issue compounds the loss: `FleeceIssuesSyncService.PullAndMergeFleeceInternalAsync` runs `git merge --no-edit` against uncommitted `.fleece/changes/*` in the working tree, which can either refuse (forcing users into manual recovery that discards the events) or, in some prefix-compatible cases, overwrite WT lines with the merged tree.

## What Changes

- **Clone creation excludes per-clone runtime state from copy.** `GitCloneService.CopyFleeceChangesAsync` no longer copies `.fleece/.active-change` or `.fleece/.replay-cache` from main into a new clone.
- **Clone creation bootstraps a fresh per-session change file.** Immediately after the copy, the service writes a new `.fleece/changes/change_<newguid>.jsonl` containing a single `{"kind":"meta","follows":"<latest-existing-change-id>"}` line, and writes `<newguid>` to `.fleece/.active-change`. The clone's agent session therefore starts a session-scoped event log from the first edit, preserving the GUID-per-session invariant.
- **Pull auto-commits uncommitted fleece events first.** Before `git merge --no-edit origin/<default>`, `FleeceIssuesSyncService` checks for uncommitted entries under `.fleece/changes/` (and `.fleece/issues.jsonl` / `.fleece/tombstones.jsonl` on the default branch). If present, it commits them locally with message `chore(fleece): pre-pull autosave [skip ci]` so the subsequent merge becomes a real three-way merge — conflicts surface to the user instead of silently overwriting the working tree.
- **Tests cover the regression surface.** New `Api.Tests`-style integration tests drive `IGitCloneService` and `IFleeceIssuesSyncService` against real temp git repos (no mocking of `git`) to assert the GUID-per-session invariant on clones and event preservation across pull/merge.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `fleece-issue-tracking`: adds requirements covering (a) the GUID-per-session invariant on clone creation, (b) exclusion of `.fleece/.active-change` and `.fleece/.replay-cache` from clone copies, and (c) pre-pull autosave of uncommitted fleece events in the sync flow.

## Impact

**Code**

- `src/Homespun.Server/Features/Git/GitCloneService.cs` — extend the `CopyDirectory` exclusion predicate in `CopyFleeceChangesAsync` and add a new private method to bootstrap the clone's active change file with a `meta.follows` pointer.
- `src/Homespun.Server/Features/Fleece/Services/FleeceIssuesSyncService.cs` — add a pre-merge autosave step in `PullAndMergeFleeceInternalAsync` (used by `PullFleeceOnlyAsync` and `SyncAsync`).

**Tests**

- New integration tests in `tests/Homespun.Api.Tests` exercising clone + parallel-edit + merge + pull sequences against real temp git repositories.

**External dependencies**

- None. Fleece.Core stays at 3.1.0 — this change is entirely about Homespun's adherence to the existing per-session-change-file contract.

**Operational**

- No data migration needed. Existing clones already have inherited `.active-change` pointers; they will continue to work as-is, and any future commits from them will keep writing to those (now-shared) files. The fix only affects clones created after deployment. Optionally, a one-off "rotate active change" on existing clones could be performed by the user via the Homespun UI if added later (out of scope for this change).

**Linkage**

- Fleece issue `7GLQuk` ("Ensure changes persist when new changes are pulled") will be tagged `openspec=persist-pull-changes` on linkage.
