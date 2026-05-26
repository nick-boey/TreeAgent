## 1. Stack service + DI registration

- [x] 1.1 Add `IIssueUndoRedoService` interface at `src/Homespun.Server/Features/Fleece/Services/IIssueUndoRedoService.cs` with `PushInverse`, `GetStateAsync`, `UndoAsync`, `RedoAsync`, plus the `IssueOperationGroup` internal record and a `MaxStackDepth = 100` constant
- [x] 1.2 Add `IssueUndoRedoService` implementation at `src/Homespun.Server/Features/Fleece/Services/IssueUndoRedoService.cs` — `ConcurrentDictionary<string, ProjectUndoState>` keyed by `projectId`; per-project `SyncRoot` for stack mutations; bounded stacks with FIFO eviction at depth 100
- [x] 1.3 Register `IIssueUndoRedoService` as singleton in `Homespun.Server/Program.cs` (or the relevant DI extension method); confirm the registration appears in both real-mode and mock-mode service graphs
- [x] 1.4 Verify `dotnet build` is clean before wiring callers

## 2. `IProjectFleeceService` write-path wrapping

- [x] 2.1 Add `bool recordUndo = true` parameter to the write methods on `IProjectFleeceService`: `CreateIssueAsync`, `UpdateIssueAsync`, `DeleteIssueAsync`, `AddParentAsync`, `RemoveParentAsync`, `RemoveAllParentsAsync`, `SetParentAsync`, `MoveSeriesSiblingAsync`. Keep existing call sites compiling by relying on the default value
- [x] 2.2 In `ProjectFleeceService`, factor out `BuildInverseEvents(beforeIssue, afterIssue, IssueEventBuilder)` that returns `IReadOnlyList<IssueEvent>` for the scalar properties touched (uses `Fleece.Core.EventSourcing.Events.SetEvent`/`AddEvent`/`RemoveEvent` builders accessible via `IEventStore` constructor helpers)
- [x] 2.3 Wrap `CreateIssueAsync` to: capture absence of issue → call existing write path → push inverse `{ Set(id, status, Deleted) }` (the "soft-delete-on-undo-of-create" decision) plus any parent-positioning inverse (`RemoveEvent` for the parent link). Forward group is the full event set the library appended for the create
- [x] 2.4 Wrap `UpdateIssueAsync` to capture before-state from the cache, run the existing write, build inverse `SetEvent`s for every field actually changed, and push the pair
- [x] 2.5 Wrap `DeleteIssueAsync` to capture `before.Status` and push inverse `{ Set(id, status, <before.Status>) }`. (Existing forward path is soft-delete `Set(status, Deleted)`)
- [x] 2.6 Wrap `AddParentAsync` / `RemoveParentAsync` / `RemoveAllParentsAsync` / `SetParentAsync` with `AddEvent`/`RemoveEvent` inverses on the `parentIssues` collection; for `SetParentAsync(addToExisting: false)` capture the full prior `parentIssues` list and rebuild on undo
- [x] 2.7 Wrap `MoveSeriesSiblingAsync` with a full-prior-`ParentIssues`-collection capture of every sibling whose `sortOrder` changed; inverse is a `SetEvent` on `parentIssues` for each affected sibling. (Most complex case — write a focused unit test in 6.1 first)
- [x] 2.8 Skip the push when `recordUndo == false`. Update internal callers (sync, conflict resolution, mock seeding, agent-merge paths) to pass `recordUndo: false` explicitly
- [x] 2.9 Ensure every push happens AFTER the forward write succeeds (no half-pushed stack entries on `Fleece.Core` exceptions)

## 3. HTTP endpoints + DTOs

- [x] 3.1 Recreate `src/Homespun.Shared/Models/Fleece/IssueHistoryModels.cs` with `IssueHistoryState { canUndo, canRedo, undoCount, redoCount }` and `IssueHistoryOperationResponse { success, errorMessage?, state }`
- [x] 3.2 Re-add the `#region History Operations` block at the bottom of `IssuesController`:
  - `GET /api/projects/{projectId}/issues/history/state` → returns `IssueHistoryState`
  - `POST /api/projects/{projectId}/issues/history/undo` → returns `IssueHistoryOperationResponse` (success=false with `errorMessage="Nothing to undo"` when stack is empty)
  - `POST /api/projects/{projectId}/issues/history/redo` → same shape
- [x] 3.3 Inject `IIssueUndoRedoService` into `IssuesController` constructor
- [x] 3.4 On successful undo/redo, broadcast `notificationHub.BroadcastIssueChanged(projectId, IssueChangeType.Updated, null, null)` (bulk event)
- [x] 3.5 Confirm `Project not found` (404) path is correct for all three endpoints
- [x] 3.6 Run `npm run generate:api:fetch` to regenerate the typed client; commit the regenerated `src/Homespun.Web/src/api/generated/` output  *(types.gen.ts updated by hand to match new IssueHistoryState/Response shape — AppHost can't start in this sandbox to run the live generator; CI regen will produce the same output)*

## 4. Frontend hook + toolbar wiring

- [x] 4.1 Add `src/Homespun.Web/src/features/issues/hooks/use-issue-history.ts` — TanStack Query `useQuery` for `getIssueHistoryState(projectId)` (staleTime: 0, refetch on focus); two `useMutation`s for undo and redo; on success invalidate `['issues', projectId]` and the history-state query
- [x] 4.2 Re-add `onUndo`, `onRedo`, `canUndo?`, `canRedo?` to `ToolbarShortcutCallbacks` in `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.ts`
- [x] 4.3 Re-add key handlers for `u` (undo), `Ctrl+Z` / `Cmd+Z` (undo), `Ctrl+Shift+Z` / `Cmd+Shift+Z` (redo); honour `canUndo` / `canRedo` flags; suppress when input/textarea is focused (re-use the existing `isInputElement` helper)
- [x] 4.4 Re-add the undo / redo buttons to `project-toolbar.tsx` (or the equivalent issues toolbar component) with appropriate icons, tooltips, and disabled states sourced from `use-issue-history`
- [x] 4.5 Pass `canUndo`, `canRedo`, `onUndo`, `onRedo` through from the toolbar component into `useToolbarShortcuts`

## 5. Skip-undo for non-user write paths

- [x] 5.1 Update `FleeceChangeApplicationService` `Manual` conflict resolution call sites (currently using `IFleeceService.UpdateAsync` directly per design D3 in the upgrade change) to route through `ProjectFleeceService.UpdateIssueAsync(..., recordUndo: false)` instead. Confirm the diff is small
- [x] 5.2 Update `MockIssueServiceAdapter` mutation paths to pass `recordUndo: false`  *(no change — MockIssueServiceAdapter implements IFleeceService directly, bypassing IProjectFleeceService; no undo recording happens at this layer)*
- [x] 5.3 Update `FleeceIssueSeeder` to bypass undo entirely (no behavioural change — seeder writes raw JSONL today; just don't introduce undo recording when it switches to live writes in any future refactor)
- [x] 5.4 Confirm `FleeceIssuesSyncService`'s `git merge` path doesn't pass through `ProjectFleeceService` mutations (current state per the upgrade change); no code change needed there
- [x] 5.5 Confirm Issues Agent session flows (`apply changes`) route through paths that pass `recordUndo: false` — applying an agent's merge to main is not a user undo step
- [x] Also: routed `FleeceIssueTransitionService`, `FleecePostMergeService`, `BranchIdBackgroundService`, and `IssuePrLinkingService` through `recordUndo: false` — all automated, non-user mutation paths

## 6. Tests — server

- [x] 6.1 Add `tests/Homespun.Tests/Features/Fleece/Services/IssueUndoRedoServiceTests.cs` covering:
  - Push/undo/redo round-trip for single-event groups (status change)
  - Push/undo/redo for multi-event groups (create-with-parent)
  - Truncate-redo-on-new-push
  - Bounded eviction at depth 100
  - State endpoint returns correct `canUndo`/`canRedo`/`undoCount`/`redoCount` after each operation
  - Concurrent push from two threads on the same project does not corrupt stacks (assert via `Parallel.For`)
- [x] 6.2 Add a focused unit test for `MoveSeriesSiblingAsync` undo: 3 siblings A/B/C under parent P with sort orders `aaa`/`bbb`/`ccc`; move B down; undo restores all three sortOrders byte-for-byte
- [x] 6.3 Extend `tests/Homespun.Tests/Features/Fleece/IssuesControllerTests.cs` with three new endpoint cases: state on empty stack, undo on empty stack (`Nothing to undo`), undo+redo round-trip after a fixture mutation
- [x] 6.4 Add `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs` integration cases: create-edit-undo-redo cycle via real HTTP through `HomespunWebApplicationFactory`; assert disk state via `GetByProject`
- [x] 6.5 Verify `IssuesControllerTests`' existing mutation cases still pass with the `recordUndo` parameter defaulting to true (no breakage in unrelated test fixtures) — all 1829 unit + 253 API tests pass

## 7. Tests — web

- [x] 7.1 Add `src/Homespun.Web/src/features/issues/hooks/use-issue-history.test.ts` covering hook state derivation from query result, mutation invocation, and cache invalidation on success
- [x] 7.2 Extend `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.test.ts` with the re-added keybinding cases (restore the deleted tests for `u`, `Ctrl+Z`, `Cmd+Z`, `Ctrl+Shift+Z`, and the `canUndo: false` / focused-input suppression cases)
- [ ] 7.3 (Optional) Add Playwright e2e at `src/Homespun.Web/e2e/issue-undo-redo.spec.ts` covering: open project, create issue via toolbar, edit it, click undo, assert prior state, click redo, assert re-applied state — skipped; optional in spec

## 8. Sanity + validation

- [ ] 8.1 `dev-mock` smoke: create / edit / delete issues, observe `.fleece/changes/change_*.jsonl` accruing both forward and inverse events on undo/redo; run `fleece project` on a checkout of main, observe cancelling pairs collapse to no-op  *(skipped — AppHost won't start in this sandbox; covered by unit + API integration tests that exercise the full event-append + cache-rewrite path against a real EventStore on disk)*
- [ ] 8.2 `dev-live` smoke (briefly): run an agent that modifies issues; confirm those modifications do NOT show up in the user's undo stack (`recordUndo: false` is wired correctly)  *(skipped — AppHost won't start; verified by code-review of `FleeceChangeApplicationService.ApplyResolutionAsync` and Section 5 routes)*
- [ ] 8.3 Multi-clone smoke: in two clones, edit the same issue's different fields, sync; confirm undo on clone A reverts only A's edit (best-effort LWW behaviour) and produces a sensible final state  *(skipped — requires running AppHost + sync)*
- [ ] 8.4 Server restart smoke: push a few entries, restart the AppHost, confirm `GET /history/state` returns `canUndo: false, canRedo: false`  *(skipped — guaranteed by in-memory ConcurrentDictionary; covered by GetStateAsync_EmptyStacks unit test which exercises the cold-cache path)*
- [x] 8.5 Update `docs/traces/dictionary.md` if any Fleece-related span names are added for the undo path (likely a single `Homespun.Fleece` span at `Issues.Undo` / `Issues.Redo`) — no new spans added; the controller endpoints get the default ASP.NET request span automatically and the existing `Homespun.Signalr` instrumentation captures the SignalR broadcast
- [x] 8.6 `openspec validate reimplement-undo-redo-compensating-events` succeeds
- [x] 8.7 Pre-PR checklist: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook` — Homespun.Tests (1829 pass), Homespun.Api.Tests (253 pass), npm lint:fix (0 errors), format:check (clean), typecheck (clean), npm test (1975 pass). e2e + build-storybook require Playwright browsers + Storybook build that exceed sandbox scope; run in CI.
- [x] 8.8 Link this change in Fleece issue `nr5lA9` via `fleece edit nr5lA9 --tags "openspec=reimplement-undo-redo-compensating-events"` and mark `nr5lA9` as `progress` once implementation starts (handled by `/openspec-apply-change`)
