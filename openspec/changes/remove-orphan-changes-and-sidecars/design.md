## Context

Today's OpenSpec change-to-issue linkage uses a `.homespun.yaml` sidecar file at `openspec/changes/<name>/.homespun.yaml`. The sidecar's `fleeceId` field is matched against the fleece-id suffix parsed from the branch name (`task/foo+abc123` → `abc123`). Each branch clone is scanned independently; changes without a sidecar are surfaced as "orphans" in a footer block under the task graph, with a link-picker dialog that lets the user pick a Fleece issue to write a sidecar against (potentially across multiple clones via the branchless mode).

Independently, the Fleece CLI now injects an OpenSpec integration block into the agent system prompt that instructs agents to write `openspec=<change-name>` tags onto Fleece issues whenever they create or work on an OpenSpec change. The tag-based linkage is already canonical in the prompt; only the server has not caught up. The sidecar mechanism remains as redundant scaffolding around an orphan-detection feature the user has decided is not worth the surface area.

Pre-existing branches in working clones may carry `.homespun.yaml` sidecars from past sessions. The user has explicitly chosen not to migrate them — they become inert files on disk, and the affected issues display as unlinked until manually retagged with `fleece edit <id> --tags openspec=<name>`.

The web client's task-graph view currently fetches three OpenSpec-related signals in parallel: `openspec-states` (per-issue), `orphan-changes` (project-wide), and the linked-PRs / agent-statuses companions. The web only ever consumes `openspec-states` for issue rendering and `orphan-changes` for the bottom footer. The branch-state ingest + read endpoints (`POST /api/openspec/branch-state`, `GET /api/openspec/branch-state`, `GET /api/openspec/branch-state/resolve`) exist server-side but have no web-client consumer — they exist purely to let the worker pre-warm the resolver cache at session end.

## Goals / Non-Goals

**Goals:**
- Switch all OpenSpec change-to-issue linkage to Fleece `openspec=<change-name>` tags read from per-clone Fleece state.
- Remove the entire orphan-changes UI and its supporting server endpoint.
- Remove the sidecar service, model, and on-disk file writes.
- Remove the worker's post-session OpenSpec snapshot hook and the public branch-state endpoints (worker no longer has the tag-side context required to pre-warm the cache correctly).
- Preserve the auto-complete-on-archive behavior (issue → `complete` when its linked change archives).
- Preserve the task-graph indicator visuals, the OpenSpec tab in the run-agent panel, the artifact-state micro-cache, and the trace dictionary.

**Non-Goals:**
- Migrating existing `.homespun.yaml` sidecars to Fleece tags. The user has chosen to ignore them; affected branches retag manually.
- Strengthening the Fleece CLI system-prompt instructions. The user has confirmed the existing injected prompt is sufficient for agents; revisions can happen separately if behavior drifts.
- Deleting the `.homespun.yaml` files themselves. They are left in place; the scanner simply stops reading them.
- Adding a new write path for `openspec=` tags. Agents already issue `fleece edit <id> --tags openspec=<name>` from the openspec skills; this change consumes those tags, it does not produce them.
- Changing `BranchStateCacheService`'s TTL, eviction, or shape. The cache stays as the resolver's internal memo; only its public ingestion path goes away.

## Decisions

### Decision 1: Replace sidecar reads with a per-clone tag map

The replacement linkage source is a `change-name → issue-id` map built from `openspec=` tags on Fleece issues in the branch clone. The scanner consumes this map instead of reading sidecar files.

```
                    OLD (sidecar)                            NEW (tag map)
                    ─────────────                            ─────────────
   Per change directory:                                Per clone:
     read .homespun.yaml                                  list issues via IProjectFleeceService
     parse YAML                                           filter tags starting "openspec="
     match sidecar.fleeceId == branchFleeceId             build map: change-name → issue-id
                                                          
   Per change directory:                                Per change directory:
     match present                                        change-name in map → linked to map[name]
     match wrong fleece-id → "inherited" (filtered)       change-name not in map → silently skipped
     match missing → "orphan"                             (no orphan / no inherited concept)
```

`ChangeScannerService` takes a new constructor dependency on `IProjectFleeceService`. The clone path is the existing first argument to `ScanBranchAsync`; `ListIssuesAsync(clonePath, includeAll: true)` already exists on the Fleece service and reads the per-clone snapshot + event logs in topological order.

The `branchFleeceId` parameter is preserved on `ScanBranchAsync` so the resolver's existing call sites do not have to be reshaped — but inside the scanner, the linkage decision now consults the tag map, not the parameter. The parameter is kept for telemetry tagging (`activity.SetTag("issue.id", branchFleeceId)`) and for the auto-complete-on-archive path in `ChangeReconciliationService`.

**Alternative considered:** Have the scanner pre-resolve only the single branch issue (via `branchFleeceId`) and check only its tags. This would be slightly cheaper (one issue read vs. all issues), but it forecloses the "changes linked to different issues on the same branch" requirement and forces a one-issue-per-branch assumption the spec already says is not true. Reading every issue's tags via the in-memory Fleece projection is sub-100ms even at thousands of issues; not worth optimizing around.

### Decision 2: Drop the worker snapshot hook entirely

The worker's `runOpenSpecPostSessionHook` exists to pre-warm the server's 60s branch-state cache so the very next graph render after session-end skips the on-disk scan. Under tag-based linkage, the worker no longer has enough context to populate that cache correctly — it sees `openspec/changes/<name>/` directories on disk, but the tag map lives in `.fleece/changes/*.jsonl` event logs and the projected snapshot, which the worker does not read.

Two paths considered:

1. **Worker posts raw change-name list; server still does the Fleece read at request time.** Saves only the directory walk + tasks.md parse. The `openspec status` subprocess is already mtime-cached, so the marginal win is minimal. Worker carries ~330 lines of code (`openspec-snapshot.ts` + tests) for that marginal win.

2. **Drop the worker hook; server does the live scan on first request.** The cache still warms after the first render (60s TTL); subsequent renders are free. Worker loses ~330 lines plus the post-session call site. Two unused GET endpoints (`branch-state`, `branch-state/resolve`) also vanish.

Path 2 wins. The cache value was modest under sidecars and shrinks further under tags — the new scan is *cheaper* (no per-file YAML parse, just an in-memory tag-map lookup against an existing Fleece projection that the issues endpoint already loaded for the same request). `BranchStateCacheService` survives as an internal memo so back-to-back renders within 60s still benefit.

### Decision 3: Trim `ChangeReconciliationService` to the auto-complete-on-archive half only

`ReconcileAsync` has two responsibilities today:

1. **Auto-link a single orphan** — writes a `.homespun.yaml` sidecar when exactly one orphan exists on the branch.
2. **Auto-complete the issue when a linked change archives** — transitions the Fleece issue to `complete` status, broadcasts an `IssueChanged` SignalR event.

Responsibility 1 disappears with sidecars; the openspec skills now own tag writes via `fleece edit`. Responsibility 2 is sidecar-agnostic (it only inspects `LinkedChanges` for `IsArchived == true`) and is preserved verbatim. The service's surface area shrinks meaningfully — no more `IChangeScannerService.TryAutoLinkSingleOrphanAsync` dependency, no more second scan-after-link.

**Alternative considered:** Inline the surviving auto-complete logic directly into `BranchStateResolverService.GetOrScanAsync` and delete `ChangeReconciliationService` entirely. Rejected because the side-effect (state transition + SignalR broadcast) is a distinct concern from the pure cache-or-scan flow, and the resolver should not own state transitions. The reconciliation service stays as a thin pass-through around the scan plus the post-scan archive check.

### Decision 4: `IssueOpenSpecState` loses its `Orphans` field

The DTO carried per-branch orphan information so the web client could aggregate orphans across branches without a second roundtrip. Now that orphan-as-a-concept is gone, the field is dead weight and a misleading shape for future readers. Drop it. The web client regenerates the SDK and `IssueOpenSpecState` arrives without the field — TypeScript callers that reference it will fail at type-check time, which is the intended forcing function for cleaning up the consumer code in the same change.

`BranchStateSnapshot.Orphans` and `BranchScanResult.OrphanChanges` go away for the same reason. They are internal types but feed `IssueOpenSpecState`; keeping them around would be pure cruft.

### Decision 5: Keep `BranchStateCacheService`, drop its public ingest endpoint

The cache is still useful: a single graph render fans out one scan per visible branch; back-to-back renders within 60s reuse the cached snapshot. The 60s TTL stays. What goes away is the public surface — no more `POST /api/openspec/branch-state` (worker no longer posts), no more `GET /api/openspec/branch-state` or `GET /api/openspec/branch-state/resolve` (web never used them). The cache becomes resolver-internal, called only by `BranchStateResolverService.GetOrScanAsync`.

`BranchStateRequest` (the POST body DTO) is deleted along with the controller action.

### Decision 6: Leave existing `.homespun.yaml` files on disk

The user has chosen not to migrate. Files become inert: the scanner ignores them, the sidecar service is deleted, no read path remains. They take a few KB per change-directory and will be cleaned up organically as branches close and clones get deleted via the existing clone-lifecycle machinery. Documenting the choice here so future cleanup work has provenance.

A one-shot migration pass (read each existing sidecar, write the corresponding `openspec=` tag onto the matching Fleece issue, then delete the sidecar) was considered. Rejected: most active branches will be re-tagged organically as agents touch them, the migration would have to handle conflict cases (sidecar says X, tag already says Y), and the failure mode of "no indicator until retagged" is benign.

## Risks / Trade-offs

- **[Pre-existing branches lose their change indicator until retagged]** → Mitigated by leaving the sidecar files in place (so manual recovery is possible — read the file, run `fleece edit <id> --tags openspec=<name>`) and by relying on agent skills to retag organically on the next session. The visual degradation is gray-vs-amber; no functional regression.
- **[Agents may forget to write the `openspec=` tag]** → Mitigated by the Fleece CLI's existing system-prompt injection. If we see drift in practice, strengthening the prompt is a separate small change. No defensive auto-link logic on the server side — that was the path we are leaving.
- **[Worker drops its OpenSpec scanning code, losing a fast-path cache warm]** → Mitigated by the cheaper-than-before live scan and the surviving 60s in-memory cache. The first render after session-end pays ≤100ms for the scan + Fleece read; subsequent renders are free.
- **[Generated SDK regeneration may break unrelated consumers]** → Mitigated by the fact that the only callers of the removed endpoints are the orphan-changes code itself (also being deleted in the same change). The generated SDK shrinks; nothing else regenerates.
- **[Spec changes touch a high-traffic capability file]** → Mitigated by the spec deltas being scoped to clearly-named requirements (Orphan change handling, Link-picker dialog, …) and by the `openspec validate` pass at the end of the change ensuring the modified-spec file remains internally consistent.
- **[The `ChangeScannerService` rewrite has the most behavior risk]** → Mitigated by writing the new tag-map-based tests first (TDD), keeping the archive fallback intact, and preserving the existing `ChangeScannerArtifactStateCacheTests` for the mtime micro-cache logic that is untouched.

## Migration Plan

This is a server + worker + web change, deployed as one PR. No data migration. Rollback is a `git revert`.

1. **Server first** — land scanner rewrite + reconciliation trim + endpoint deletions + DTO field removals. The web SDK regeneration step will pick up the narrowed surface.
2. **Worker next (same PR)** — delete `openspec-snapshot.ts` and its post-session hook call. Tests for the deleted module disappear with it.
3. **Web last (same PR)** — regenerate SDK, delete orphan UI + hooks, drop the `useOrphanChanges` import in `TaskGraphView`, delete e2e spec.
4. **Spec deltas (same PR)** — `openspec/specs/openspec-integration/spec.md` reshapes per the modify-capability deltas in `specs/`.

Rollback: the change is self-contained per the PR boundary; `git revert` restores sidecars, orphan UI, and worker scanning together. No on-disk artifact survives the revert in a load-bearing way (pre-existing `.homespun.yaml` files were inert before this change and will become live again after a revert — they were never deleted).

## Open Questions

None at proposal time. All four design points raised in explore-mode discussion were resolved by the user:

1. Auto-complete-on-archive → **keep**
2. Tag-write authority → **Fleece CLI system-prompt injection already covers it; we can strengthen later if drift**
3. Existing sidecars → **ignore (leave in place)**
4. Worker snapshot endpoint → **drop entirely; keep internal cache**
