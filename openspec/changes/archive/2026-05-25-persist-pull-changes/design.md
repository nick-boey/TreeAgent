## Context

`GitCloneService.CreateCloneAsync` and `FleeceIssuesSyncService.PullAndMergeFleeceInternalAsync` are the two surfaces involved in the data-loss bug described in the proposal. The relevant code:

- `src/Homespun.Server/Features/Git/GitCloneService.cs:121-139` — calls `CopyFleeceChangesAsync` after `git clone --local`, then runs `fleece install`.
- `src/Homespun.Server/Features/Git/GitCloneService.cs:768-795` — `CopyFleeceChangesAsync` deletes the clone's `.fleece/` and copies main's full `.fleece/` via `CopyDirectory(..., name => name == ".git")`. Only the `.git` directory is excluded.
- `src/Homespun.Server/Features/Fleece/Services/FleeceIssuesSyncService.cs:155-184` — `PullAndMergeFleeceInternalAsync` runs `git merge --no-edit origin/<default>` directly. No pre-merge handling of uncommitted `.fleece/changes/*`.

The Fleece 3.1 event-sourced design relies on a single contract: **each session writes to its own `change_<guid>.jsonl` file with a `meta.follows` pointer to the previous session's id**. That contract is what makes "divergent edits merge cleanly through event files" (existing spec scenario) actually true. The clone path currently breaks the contract by propagating `.active-change` — both repos end up writing to the same file path.

Empirical verification (already done during exploration): `fleece install` does not touch `.active-change` or `.replay-cache`. The clone path is the sole owner of these files in the clone's working tree.

## Goals / Non-Goals

**Goals:**

- Restore the GUID-per-session invariant on clones so the existing "divergent edits merge cleanly" guarantee is actually enforced.
- Ensure no path through `PullFleeceOnlyAsync` / `SyncAsync` can silently overwrite uncommitted `.fleece/changes/*` lines in the working tree.
- Add integration tests against real temp git repos that would have caught this bug.

**Non-Goals:**

- Migrating already-existing clones to a "rotated" active change. Existing clones keep their inherited pointer; the fix applies forward-only. Operationally users can recreate clones if they want the fix retroactively.
- Changing Fleece.Core itself. Stay on 3.1.0 — this is a Homespun-side adherence fix.
- Reworking the `discard-non-fleece-and-pull` endpoint. It already preserves `.fleece/` and its semantics remain correct after this change.
- Adding any UI surface for the pre-pull autosave commit; it's invisible to users by design (a synthetic commit that exists only for git's three-way merge to function correctly).
- Replacing `git merge --no-edit` with a rebase or custom merge driver. The fix is composable with the existing merge strategy.

## Decisions

### Decision 1: Exclude `.active-change` and `.replay-cache` from clone copy

**Choice**: Extend the `CopyDirectory` exclusion predicate in `CopyFleeceChangesAsync` to also exclude `.active-change` and `.replay-cache` by exact filename.

**Alternatives considered:**
- *Whitelist instead of blacklist* (copy only `issues.jsonl`, `tombstones.jsonl`, `changes/`, `workflows/`). Safer against future Fleece additions of new per-clone files, but brittle if Fleece introduces a new file we'd actually want copied. Rejected on cost/benefit grounds — the blacklist is one-line and the file set is small and stable.
- *Have `fleece install` clear these files*. Would put the fix in the wrong place (Fleece CLI doesn't know it's running in a fresh clone), and we'd need a coordinated release with `Fleece.Cli` 3.1.x. Rejected.

### Decision 2: Bootstrap a fresh `change_<newguid>.jsonl` in the clone explicitly

**Choice**: After `CopyFleeceChangesAsync` and before `fleece install`, `GitCloneService` writes a new `.fleece/changes/change_<newguid>.jsonl` whose first line is `{"kind":"meta","follows":"<latest-existing-change-id>"}` and writes `{"guid":"<newguid>"}` to `.fleece/.active-change` (the file format is a JSON object with a `guid` field — not a bare hex string; verified empirically against `Fleece.Cli` 3.1).

**Rationale**: Doing this explicitly (rather than letting the first agent write trigger Fleece's lazy bootstrap) gives us three guarantees:
1. The clone's session log is recoverable from the very first event — there's no window where `.active-change` is missing and a concurrent process could observe an inconsistent state.
2. The `meta.follows` DAG pointer is set deterministically by Homespun, using the latest change file id sorted by mtime (or by filename — the GUIDs are random, so mtime is more meaningful for DAG ordering).
3. The integration tests can assert the exact file shape rather than depending on Fleece CLI's bootstrap behavior.

**Determining the "latest existing change id":** scan `.fleece/changes/change_*.jsonl` in the clone after the copy and pick the file with the most recent mtime. If the changes directory is empty (e.g., fresh Fleece state), omit `follows` from the meta entry — that's a valid root.

**GUID format:** match the existing convention (32 lowercase hex characters, no dashes — `Guid.NewGuid().ToString("N")` in .NET).

**Alternatives considered:**
- *Delete the inherited `.active-change` and let Fleece self-bootstrap on first write*. Works in practice but couples our correctness to Fleece's lazy behavior. Rejected for testability and determinism.
- *Bootstrap at the moment the first agent session starts* (e.g., in `IClaudeSessionService`). Spreads the invariant across two services and creates a window where the clone exists with a stale pointer. Rejected.

### Decision 3: Pre-pull autosave commits uncommitted fleece events

**Choice**: In `PullAndMergeFleeceInternalAsync`, before invoking `git merge`, check `git status --porcelain .fleece/changes/` (and on the default branch, `.fleece/issues.jsonl` + `.fleece/tombstones.jsonl`). If any output, run `git add` + `git commit -m "chore(fleece): pre-pull autosave [skip ci]"` to make the local working state real, then merge.

**Why a commit and not a stash:**
- A real commit becomes part of git history and survives any subsequent recovery action (including `git merge --abort`, manual `git checkout`, or the existing `discard-non-fleece-and-pull` endpoint, which only touches non-`.fleece/` paths).
- Stash is process-local state that can be lost by a poorly-timed `git stash drop` or by the user clearing their stash through another tool. Commit is durable.
- On three-way merge with concurrent fleece edits in remote, git surfaces a real conflict to the user instead of silently overwriting — which is the explicit goal.

**Scope of the autosave:** restrict to `.fleece/` paths only. The existing sync flow refuses to proceed if non-fleece working-tree files are dirty (`SyncAsync` line 196-207). The pre-pull autosave does NOT extend that policy — it only touches files the pre-commit hook would have staged on a normal commit, so users never see a surprise commit of unrelated work.

**Idempotency:** the commit is a no-op when nothing under `.fleece/` is dirty. The code path checks before committing and skips cleanly.

**Conflict surfacing:** if the post-merge step still fails (because both sides modified the same change file, which can only happen for existing pre-fix clones with shared pointers — i.e., the "legacy" case), `git merge --abort` runs as today, and the user receives the same error as the current implementation. The autosave doesn't worsen the failure mode; it just ensures that when failure is avoidable (because the working tree is committed), it is in fact avoided.

**Alternatives considered:**
- *Stash-pop wrapper around the merge*. Simpler to implement but loses durability (see above).
- *Fail fast if `.fleece/changes/` is dirty, ask user to commit manually*. Worst UX — the whole point of Homespun is to abstract git from users.

### Decision 4: Leave `.replay-cache` regeneration to Fleece

**Choice**: Don't write a `.replay-cache` in the clone. Fleece regenerates it on first read.

**Rationale**: The cache is a derived artifact; carrying it from main doesn't save meaningful startup time and creates the risk identified during exploration (a malformed or stale cache causing parse errors at clone startup). Letting Fleece rebuild it is correct by construction.

### Decision 5: Tests run against real git, not mocks

**Choice**: New integration tests live in `tests/Homespun.Api.Tests` (or a new `tests/Homespun.Tests/Integration/` folder if `Api.Tests` is HTTP-only). They create a temp directory, run real `git init`, `git commit`, `git clone`, etc., and exercise `IGitCloneService` + `IFleeceIssuesSyncService` end-to-end.

**Rationale**: The bug lives entirely in the seam between filesystem state, fleece file conventions, and git's merge behavior. Every mockable layer in this path returned "success" today. Real-git tests are the only way to catch regressions of this shape. Cost: a few seconds per test for the git operations; acceptable.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| **Race on `meta.follows` selection:** if two clones are bootstrapped in parallel from the same main, both will pick the same "latest existing change id" as their follows pointer. This is actually fine — the DAG is a forest, multiple children of the same node is legal — but worth documenting. | Document in design (here) and in the spec scenario. No code-level mitigation needed. |
| **Pre-pull autosave creates noise in `git log`:** every pull with dirty fleece state produces a `chore(fleece): pre-pull autosave` commit. | (a) Use `[skip ci]` to avoid triggering CI. (b) Accept the noise — these commits are deletable post-hoc by the user if desired, and the alternative (silent data loss) is worse. (c) Future: consider squashing autosave commits into the next compaction commit on the default branch — out of scope here. |
| **Legacy clones still share `.active-change`:** clones created before this fix continue to have inherited pointers. | Forward-only fix is documented in the proposal as a non-goal. Users can rebuild clones via existing UI. No code-level fallback needed. |
| **Tests against real git slow CI:** end-to-end git/fs tests add seconds-per-test overhead. | (a) Keep the new tests to a minimal set (5-10 scenarios, see specs). (b) These tests live alongside existing `Api.Tests` integration tests; CI already accommodates that pattern. |
| **`meta.follows` to an old change-id whose file has been compacted away:** `fleece project` deletes change files after folding them into the snapshot. A clone's `meta.follows` pointer might reference a deleted id. | **Resolved by spike**: `Fleece.Cli` 3.1 tolerates dangling `follows` pointers. Spike test: synthesized a `change_<new>.jsonl` whose `meta.follows` referenced a compacted-away id, ran `fleece list` and `fleece edit`, both succeeded with no errors. Projection is the truth; the DAG pointer is advisory for ordering and is ignored when the target is missing. No fallback rule required. |

## Migration Plan

Forward-only, no data migration. Deploy the code; new clones inherit the new behavior. The pre-pull autosave is a no-op when state is clean, so existing workflows that already commit fleece changes before pulling are unaffected.

**Rollback:** revert the two changed files. Existing clones built with the fix continue to function — they have a valid GUID-per-session change file, which is just stricter adherence to the contract Fleece.Core already documents. No state is left in an unreadable form.

## Open Questions

1. ~~Does Fleece.Core tolerate a `meta.follows` pointer to a change-id that has been compacted away?~~ **Resolved by spike against `Fleece.Cli` 3.1: yes, dangling `follows` pointers are tolerated silently.** Implementation can proceed with the "latest existing change-id by mtime" rule without a fallback.
2. **Should the pre-pull autosave commit also auto-amend on the next normal commit, to keep history clean?** Tabling for a follow-up — not in scope.
