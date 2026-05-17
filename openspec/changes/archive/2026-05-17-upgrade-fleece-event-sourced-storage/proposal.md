## Why

Fleece 3.1.0 replaced per-clone hashed `.fleece/issues_{hash}.jsonl` files with an event-sourced model: a projected `.fleece/issues.jsonl` snapshot plus per-session append-only event files in `.fleece/changes/change_{guid}.jsonl`. Our codebase has four code paths that write raw `issues_{hash}.jsonl` files directly through `FleeceFileHelper` — bypassing `IFleeceService` — that will silently break under the new layout. A spike (two divergent clones, `git merge`, `fleece project`) confirms that field-level last-writer-wins now falls out of pure event replay for free, so the ~600 lines of bespoke `IssueMerger` plumbing in our sync and change-application services can be deleted rather than ported.

## What Changes

- Bump `Fleece.Core` (`Homespun.Server`, `Homespun.Shared`) and `Fleece.Cli` (`Dockerfile.base`) from 3.0.0 → 3.1.0.
- Run `fleece migrate` once in-repo to rewrite `.fleece/issues.jsonl` into the v3.1 lean snapshot (drops per-field `*LastUpdate`/`*ModifiedBy` metadata) and create `.fleece/changes/` + `.fleece/tombstones.jsonl`.
- Run `fleece install` to wire the pre-commit hook (auto-stages `.fleece/changes/` always, plus `issues.jsonl`/`tombstones.jsonl` on the default branch) and add `.fleece/.active-change` + `.fleece/.replay-cache` to `.gitignore`.
- **BREAKING (server-only)**: delete `FleeceFileHelper` and route every reader/writer through `IFleeceService`. No public HTTP API surface changes.
- **BREAKING (server-only)**: collapse `FleeceIssuesSyncService` from stash → fast-forward → custom `IssueMerger` LWW → `SaveIssuesAsync` into `git fetch` → `git merge` plus a `fleece project` shell-out when on the default branch.
- **BREAKING (server-only)**: rewrite `FleeceChangeApplicationService` to detect agent-vs-main changes via `IFleeceService.GetAllAsync()` on both paths, then apply by `git merge`-ing the agent branch's events into main (no manual `IssueMerger` pass).
- **BREAKING (HTTP API)**: remove the undo/redo endpoints, `IssueHistoryService`, the `.history/` sidecar directory, and the snapshot-based `ApplyHistorySnapshotAsync` path. Follow-up tracked in Fleece issue `nr5lA9` for a compensating-event redesign.
- Provision `fleece install` inside agent-clone setup so agent-side commits hit the same pre-commit hook as the main project.
- `FleeceIssueSeeder` writes `.fleece/issues.jsonl` directly (the supported v3.1 snapshot shape for a fresh repo with no active change).
- Regenerate `tests/Homespun.Web.LayoutFixtures/fixtures/*.input.json` with `UPDATE_FIXTURES=1` to pick up `sortOrder` → `lexOrder` wire-format rename and the lean snapshot shape.
- Update `CLAUDE.md` "Current version: **3.0.0**" → **3.1.0** and the architecture blurb to describe event-sourced storage + `fleece project` instead of the hash-file layout.

## Capabilities

### New Capabilities

(none — this is a library upgrade + refactor inside an existing capability)

### Modified Capabilities

- `fleece-issue-tracking`: replace the hash-file storage model and bespoke `IssueMerger` sync pipeline with the v3.1 event-sourced model; remove the undo/redo requirement (deferred to a follow-up change keyed off issue `nr5lA9`); align the wire-format sub-requirement on `sortOrder` → `lexOrder`.

## Impact

**Affected code (server)**
- `src/Homespun.Server/Features/Fleece/Services/`:
  - `FleeceFileHelper.cs` — **deleted**
  - `FleeceIssuesSyncService.cs` — rewritten (~400 LOC → ~100 LOC)
  - `FleeceChangeApplicationService.cs` — `ApplyChangesViaFileMergeAsync` deleted, change application reroutes through `IFleeceService` + `git merge`
  - `FleeceChangeDetectionService.cs` — reads both sides through `IFleeceService.GetAllAsync()` instead of `FleeceFileHelper.LoadIssuesAsync`
  - `ProjectFleeceService.cs` — `ApplyHistorySnapshotAsync` deleted; `ResolveJsonlFilePath` consolidation branch deleted; `RecordHistorySnapshotAsync` call sites removed
  - `IssueHistoryService.cs` + `IIssueHistoryService.cs` + `FleeceHistoryOptions.cs` — **deleted**
- `src/Homespun.Server/Features/Testing/Services/FleeceIssueSeeder.cs` — writes the lean snapshot directly
- `src/Homespun.Server/Features/Testing/Services/MockIssueServiceAdapter.cs` — verified against the v3.1 replay engine (already uses stable `issues.jsonl`)
- `src/Homespun.Server/Features/Git/GitCloneService.cs` (and agent provisioning paths) — invoke `fleece install` after `git clone`
- HTTP controllers + DI registration — remove the undo/redo endpoints

**Affected code (web)**
- `src/Homespun.Web/src/features/issues/*` — remove undo/redo UI hooks, buttons, keybindings
- `src/Homespun.Web/src/features/issues/services/layout/` — TS port of `IIssueLayoutService` accepts `lexOrder` (with a transitional read alias for `sortOrder` during fixture regen)

**Affected code (tests / fixtures)**
- `tests/Homespun.Tests/Features/Fleece/` — delete the in-memory `IssueMerger` round-trip tests; rewrite `FleeceIssuesSyncServiceTests` against the new git-merge flow; delete `IssueHistoryServiceTests`
- `tests/Homespun.Web.LayoutFixtures/fixtures/*.input.json` — regenerate with `UPDATE_FIXTURES=1`
- `tests/Homespun.Tests/Features/Testing/FleeceIssueSeederTests.cs` — update assertions for `issues.jsonl` (no hash file)

**Affected packages / config**
- `src/Homespun.Server/Homespun.Server.csproj`, `src/Homespun.Shared/Homespun.Shared.csproj` — `Fleece.Core` 3.0.0 → 3.1.0
- `Dockerfile.base` — `Fleece.Cli` 3.0.0 → 3.1.0
- `CLAUDE.md` — version + storage-model description
- `.gitignore` (root) — `.fleece/.active-change`, `.fleece/.replay-cache`
- `.git/hooks/pre-commit` — installed via `fleece install`

**Data migration**
- One-time, in-repo: `fleece migrate` rewrites `.fleece/issues.jsonl`. The migration is committed in the same PR as the package bump so reviewers see the wire-format diff alongside the version change.

**Out of scope**
- Undo/redo redesign (tracked in Fleece issue `nr5lA9`).
- Daily GitHub Action template for automatic `fleece project` on `main` (Fleece's recommended scheduling). We'll shell out from the server when fleece-sync runs on the default branch; the GH Action is a follow-up.
- Any change to the public HTTP API beyond removing the undo/redo endpoints.
