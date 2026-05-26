## Why

The Fleece 3.0 → 3.1 upgrade (archived change `2026-05-17-upgrade-fleece-event-sourced-storage`) deleted the snapshot-based undo/redo path because it bypassed the new event store: `IssueHistoryService` wrote full-issue JSONL files into a `.history/` sidecar and re-applied them through `ProjectFleeceService.ApplyHistorySnapshotAsync`. That pattern is fundamentally incompatible with the v3.1 model where every write must flow through `IFleeceService` and land in the active `change_{guid}.jsonl`. The old design was also flaky enough that users rarely exercised it, so the deletion was clean rather than a port. Fleece issue `nr5lA9` tracks the redesign and this change is its proposal.

The redesigned approach uses **compensating events**: every user-driven mutation pushes its inverse onto an in-memory per-project stack. Undo pops the inverse, appends it via `IEventStore.AppendEventsAsync`, and pushes the original event group onto a sibling redo stack. Because the inverse rides the same event store as forward writes, replay over the merged event set produces the rolled-back field-level state for free, and `fleece project` collapses the compensating-event pair to a no-op at projection time.

## What Changes

- Add `IIssueUndoRedoService` + `IssueUndoRedoService` in `Homespun.Features.Fleece.Services` — per-project undo/redo stacks (in-memory, dropped on restart) keyed by `projectId`.
- Wrap every user-driven mutation method on `IProjectFleeceService` (`CreateIssueAsync`, `UpdateIssueAsync`, `DeleteIssueAsync`, `AddParentAsync`, `RemoveParentAsync`, `RemoveAllParentsAsync`, `SetParentAsync`, `MoveSeriesSiblingAsync`) with a "capture before-state → write → push inverse" wrapper. Each HTTP-call-scoped wrap pushes ONE entry onto the stack (multi-event operations like create-with-parent land as a single undoable group).
- Re-introduce three HTTP endpoints on `IssuesController`, mirroring the deleted shape:
  - `GET /api/projects/{projectId}/issues/history/state` → `{ canUndo: bool, canRedo: bool, undoCount, redoCount }`
  - `POST /api/projects/{projectId}/issues/history/undo` → `{ success, errorMessage?, state }`
  - `POST /api/projects/{projectId}/issues/history/redo` → same shape
- Undo/redo broadcast a bulk `IssueChanged({kind: 'updated', issueId: null, issue: null})` on `NotificationHub` so connected clients invalidate their caches (matches the deleted behaviour).
- Recreate the frontend hook `use-issue-history.ts`, restore toolbar undo/redo buttons in the issues toolbar component, and re-add keybindings `u`, `Ctrl+Z`, `Cmd+Z`, `Ctrl+Shift+Z`, `Cmd+Shift+Z` in `use-toolbar-shortcuts.ts`.
- Add the public spec requirement back to `fleece-issue-tracking` describing the compensating-event semantics, the in-memory non-durable stack, and the truncate-redo-on-new-edit rule.
- Add an Issues Controller hook that **bypasses** undo recording for sync/conflict-resolution writes (those are not user-initiated single steps; they're git-merge fallout). Concretely: keep the inverse-pushing wrapper on `IProjectFleeceService` mutations called from `IssuesController` direct CRUD endpoints, but route `FleeceChangeApplicationService` / `FleeceIssuesSyncService` / `MockIssueServiceAdapter` mutations through a path that skips the stack push.

## Capabilities

### New Capabilities

(none — re-establishes a previously-removed requirement inside `fleece-issue-tracking`)

### Modified Capabilities

- `fleece-issue-tracking`: re-add the **Undo/redo issue history** requirement, rewritten for the event-sourced storage model. Scenarios cover compensating-event semantics, single-step granularity, truncate-redo-on-edit, best-effort LWW behaviour under concurrent edits, and stack reset on server restart.

## Impact

**Affected code (server)**
- `src/Homespun.Server/Features/Fleece/Services/`:
  - `IIssueUndoRedoService.cs` + `IssueUndoRedoService.cs` — **new**
  - `ProjectFleeceService.cs` — every write method captures before-state and emits inverse events via `IIssueUndoRedoService.PushInverse`; an internal write-without-undo overload services sync/agent-merge paths
  - `IProjectFleeceService.cs` — new internal-style write overload taking a `recordUndo: bool` flag (default true)
- `src/Homespun.Server/Features/Fleece/Controllers/IssuesController.cs`:
  - Re-add the constructor parameter `IIssueUndoRedoService undoRedoService`
  - Re-add the `#region History Operations` block with three endpoints
- `src/Homespun.Server/Features/Fleece/Services/FleeceChangeApplicationService.cs`, `FleeceIssuesSyncService.cs`:
  - Call mutation methods with `recordUndo: false` where they do (e.g. `Manual` conflict resolution via `IFleeceService.UpdateAsync`); sync's `git merge` path doesn't go through `ProjectFleeceService` mutations at all, so no change there
- `src/Homespun.Server/Features/Testing/Services/MockIssueServiceAdapter.cs`:
  - Mutation paths in mock mode pass `recordUndo: false` (mock seeding isn't a user step)
- `src/Homespun.Server/Program.cs` (or DI extension method): register `IIssueUndoRedoService` as singleton

**Affected code (shared)**
- `src/Homespun.Shared/Models/Fleece/IssueHistoryModels.cs` — **recreate** with the response DTOs only (`IssueHistoryState`, `IssueHistoryOperationResponse`). No `IssueHistoryEntry` model since the stack is server-side and entries don't escape the API surface.

**Affected code (web)**
- `src/Homespun.Web/src/features/issues/hooks/use-issue-history.ts` — **new** (TanStack Query hook over `/history/state`, `/history/undo`, `/history/redo` with cache-invalidation on success)
- `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.ts` — re-add `onUndo`, `onRedo`, `canUndo`, `canRedo` callbacks + key handlers for `u`, `Ctrl+Z`, `Cmd+Z`, `Ctrl+Shift+Z`, `Cmd+Shift+Z`
- `src/Homespun.Web/src/features/issues/components/project-toolbar.tsx` (or the equivalent toolbar component) — re-add undo/redo buttons with proper disabled states from `use-issue-history`
- `src/Homespun.Web/src/api/generated/` — regenerated from OpenAPI after the server endpoints land

**Affected code (tests / fixtures)**
- `tests/Homespun.Tests/Features/Fleece/Services/IssueUndoRedoServiceTests.cs` — **new** unit suite (stack semantics, inverse composition, truncate-on-edit, LWW best-effort behaviour)
- `tests/Homespun.Tests/Features/Fleece/IssuesControllerTests.cs` — extend with three new endpoint cases
- `tests/Homespun.Api.Tests/Features/IssuesApiTests.cs` — add integration cases for the round-trip undo/redo flow
- `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.test.ts` — re-add the undo/redo keybinding cases (deleted in the v3.1 upgrade)
- `src/Homespun.Web/src/features/issues/hooks/use-issue-history.test.ts` — **new**
- `src/Homespun.Web/e2e/` — optional smoke test: create-edit-undo-redo cycle through Playwright

**Affected code (no change but worth noting)**
- `Fleece.Core` 3.1.0's `IEventStore.AppendEventsAsync` is used directly to write inverses. The `IFleeceService` mutation API is the same one used for forward writes, so no library API change is needed — the difference is in *what events the inverse builder constructs*, not how they're appended.
- `fleece project` and the sync flow naturally pick up compensating-event pairs as part of the merged event set; no special-casing needed at projection time.

**Out of scope**
- Cross-session undo (would require a durable on-disk stack and conflict resolution against concurrent commits — not worth the complexity for a "short-lived workflow affordance" per issue `nr5lA9`).
- Undoing changes that have already been pushed and projected onto `main` (would require git history rewrite — explicitly out per `nr5lA9`).
- Stack-state UI beyond enable/disable button states (no undo-history dropdown; the original feature didn't have one and engagement was low).
- Per-user stacks within a single project. Single-user dev tool flavour; multi-user collisions accepted under best-effort semantics.
- Guard against concurrent edits invalidating an undo (the "field changed under us" case). Best-effort LWW per the design decision; the inverse appends regardless of current disk value.
