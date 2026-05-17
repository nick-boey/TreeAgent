## Context

`Fleece.Core` 3.0.0 stored issues as one-or-more `.fleece/issues_{hash}.jsonl` files where the hash was a content-derived id. Two clones editing the same project produced different filenames, so two-sided git merges of `.fleece/` were unsafe and we hand-rolled a stash-pull-pop-and-merge pipeline backed by a bespoke field-level `IssueMerger` (last-writer-wins). Four code paths in our codebase wrote raw `issues_{hash}.jsonl` files directly through a `FleeceFileHelper` helper, bypassing `IFleeceService`: `FleeceIssuesSyncService`, `FleeceChangeApplicationService.ApplyChangesViaFileMergeAsync`, `ProjectFleeceService.ApplyHistorySnapshotAsync`, and `FleeceIssueSeeder`.

Fleece.Core 3.1.0 replaces this with an event-sourced model:

```
  .fleece/
    issues.jsonl              ◀ projected snapshot, lean record (no per-field metadata)
                                written ONLY by `fleece project` on the default branch
    changes/
      change_<guid>.jsonl     ◀ per-session append-only event log
      change_<guid>.jsonl       (each line: meta | create | set | add | remove | hard-delete)
    .active-change            ◀ local pointer to current change file (gitignored)
    .replay-cache             ◀ local replay cache (gitignored)
    tombstones.jsonl          ◀ records of hard-deleted issue ids
```

Reads load the snapshot and replay all change files in topological order (each file's leading `meta` event carries a `follows` pointer to the predecessor session GUID, forming a DAG). Writes append events to the active change file. `fleece project` compacts the snapshot + all events into a fresh snapshot, deletes the change files, and runs only on the default branch.

A spike in this exploration session verified the merge story end-to-end:

```
  baseline (3 issues)
       │
       ▼
   ┌──────────┐                     ┌──────────┐
   │ agent    │ rename A, delete B, │ user     │ set A.description,
   │ branch   │ create D            │ on main  │ set C.status=Progress
   └────┬─────┘                     └────┬─────┘
        │                                │
        ▼                                ▼
   change_AGT.jsonl                 change_USR.jsonl
   (different GUID → different filename → no file-level conflict)
        │                                │
        └────────────┬───────────────────┘
                     ▼
                 git merge          (no conflicts; both change files coexist)
                     │
                     ▼
              fleece project       (on main)
                     │
                     ▼
              issues.jsonl correctly contains the field-level merge:
              A.title = "Issue A (agent renamed)"     ← agent
              A.description = "user-set description"  ← user
              B.status = Deleted                       ← agent
              C.status = Progress                      ← user
              D = created                              ← agent
```

Field-level LWW falls out of pure event replay for free, so `IssueMerger` is no longer a load-bearing component on either side.

## Goals / Non-Goals

**Goals**
- Get our entire Fleece access pipeline to flow through `IFleeceService`. No code path writes JSONL files behind the library's back.
- Delete the bespoke merge pipeline (`FleeceFileHelper`, `IssueMerger` usage in our app code, in-memory stash/pop dance in `FleeceIssuesSyncService`) rather than port it.
- Make `git fetch + git merge` the canonical sync mechanism for `.fleece/`. On the default branch, follow up with a `fleece project` shell-out to compact.
- Honour the v3.1 layout in every place we provision a clone (main repo + agent clones) by running `fleece install` to wire the pre-commit hook and gitignore.
- Migrate the in-repo `.fleece/issues.jsonl` once, in the same PR as the package bump, so reviewers see the wire-format diff alongside the version change.

**Non-Goals**
- A new undo/redo implementation. Removed in this change; redesign tracked in Fleece issue `nr5lA9`.
- A daily GitHub Action to run `fleece project` automatically. We'll shell out from the server when sync runs on the default branch; the Action is a follow-up.
- HTTP API changes beyond removing the undo/redo endpoints.
- Cross-stack changes to the TS layout port beyond accepting the renamed `lexOrder` wire key — internal algorithms are unchanged.

## Decisions

### D1. Single migration step (`fleece migrate`) in the same PR as the package bump

Run `fleece migrate` once locally and commit the rewritten `.fleece/issues.jsonl` (lean snapshot shape) plus the empty `.fleece/changes/` directory plus `tombstones.jsonl` in the same commit/PR as the `Fleece.Core` version bump.

*Alternatives considered:* (a) ship the migration in a separate prep PR, run it at boot via a one-shot service. (b) keep both formats readable in code with a compatibility shim.

*Rationale:* (a) creates a window where the codebase is half-migrated and adds operational complexity to coordinate two PRs across deploys. (b) is what `FleeceFileHelper` did for the hash-vs-stable path and is exactly the kind of in-app divergence from `IFleeceService` we're trying to remove. The library itself ships a one-shot `fleece migrate` that's idempotent and produces deterministic output — use it.

### D2. Sync collapses to `git fetch + git merge` (+ optional `fleece project`)

Replace `FleeceIssuesSyncService`'s 400-line stash/fast-forward/in-memory-merge/save pipeline with: `git fetch origin`, `git merge --no-edit origin/<default>`, and — only when the current branch IS the default branch — shell out to `fleece project`. Reload the cache after the merge so the in-memory `ProjectFleeceService` picks up the new events.

*Alternatives considered:* (a) keep a thin `IssueMerger` wrapper for "safety" against unforeseen merge cases. (b) require operators to invoke `fleece project` manually from the CLI.

*Rationale:* (a) duplicates what event replay already gives us deterministically. The spike showed event replay + `fleece project` produces the same merged state as the old `IssueMerger` for divergent edits, hard-deletes, and creates. (b) breaks ergonomic parity with today's behaviour — sync currently is a one-button operation, and we want that to stay true.

### D3. Apply-agent-changes routes through git merge of the agent branch

Replace `FleeceChangeApplicationService.ApplyChangesViaFileMergeAsync` (load both → `IssueMerger` → save) with: detect changes by diffing `IFleeceService.GetAllAsync()` on both paths (for the UI preview), then apply by `git merge`-ing the agent's branch (carrying its `change_<guid>.jsonl` events) into main. The "apply" step becomes git plumbing, not field-level state copying.

*Alternatives considered:* directly append the agent's events to main's active change file.

*Rationale:* keeps each clone's change file authorship intact (the `by` field records who made each edit), makes the operation atomic at the git layer, and means the operation looks identical to any human-driven branch merge for downstream tools (`fleece project`, GitHub PR diffs).

### D4. Delete undo/redo; track the redesign in `nr5lA9`

Snapshot-based undo/redo lived outside the library in a `.history/` sidecar and re-applied state via raw JSONL writes. That worked for v3.0 because `IFleeceService` re-read the disk on every access. v3.1 owns its replay state internally and would diverge from any sidecar-driven mutation. A redesign needs to work in the event-sourced world (suggested approach: compensating events on the active change file with an in-memory undo stack). That's a non-trivial design and out of scope here.

*Alternatives considered:* (a) port the snapshot model to write events instead of files. (b) keep the existing service but feature-flag it off.

*Rationale:* (a) is the right ultimate destination but requires its own design — what's the inverse of an `add` event when you don't know if the value was present before, what happens when undo crosses a `fleece project` boundary, how does the undo stack survive server restart. Those decisions belong in their own change. (b) leaves ~500 lines of dead code that's easy to accidentally re-enable. Cleaner to remove and add back deliberately.

### D5. Mock-mode seeding writes the v3.1 snapshot directly

`FleeceIssueSeeder` switches from writing `issues_<hash>.jsonl` to writing `.fleece/issues.jsonl` (lean snapshot shape) with no `changes/` directory. A fresh repo with no active change is a valid v3.1 starting state — replay over an empty changeset is a no-op.

*Alternatives considered:* drive the seeder through `IFleeceService.CreateAsync` instead of writing files.

*Rationale:* the seeder is for fast Mock Mode startup; doing N `CreateAsync` calls on each cold start is slower and ties seeding to the live service-graph. Writing the snapshot directly stays in-spirit (it's the supported v3.1 on-disk shape) while keeping seed performance.

### D6. `fleece install` runs during agent-clone provisioning

After `git clone` for any agent working directory, run `fleece install` so the pre-commit hook stages `.fleece/changes/` and the gitignore entries land. Without this, agent commits won't stage their event files, and `.active-change` would be committed (a per-clone pointer that should stay local).

*Alternatives considered:* hand-roll equivalent hook content + gitignore additions ourselves.

*Rationale:* the install command is the documented integration point; mirroring its output ourselves means re-implementing whatever upstream changes the hook gains in future point releases.

## Risks / Trade-offs

[Risk: `.fleece/issues.jsonl` ordering changes between clones running `fleece project` independently]
→ Mitigation: `fleece project` only runs on the default branch (enforced by the CLI). The deterministic snapshot writer in v3.1 produces stable line ordering for the same input event set. Confirmed in the spike — two runs on the same merged state produced byte-identical snapshots.

[Risk: Multiple change files growing unboundedly on long-lived feature branches]
→ Mitigation: each session/branch writes ONE change file (rotated on a new session); the file is append-only but bounded by the number of writes in that session. `fleece project` on main flattens them after merge. Worst case: a 6-month branch with 10K events is one ~2MB JSONL file.

[Risk: Existing TS layout fixtures use `sortOrder`; renamed to `lexOrder` on the wire]
→ Mitigation: regenerate fixtures via `UPDATE_FIXTURES=1` in the same PR. The C# `ParentIssueRef.SortOrder` property name doesn't change; only the JSON key does via `[JsonPropertyName("lexOrder")]`. The TS port reads the JSON wire key, so we update it once.

[Risk: Pre-commit hook collides with future Husky/lefthook adoption]
→ Mitigation: the fleece block is bracketed (`# >>> fleece block >>>` / `# <<< fleece block <<<`), and `fleece install` is idempotent. Any future hook manager can wrap or replace the block deliberately.

[Risk: Server can't shell out to `fleece` in container images that don't include the CLI]
→ Mitigation: `Dockerfile.base` already pins `Fleece.Cli` 3.0.0; bumping to 3.1.0 keeps it on `$PATH` in every container. Host-mode dev profiles inherit the developer's local install (already required for `fleece` operations today).

[Risk: Removing undo/redo is a user-visible regression]
→ Mitigation: the feature was best-effort and known to be flaky. Communicate in the PR description and (briefly) in the in-app changelog/toast if one exists. Issue `nr5lA9` tracks the redesign.

[Risk: `fleece project` shell-out failures during sync silently drop the compaction step]
→ Mitigation: surface non-zero exit codes as a sync warning (not a hard failure — the snapshot is still correct, just un-compacted). Log to OTel. Add a retry on next sync.

## Migration Plan

1. **Pre-flight (no behaviour change)**
   - Bump `Fleece.Core` 3.0.0 → 3.1.0 in `Homespun.Server.csproj` + `Homespun.Shared.csproj`.
   - Bump `Fleece.Cli` 3.0.0 → 3.1.0 in `Dockerfile.base`.
   - Update `CLAUDE.md` version note and the Fleece feature-slice description.

2. **In-repo migration (one commit)**
   - Run `fleece migrate` once against `/workdir/.fleece/`.
   - Run `fleece install` to add `.gitignore` entries and pre-commit hook.
   - Commit the rewritten `.fleece/issues.jsonl`, the new `.fleece/changes/` directory, `.fleece/tombstones.jsonl`, `.gitignore` updates, and `.git/hooks/pre-commit` (note: hooks are not normally tracked; we'll document the install step in `CLAUDE.md` for contributors).

3. **Code surgery (this is the bulk of the diff)**
   - Delete `FleeceFileHelper.cs`. Every caller routed to `IFleeceService.GetAllAsync()` / `IFleeceService` mutators.
   - Rewrite `FleeceIssuesSyncService` per D2.
   - Rewrite `FleeceChangeApplicationService` per D3.
   - Delete `IssueHistoryService`, `IIssueHistoryService`, `FleeceHistoryOptions`, the `RecordHistorySnapshotAsync` call sites, the `/history/undo` + `/history/redo` controllers, and the web hooks/buttons/keybindings for undo/redo.
   - Update `FleeceIssueSeeder` per D5.
   - Add `fleece install` invocation to clone provisioning per D6.
   - Delete the `IssueMerger` field from `FleeceChangeDetectionService` and route reads through `IFleeceService` on both paths.

4. **Fixture / test refresh**
   - Regenerate `tests/Homespun.Web.LayoutFixtures/fixtures/*.input.json` with `UPDATE_FIXTURES=1`.
   - Delete `FleeceIssueSyncIntegrationTests`' `IssueMerger` round-trip cases; rewrite against `git merge`.
   - Delete `IssueHistoryServiceTests`.
   - Update `FleeceIssueSeederTests` to assert on `issues.jsonl` shape.

5. **Verification**
   - `dotnet test` clean.
   - `npm run typecheck` + `npm test` clean.
   - Manual smoke: start `dev-mock`, create/edit/delete issues, observe `.fleece/changes/` accruing events; run `fleece project` from a shell, observe `issues.jsonl` rewritten and `changes/` cleared.
   - Manual smoke (sync path): two clones, edit divergently, run pull-and-sync, confirm both sides reconcile and `fleece project` runs on default branch.
   - Manual smoke (agent path): start an agent session in `dev-live`, have it mutate issues, apply changes, confirm the agent's change file lands in main's `.fleece/changes/` via the git merge path.

**Rollback**: revert the PR. The migration is one-way (the legacy hash format is gone in 3.1.0), but the lean snapshot can be hand-converted back by re-introducing `*LastUpdate`/`*ModifiedBy` fields with `lastUpdate` as their value if absolutely necessary. In practice, rollback would mean reverting `Fleece.Core` to 3.0.0 and accepting the data loss of per-field LWW metadata.

## Open Questions

- **Should we ship the daily `fleece project` GitHub Action in this change, or as a follow-up?** Leaning follow-up — it's a CI/CD concern that benefits from being landed and observed independently. The server-side shell-out (D2) provides interactive parity in the meantime.
- **Do agent clones need their own `fleece install` if they only ever merge their branch back into main (never edit on `main`)?** Yes — the gitignore entries matter regardless of branch, and the hook makes commits hygienic. Cost is one shell-out per clone provisioning.
- **What happens if `fleece project` is running on the server and a sync runs concurrently?** The CLI takes the same `EventStore._writeLock` semaphore the library uses; concurrent projection + append is internally safe. Worth a brief load test but not a blocker.
