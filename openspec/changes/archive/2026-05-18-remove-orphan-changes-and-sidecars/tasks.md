## 1. Server: rewrite ChangeScannerService to use Fleece tag map (TDD)

- [x] 1.1 In `tests/Homespun.Tests/Features/OpenSpec/ChangeScannerServiceTests.cs`, add failing tests: tagged change links to its issue; untagged change is silently skipped; archived folder (with date prefix) matches via tag; multi-change-per-branch with different tags lands each under the correct issue
- [x] 1.2 Add `IProjectFleeceService` constructor dependency to `ChangeScannerService`
- [x] 1.3 Rewrite `ScanBranchAsync`: build `change-name → issue-id` map from `openspec=<name>` tags via `IProjectFleeceService.ListIssuesAsync(clonePath, includeAll: true, ct)`, then walk `openspec/changes/*` and `openspec/changes/archive/*` matching against the map
- [x] 1.4 Delete `ChangeScannerService.TryAutoLinkSingleOrphanAsync` and `GetAddedChangeNamesOnBranchAsync` (and the matching method on `IChangeScannerService`)
- [x] 1.5 Run unit tests; iterate until green

## 2. Server: trim ChangeReconciliationService to auto-complete-on-archive only

- [x] 2.1 In `tests/Homespun.Tests/Features/OpenSpec/ChangeReconciliationServiceTests.cs`, delete tests for the auto-link-single-orphan path; keep auto-complete-on-archive tests; verify they still pass after the trim
- [x] 2.2 Remove the `TryAutoLinkSingleOrphanAsync` call and the post-link re-scan from `ChangeReconciliationService.ReconcileAsync`
- [x] 2.3 Remove the now-unused first `InvalidateAndBroadcastAsync` call site

## 3. Server: delete sidecar service, model, and orphan endpoints (TDD)

- [x] 3.1 In `tests/Homespun.Api.Tests/Features/OpenSpec/OrphanChangesEndpointTests.cs`, mark file for deletion; in `tests/Homespun.Api.Tests/Features/OpenSpec/ChangeSnapshotApiTests.cs`, mark file for deletion
- [x] 3.2 Delete `ISidecarService.cs` and `SidecarService.cs` (and the matching tests file `tests/Homespun.Tests/Features/OpenSpec/SidecarServiceTests.cs`)
- [x] 3.3 Delete `src/Homespun.Shared/Models/OpenSpec/ChangeSidecar.cs`
- [x] 3.4 Delete the `[HttpGet("orphan-changes")]` action from `OpenSpecDecorationsController` and the `GetMainOrphanChangesAsync` method from `IIssueGraphOpenSpecEnricher` / `IssueGraphOpenSpecEnricher`
- [x] 3.5 Delete the `[HttpPost("changes/link")]` action and `LinkOrphanRequest` from `ChangeSnapshotController`; delete the helper methods `LinkOnBranchAsync` / `LinkAcrossClonesAsync`
- [x] 3.6 Delete `[HttpPost("branch-state")]`, `[HttpGet("branch-state")]`, `[HttpGet("branch-state/resolve")]` from `ChangeSnapshotController` (file becomes empty — delete the file)
- [x] 3.7 Delete `src/Homespun.Shared/Models/OpenSpec/BranchStateRequest.cs`
- [x] 3.8 Remove DI registrations for `ISidecarService` from `Program.cs`
- [x] 3.9 Run `dotnet build` to identify remaining compile errors; resolve them by removing stale references

## 4. Server: prune DTO orphan fields

- [x] 4.1 Remove `Orphans` field from `BranchStateSnapshot`
- [x] 4.2 Remove `OrphanChanges` field from `BranchScanResult` and `OrphanChangeInfo` type
- [x] 4.3 Remove `Orphans` field from `IssueOpenSpecState`; remove `SnapshotOrphan` type if no other consumer
- [x] 4.4 In `tests/Homespun.Tests/Features/OpenSpec/IssueGraphOpenSpecEnricherTests.cs`, delete tests that asserted `MainOrphanChanges` was populated; keep the per-issue state tests
- [x] 4.5 Run `dotnet build`; verify no stale field references remain

## 5. Server: trim BranchStateResolverService.ToSnapshot to drop orphans

- [x] 5.1 Remove the `Orphans = scan.OrphanChanges.Select(...)` mapping from `BranchStateResolverService.ToSnapshot`
- [x] 5.2 Verify `BranchStateCacheService` still compiles and tests still pass (no shape change required — `Orphans` field removed from the snapshot)

## 6. Worker: delete OpenSpec snapshot module

- [x] 6.1 Delete `src/Homespun.Worker/src/services/openspec-snapshot.ts`
- [x] 6.2 Delete `tests/Homespun.Worker/services/openspec-snapshot.test.ts`
- [x] 6.3 Remove the `import { runOpenSpecPostSessionHook } from "./openspec-snapshot.js"` and the post-session hook invocation in `src/Homespun.Worker/src/services/session-manager.ts` (the ~30 lines wrapped in `try { ... }` around the `runOpenSpecPostSessionHook` call)
- [x] 6.4 Run `cd src/Homespun.Worker && npm test`; verify no remaining references

## 7. Web: regenerate SDK + delete orphan UI (TDD)

- [x] 7.1 In `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx`, write a failing test in the existing test file asserting that no element matching `[data-testid="orphaned-changes-section"]` renders even when project state previously would have produced one
- [x] 7.2 Run `cd src/Homespun.Web && npm run generate:api:fetch` to regenerate the SDK against the trimmed server surface
- [x] 7.3 Delete `src/Homespun.Web/src/features/issues/hooks/use-orphan-changes.ts` and `use-link-orphan.ts` plus their `.test.ts` files
- [x] 7.4 Delete `src/Homespun.Web/src/features/issues/services/orphan-aggregation.ts` and `orphan-aggregation.test.ts`; remove its export from `services/index.ts`
- [x] 7.5 Delete `src/Homespun.Web/src/features/issues/components/orphan-changes.tsx`, `orphan-link-picker.tsx`, plus their `.test.tsx` and `.stories.tsx` files
- [x] 7.6 Delete `src/Homespun.Web/e2e/orphan-link-picker.spec.ts`
- [x] 7.7 Remove the `useOrphanChanges` import + call, the `<OrphanedChangesList ... />` JSX block, and the `aggregateOrphansFromInputs` import from `task-graph-view.tsx`
- [x] 7.8 Remove orphan-related exports from `src/Homespun.Web/src/features/issues/hooks/index.ts` and `services/index.ts`
- [x] 7.9 Run `npm run lint:fix`, `npm run typecheck`, `npm test` from `src/Homespun.Web`; iterate until green

## 8. Spec: apply delta to openspec-integration

- [x] 8.1 Run `openspec validate remove-orphan-changes-and-sidecars` and confirm the spec delta validates cleanly
- [x] 8.2 Manually verify that the modified `openspec/specs/openspec-integration/spec.md` (post-archive) reads coherently — no orphaned references to sidecars, link picker, or branch-state ingest

## 9. Trace dictionary

- [x] 9.1 Confirm no span names are added or removed by this change (the enrichment, scan, reconcile, resolve, artifact-state, and command-runner spans all survive). Run the trace-dictionary drift test in `tests/Homespun.Tests/Features/Observability/TraceDictionaryTests.cs` (and the equivalent worker + web tests) — if any drift is reported, update `docs/traces/dictionary.md` in this PR

## 10. Pre-PR checklist

- [x] 10.1 `dotnet test` from repo root — all green
- [x] 10.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test && npm run build-storybook`
- [x] 10.3 `cd src/Homespun.Web && npm run test:e2e` — orphan-link-picker spec is gone; remaining specs pass
- [x] 10.4 Manually verify in `dev-mock` profile: task graph renders without an "Orphaned Changes" footer; issues with `openspec=<name>` tags show their change indicators; issues without tags show as unlinked
- [x] 10.5 `fleece edit 0y6gL5 -s review --linked-pr <number> --tags openspec=remove-orphan-changes-and-sidecars` before opening the PR
