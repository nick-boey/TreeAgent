## Why

The "orphaned OpenSpec changes" footer at the bottom of the task graph — and the sidecar-based linkage it depends on — has proven not useful in practice. The sidecar (`.homespun.yaml` written into each `openspec/changes/<name>/` directory) was an early link mechanism that has since been superseded by Fleece's `openspec=<change-name>` tag convention, which is already documented in the agent system prompt and is the linkage path the workflow now expects. The dual mechanism adds complexity (per-clone sidecar scans, auto-link reconciliation, orphan detection, branchless multi-clone link writes, link-picker UI) without earning the surface area.

## What Changes

- **BREAKING** — Linkage source switches from `.homespun.yaml` sidecars to Fleece `openspec=<change-name>` tags. Pre-existing sidecars are ignored (left as inert files); pre-existing branches with no tag display as unlinked until manually retagged via `fleece edit <id> --tags openspec=<name>`.
- **BREAKING** — Remove `POST /api/openspec/changes/link`, `GET /api/projects/{projectId}/orphan-changes`, `POST /api/openspec/branch-state`, `GET /api/openspec/branch-state`, `GET /api/openspec/branch-state/resolve`. None are consumed by the web client today (the web only uses `/openspec-states` via the enricher).
- Delete server: `ISidecarService` + `SidecarService`, `ChangeSidecar` model, `LinkOrphanRequest` + endpoint, `IIssueGraphOpenSpecEnricher.GetMainOrphanChangesAsync`, the orphan branch of `EnrichAsync`, `ChangeScannerService.TryAutoLinkSingleOrphanAsync` + `GetAddedChangeNamesOnBranchAsync`, the auto-link half of `ChangeReconciliationService`, the `Orphans` / `OrphanChanges` fields on `BranchStateSnapshot` / `BranchScanResult` / `IssueOpenSpecState`, `BranchStateRequest` model.
- Delete worker: `src/services/openspec-snapshot.ts` (and tests), the post-session hook invocation in `session-manager.ts`.
- Delete web: `OrphanedChangesList`, `OrphanLinkPicker` (+ stories, tests), `useOrphanChanges`, `useLinkOrphan` (+ tests), `orphan-aggregation.ts` (+ tests), `e2e/orphan-link-picker.spec.ts`. Regenerate the SDK so orphan endpoints drop out.
- Rewrite `ChangeScannerService.ScanBranchAsync`: list Fleece issues in the clone via `IProjectFleeceService.ListIssuesAsync(clonePath)`, build a `change-name → issue-id` map from `openspec=` tags, then walk `openspec/changes/*` and `openspec/changes/archive/*` and match against the map. A change with a matching tag is linked; otherwise it is silently skipped.
- Keep `BranchStateCacheService` as the resolver's internal memo (60s TTL, still useful for back-to-back graph requests within the same render window).
- Keep `ChangeReconciliationService`'s auto-complete-on-archive behavior (issue transitions to `complete` when its linked change archives). This is sidecar-agnostic.
- Keep all graph visuals (branch dot colours, change phase symbols), the OpenSpec tab in the run-agent panel, the artifact-state micro-cache, and the trace-dictionary spans on the enrichment path.

## Capabilities

### New Capabilities

_None._ This change reshapes existing capabilities only.

### Modified Capabilities

- `openspec-integration`: Linkage source switches from sidecars to Fleece tags; orphan detection / display / link-picker / branchless link endpoint / orphan-changes endpoint / worker snapshot post + endpoints all removed; branch scanner rewritten to consult the per-clone Fleece tag map; auto-complete-on-archive behavior preserved.

## Impact

- **APIs removed**: `POST /api/openspec/changes/link`, `GET /api/projects/{projectId}/orphan-changes`, `POST /api/openspec/branch-state`, `GET /api/openspec/branch-state`, `GET /api/openspec/branch-state/resolve`.
- **API shape narrows**: `IssueOpenSpecState` loses its `Orphans` field. Web client must regenerate.
- **Server code**: `Homespun.Server/Features/OpenSpec/` shrinks (Sidecar service, the orphan branch of the enricher, the auto-link branch of reconciliation, the snapshot ingest controller). `ChangeScannerService` rewrites its linkage logic to take a dependency on `IProjectFleeceService`.
- **Worker code**: `Homespun.Worker/src/services/openspec-snapshot.ts` deletes entirely. `session-manager.ts` drops the post-session hook (≈30 lines).
- **Web code**: bottom-of-graph orphan footer disappears. `TaskGraphView` drops one hook (`useOrphanChanges`) and the `OrphanedChangesList` render block.
- **Spec**: `openspec/specs/openspec-integration/spec.md` is heavily reshaped — five requirements removed, three rewritten.
- **Pre-existing data**: branches with `.homespun.yaml` sidecars but no `openspec=` Fleece tag will silently show no change indicator. Users can retag manually via `fleece edit <id> --tags openspec=<name>`. No migration shipped.
- **Tests**: server unit tests for sidecar service, the orphan branch of the enricher, the auto-link branch of reconciliation, and the snapshot ingest endpoint are all deleted. New tests cover tag-based linkage in `ChangeScannerServiceTests`. Web tests for orphan-aggregation, orphaned-changes list, and the orphan link picker are deleted. The trace-dictionary drift check still passes (no spans added or removed by this change).
