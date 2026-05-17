## 1. Package bumps + in-repo migration

- [x] 1.1 Bump `Fleece.Core` 3.0.0 → 3.1.0 in `src/Homespun.Server/Homespun.Server.csproj`
- [x] 1.2 Bump `Fleece.Core` 3.0.0 → 3.1.0 in `src/Homespun.Shared/Homespun.Shared.csproj`
- [x] 1.3 Bump `Fleece.Cli` 3.0.0 → 3.1.0 in `Dockerfile.base`
- [x] 1.4 Run `fleece migrate` against `/workdir/.fleece/` to rewrite `issues.jsonl` as the v3.1 lean snapshot and create `tombstones.jsonl`
- [x] 1.5 Run `fleece install` at repo root; verify `.gitignore` gains `.fleece/.active-change` + `.fleece/.replay-cache` and `.git/hooks/pre-commit` carries the fleece block
- [x] 1.6 Update `CLAUDE.md` "Current version: **3.0.0**" → "**3.1.0**" and rewrite the Fleece feature-slice paragraph to describe event-sourced storage + `fleece project` instead of hash-file storage
- [x] 1.7 Verify `dotnet build` succeeds on the in-repo migrated state before touching code

## 2. Delete FleeceFileHelper and reroute readers

- [x] 2.1 Replace `FleeceFileHelper.LoadIssuesAsync` callers in `FleeceChangeDetectionService` with `IFleeceService.GetAllAsync()` on each path (inject `IFleeceService` factory or use a path-scoped service)
- [x] 2.2 Verify no caller of `FleeceFileHelper.SaveIssuesAsync` remains after sections 3-5 land
- [x] 2.3 Delete `src/Homespun.Server/Features/Fleece/Services/FleeceFileHelper.cs`
- [x] 2.4 Delete the hash-file consolidation branch in `ProjectFleeceService.ResolveJsonlFilePath` (migration handled it once); simplify to "ensure the stable file exists, return its path"

## 3. Collapse FleeceIssuesSyncService

- [x] 3.1 Replace `PullAndMergeFleeceInternalAsync` body with `git fetch origin` + `git merge --no-edit origin/<default>` + cache reload
- [x] 3.2 Add `fleece project` shell-out only when the current branch equals the default branch (after a successful merge)
- [x] 3.3 Surface non-zero `fleece project` exit codes as a sync warning (`HasNonFleeceChanges`-style soft field), not a hard failure
- [x] 3.4 Delete the in-memory `IssueMerger` field, `MergeFleeceFromRemoteAsync`, `TryResolveFleeceConflictsAsync`, and the stash/clean-fd dance from `FleeceIssuesSyncService`
- [x] 3.5 Keep `GetNonFleeceChangesAsync`, `CheckBranchStatusAsync`, and `Discard*` methods as-is (still useful)

## 4. Rewrite FleeceChangeApplicationService

- [x] 4.1 Delete `ApplyChangesViaFileMergeAsync` and the `IssueMerger _issueMerger` field
- [x] 4.2 Detection path: change `LoadIssuesFromPathAsync` to use `IFleeceService.GetAllAsync()` on each path (matches the change-detection service pattern)
- [x] 4.3 Apply path (AgentWins / no-conflict cases): `git merge` the agent branch into main inside the working tree; reload cache; do NOT call `IssueMerger`
- [x] 4.4 Apply path (MainWins): skip the agent's change file altogether (don't merge); reload cache
- [x] 4.5 Apply path (Manual): unchanged in spirit — store pending conflicts, resolve via `ResolveConflictsAsync`, but resolution writes via `IFleeceService.UpdateAsync` (those edits become events in the active change file)

## 5. Delete undo/redo

- [x] 5.1 Delete `IssueHistoryService.cs`, `IIssueHistoryService.cs`, `FleeceHistoryOptions.cs`
- [x] 5.2 Delete `ProjectFleeceService.ApplyHistorySnapshotAsync` and every `RecordHistorySnapshotAsync` call site in `ProjectFleeceService`
- [x] 5.3 Delete the `/api/projects/{projectId}/issues/history/undo` and `/redo` controller endpoints + their request/response DTOs + `IssueHistoryState` if unused elsewhere
- [x] 5.4 Delete the DI registration for `IIssueHistoryService` in `Program.cs` / extension method
- [x] 5.5 Delete frontend undo/redo hooks, buttons, keybindings, and any toolbar/SignalR plumbing they depended on
- [x] 5.6 Delete `tests/Homespun.Tests/Features/Fleece/Services/IssueHistoryServiceTests.cs` and any e2e tests that exercise undo/redo
- [x] 5.7 Remove undo/redo references from `CLAUDE.md` if any exist
- [x] 5.8 Confirm Fleece issue `nr5lA9` exists and is referenced in the PR description as the redesign tracker

## 6. Mock-mode seeding + agent-clone provisioning

- [x] 6.1 Change `FleeceIssueSeeder.SeedIssuesAsync` to write to `.fleece/issues.jsonl` (no hash filename) with the lean snapshot shape
- [x] 6.2 Verify `MockIssueServiceAdapter` still works against v3.1 replay (pre-write `issues.jsonl`, no `changes/`, no `.active-change` — empty event log is a valid v3.1 state)
- [x] 6.3 In `GitCloneService` (or wherever agent working directories are provisioned), invoke `fleece install` after a successful clone
- [x] 6.4 Confirm Docker-mode agent execution images inherit `fleece` 3.1.0 on `$PATH` (via `Dockerfile.base` bump)

## 7. Tests and fixtures

- [x] 7.1 Regenerate `tests/Homespun.Web.LayoutFixtures/fixtures/*.input.json` with `UPDATE_FIXTURES=1`; verify the diff is limited to `sortOrder` → `lexOrder` plus removal of `*LastUpdate`/`*ModifiedBy` fields
- [x] 7.2 Delete `IssueMerger` round-trip cases from `FleeceIssueSyncIntegrationTests`; replace with a "two-clones divergent edit → git merge → cache reload" integration case mirroring the design spike
- [x] 7.3 Delete `FleeceChangeApplicationServiceTests`' file-merge cases; replace with cases that assert `git merge` is the apply mechanism (use a temp git repo fixture)
- [x] 7.4 Update `FleeceIssueSeederTests` to assert seeded data lands in `.fleece/issues.jsonl` (not `issues_<hash>.jsonl`)
- [x] 7.5 Update or delete `MockGitCloneServiceTests` references to `issues_abc.jsonl` (use `issues.jsonl`)
- [x] 7.6 Update `FleeceIssuesSyncServiceTests` to assert the new git-fetch/merge/(optionally)-project pipeline; delete tests that asserted on the old stash/clean-fd ordering
- [x] 7.7 Run full `dotnet test` and the React Vitest + Playwright suites; ensure clean

## 8. Sanity + spans + validation

- [x] 8.1 Run `dev-mock` end-to-end: create/edit/delete issues, observe `.fleece/changes/change_*.jsonl` accruing events
- [x] 8.2 Manual shell-out of `fleece project` against the running mock state; observe `issues.jsonl` rewritten and `changes/` cleared
- [x] 8.3 Two-clone divergent-edit smoke: confirm pull-and-sync reconciles without invoking the deleted `IssueMerger` code path
- [x] 8.4 Agent-session smoke: in `dev-live`, run an agent that mutates issues; apply changes; confirm the agent's change file lands in main's `.fleece/changes/`
- [x] 8.5 Update `docs/traces/dictionary.md` if any Fleece-related span names change; let the drift check pass
- [x] 8.6 `openspec validate upgrade-fleece-event-sourced-storage` succeeds
- [x] 8.7 Pre-PR checklist: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook` — all green
