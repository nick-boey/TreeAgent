## Context

Fleece 3.1's event store is the authoritative write path: every mutation is one or more events (`CreateEvent`, `SetEvent`, `AddEvent`, `RemoveEvent`, `HardDeleteEvent`) appended to the active `change_{guid}.jsonl` and replayed on read. Field-level LWW falls out of timestamp-ordered replay for free — the same property re-set twice resolves to the later value. That's the lever for compensating events: writing a `Set(id, prop, prior_value)` event with a fresh timestamp produces the same observable result as undoing the prior set.

The deleted v3.0 design wrote raw full-issue JSONL files into a `.history/` sidecar and re-applied them through `ProjectFleeceService.ApplyHistorySnapshotAsync`, bypassing the library. That's the exact "in-app divergence from `IFleeceService`" pattern the v3.1 upgrade explicitly removed. The redesign keeps every write — including undo writes — going through the event store.

Library API audited from `Fleece.Core.dll` 3.1.0:

```
Fleece.Core.EventSourcing.Services.IEventStore
  AppendEventsAsync(IEnumerable<IssueEvent>)
  AppendIssueAsync(Issue)                            ← composes events for create
  AppendTombstonesAsync(...)                          ← hard delete writes here
  GetActiveChangeFilePathAsync()
  ActiveChangePointer { get; }

Fleece.Core.EventSourcing.Events
  IssueEvent (base)
  CreateEvent      { issueId, fields...  }
  SetEvent         { issueId, property, value }
  AddEvent         { issueId, collection, value }     ← e.g. parent add
  RemoveEvent      { issueId, collection, value }     ← e.g. parent remove
  HardDeleteEvent  { issueId }
  MetaEvent        ← managed by the library, rejected from AppendEventsAsync

Fleece.Core.Models
  PropertyChange { property, oldValue, newValue }     ← shape used for diffs
```

`AppendEventsAsync` accepts any combination of events as one append. That's how a multi-event "logical step" (create + set status + add parent) gets undone atomically: build the N inverse events, append them in one call, push one stack entry.

The user explicitly accepted these design points in the exploration session:
1. **Per-project stack key** (no per-user dimension)
2. **Inverse of `Create` = soft-delete** (Set status=Deleted) rather than hard-delete via tombstones
3. **One HTTP call = one undoable step** (wrap N events as a single stack entry)
4. **Best-effort under concurrent edits** (append inverse with current timestamp; LWW wins or loses naturally)
5. **New edit truncates redo** (matches standard editor convention)

## Goals / Non-Goals

**Goals**
- Every undo/redo write flows through `IEventStore.AppendEventsAsync` — no raw file writes, no `IssueMerger`-style state copying.
- One HTTP-call-level user mutation = one stack entry, regardless of how many events it spawned.
- Compensating-event pairs collapse to no-op at `fleece project` time, so undo/redo storms don't bloat the projected snapshot.
- The frontend hook re-uses the deleted endpoint shape so the UI re-add is a near-revert of the deleted diff plus the new TanStack Query wiring.
- Mock-mode and sync-driven writes do NOT push undo entries (those aren't user actions).

**Non-Goals**
- Durable cross-session undo. Stack lives in process memory; restart clears it.
- Undoing across a `git push` / projection boundary. Once events have landed on `main` and compacted, they are no longer undoable from this client's stack.
- Per-user partitioning of stacks within a project.
- An "undo history" UI listing past steps. Buttons only.
- Conflict detection against concurrent edits. Best-effort LWW.
- A redo-stack that survives toggling between projects (single-project focus; switching projects keeps separate stacks but does not migrate state).

## Decisions

### D1. Per-project, in-memory stack keyed by `projectId`

```csharp
public interface IIssueUndoRedoService
{
    void PushInverse(string projectId, IssueOperationGroup forwardEvents,
                                       IssueOperationGroup inverseEvents);
    Task<IssueHistoryState> GetStateAsync(string projectId);
    Task<bool> UndoAsync(string projectId, CancellationToken ct);
    Task<bool> RedoAsync(string projectId, CancellationToken ct);
}

internal sealed record IssueOperationGroup(IReadOnlyList<IssueEvent> Events);

internal sealed class IssueUndoRedoService : IIssueUndoRedoService
{
    private readonly ConcurrentDictionary<string, ProjectUndoState> _stacks = new();
    private sealed class ProjectUndoState
    {
        public Stack<UndoEntry> Undo  { get; } = new();
        public Stack<UndoEntry> Redo  { get; } = new();
        public object SyncRoot       { get; } = new();
    }
    private sealed record UndoEntry(IssueOperationGroup Forward, IssueOperationGroup Inverse);
}
```

Both stacks are bounded at 100 entries (matches the deleted `MaxHistoryEntries` default) — when an entry overflows, the oldest entry is silently dropped from the bottom of the stack.

*Alternatives considered:*
- (a) Per-(project, userEmail). Cleaner multi-user semantics, but the dev-tool flavour and "users rarely exercised undo" make this overkill.
- (b) Durable on-disk stack at `.fleece/.undo-stack` (gitignored). Survives restart but introduces a second per-clone local-state file the user has to reason about. The original feature didn't survive restart either; behaviour parity is the simpler call.

*Rationale:* Per-project in-memory matches the simplicity of the deleted feature. The user explicitly accepted "server restart clears stack" as a tradeoff.

### D2. Capture-before-state lives inside `ProjectFleeceService`, not in the controller

The inverse builder needs the issue state *before* the forward write. The controller doesn't have it cleanly — it would have to call `GetIssueAsync` redundantly. `ProjectFleeceService` already loads the cache before every write. Add a thin wrapper that:

1. Reads the current state of the affected fields/edges from the cache.
2. Calls the existing write path (unchanged forward logic).
3. Constructs the inverse event group from the captured before-state.
4. Pushes the (forward, inverse) entry onto the stack.

```
                ┌─────────────────────────────────────────────────────┐
                │   IProjectFleeceService.UpdateIssueAsync            │
                │   (recordUndo: bool = true)                         │
                ├─────────────────────────────────────────────────────┤
  before  ──►   │   var before = cache[issueId]                       │
                │   var after  = await service.UpdateAsync(...)       │
                │   cache[issueId] = after                            │
                │                                                     │
  if recordUndo:│   var fwd   = DiffToEvents(before, after)           │
                │   var inv   = DiffToEvents(after, before)           │
                │   undoRedoService.PushInverse(projectId, fwd, inv)  │
                │                                                     │
                │   return after                                      │
                └─────────────────────────────────────────────────────┘
```

The `recordUndo` flag lets sync, conflict-resolution, mock-seeding, and agent-merge call sites opt out cleanly via a single parameter — no parallel API surface, no internal-vs-public split.

*Alternatives considered:*
- (a) Subscribe to `IEventStore` events after the fact and reconstruct inverses by diffing snapshots. Possible but requires a new library hook the package doesn't expose; also the timing is racy when multiple mutations interleave.
- (b) A controller-level decorator (`IIssueMutationOrchestrator`) that owns capture-before-state and forwarding. Cleaner separation but doubles the layer count for a single concern.

*Rationale:* The cache is already loaded; before-state is a hash-map read. Putting the inverse build at the write site is the lowest-friction integration. The `recordUndo` flag is the standard escape hatch for non-user writes.

### D3. Inverse-of-Create is soft-delete (Set status=Deleted), not hard-delete

```
   Forward:    Create(A, {title=X, type=Task, status=Open})
   Inverse:    Set(A, status, Deleted)

   Redo of original Create:
               Set(A, status, Open)             ← NOT a new Create event
                                                    (the issue already exists)
```

This keeps tombstones reserved for "actually gone forever" semantics. It also makes undo and redo of a create symmetric in cost: both are a single `Set` of status, plus whatever fields changed during the lifetime of the redo (none, if the user did nothing between undo and redo).

Caveat: if the user *deletes* an issue (the system soft-deletes by setting status=Deleted), then undoes, the inverse is `Set(id, status, <prior status>)`. That's the same shape as undoing a create, so the system doesn't need a special-case branch for "is this a create-undo or a delete-undo?" — both reduce to "set status to the prior value".

*Alternatives considered:*
- (a) Hard-delete on undo-of-create. Cleaner end state (issue truly gone from snapshot) but asymmetric: redo would need to *re-Create* with the original fields, including the original id, which Fleece.Core allows but feels heavier than a status flip.
- (b) Track a per-entry "this was a create" flag and dispatch on it. Adds complexity for no observable benefit.

*Rationale:* The user accepted soft-delete in the exploration session. Symmetric, cheap, and tombstones stay reserved for genuine hard deletes via `fleece` CLI or future explicit "purge issue" flows.

### D4. One HTTP call = one stack entry, regardless of event count

A `CreateIssueAsync` with a parent positioning emits a `CreateEvent` plus an `AddEvent(parents, parentId)` — both at the library level, both inside the same `ProjectFleeceService` method call. The stack pushes ONE `UndoEntry` containing both forward events and both inverse events.

Undo applies all inverses in one `AppendEventsAsync` call. Redo applies all forwards in one `AppendEventsAsync` call.

*Alternatives considered:* per-event entries (one push per individual `IssueEvent`). Trivial to capture but mashes Ctrl+Z forever to roll back a single "create child below this issue" action, which is what the user thinks of as one step.

*Rationale:* user-visible granularity = user mental model. Implementation is a single record wrapping a `List<IssueEvent>`.

### D5. Best-effort under concurrent edits (no `lastUpdate` guard)

If two agents/users mutate the same field concurrently, my undo blindly appends an inverse using the value I captured when *I* wrote it. Whether that value sticks depends entirely on LWW timestamp ordering: my undo carries `now`, which is newer than any prior write — so my undo wins. If a third party writes *after* my undo, theirs wins.

```
   t=1 :  Me        Set(A.status, Open)         pushed inverse Set(A.status, Draft)
   t=2 :  Coworker  Set(A.status, Review)       (I'm not aware)
   t=3 :  Me        Undo  →  AppendEvents(Set(A.status, Draft, ts=t3))
                    Replay: t1 Open → t2 Review → t3 Draft   ⇒  Draft wins
```

The coworker's change is overwritten. Best-effort, by design. The issue calls undo "a short-lived workflow affordance, not durable state," which the user confirmed.

*Alternatives considered:*
- Snapshot the `lastUpdate` of every affected field when pushing the inverse, refuse to undo if the disk version is newer. Adds a per-field metadata check (Fleece 3.1 dropped `*LastUpdate` from the projected snapshot but still tracks it in events).
- Two-phase undo: dry-run first, ask user to confirm if conflict detected.

*Rationale:* the cost of getting this right (re-introduce per-field `lastUpdate` tracking on the client side, or a new query path) outpaces the value. Single-user dev tool flavour. The user accepted LWW.

### D6. New edit after undo truncates the redo stack

Standard editor convention. Implementation: every `PushInverse` call clears the redo stack.

```
   Stack:  undo=[A, B, C]   redo=[]      ← current state

   Undo:   undo=[A, B]      redo=[C]
   Undo:   undo=[A]         redo=[C, B]

   New edit D:
           undo=[A, D]      redo=[]      ← B, C dropped on push
```

*Alternatives considered:* branching undo trees (preserve every undone path). Too rich for the use case; even fancy editors hide this behind a "history" panel which is explicitly out of scope.

*Rationale:* matches user expectation. Trivial to implement.

### D7. Sync, agent-merge, mock-seeding, and conflict-resolution paths skip the stack

These call sites use `recordUndo: false`:

- `FleeceIssuesSyncService` — `git merge` doesn't go through `ProjectFleeceService` mutations, but the cache-reload after merge could conceptually rewind the stack. Decision: leave the stack untouched (entries may reference issue states that no longer match disk, undo will be best-effort under D5).
- `FleeceChangeApplicationService` — `Manual` conflict resolution calls `IFleeceService.UpdateAsync` directly; route through `ProjectFleeceService.UpdateIssueAsync(... recordUndo: false)` instead.
- `MockIssueServiceAdapter` — mock seeding is fixture setup, not user action.
- `FleeceIssueSeeder` — same.

*Rationale:* "user-initiated edit" is the entire premise of undo. Anything else is noise.

### D8. Undo/redo broadcast a bulk `IssueChanged` event

```csharp
await notificationHub.BroadcastIssueChanged(
    projectId, IssueChangeType.Updated, issueId: null, issue: null);
```

A single user step can touch multiple issues (e.g. `MoveSeriesSiblingAsync` updates sort orders on every sibling under the parent). The deleted code broadcast a bulk event (null id) which the client handles as "invalidate every issue cache for this project". Keep that shape.

*Alternatives considered:* enumerate affected issue ids and emit one `Updated` per id.

*Rationale:* the cost of enumerating is non-zero and the client already handles bulk events correctly. Match the deleted behaviour; revisit only if a perf problem materialises.

## Risks / Trade-offs

[Risk: Stack entries reference issue states that no longer match disk after a sync]
→ Mitigation: best-effort LWW (D5) absorbs this. Worst case is an undo silently does nothing observable because the disk has moved on past the inverse's effect. Document it; the user accepted it.

[Risk: An undo of a `MoveSeriesSiblingAsync` is non-trivial because it cascades into multiple `SetEvent`s on `parentIssues[].sortOrder` of every sibling]
→ Mitigation: capture the full `ParentIssues` collection of every affected sibling before the move; restore each `sortOrder` via `SetEvent`s in the inverse. This is the most complex case; cover it with a dedicated unit test.

[Risk: A user creates an issue, then runs an agent that mutates it, then undoes — the undo erases the agent's edits]
→ Mitigation: this is correct behaviour per the LWW semantics. The user undid their own create; the agent's edits to a now-soft-deleted issue are still in the event stream but are masked by the inverse `Set(status, Deleted)`. If the user redoes, the issue resurfaces with the agent's edits intact (replay produces the field-level merge).

[Risk: Repeated undo/redo storms inflate the change file]
→ Mitigation: `fleece project` on the default branch compacts cancelling pairs at projection time (a `Set(A.title, X)` followed by `Set(A.title, X')` followed by `Set(A.title, X)` reduces to the final value). The change file grows on the feature branch but is bounded by the user's actual session length.

[Risk: Re-adding the controller endpoints expands the public OpenAPI surface that was deliberately removed in the upgrade]
→ Mitigation: this is intentional — `nr5lA9` is the redesign that brings it back. The OpenAPI regeneration is part of the task list; reviewers should see the three new endpoints in the diff.

[Risk: Frontend keybinding collision with browser shortcuts (`Cmd+Z` is universal browser undo)]
→ Mitigation: the handler in `use-toolbar-shortcuts.ts` calls `preventDefault` and ignores key events when an input/textarea has focus (matches the existing pattern). The deleted implementation worked the same way.

[Risk: Multi-tab clients see stale undo state]
→ Mitigation: undo state is server-side and the `GetStateAsync` endpoint is the source of truth. The TanStack Query hook polls only on mount / mutation; clients in different tabs may see different stack pointers briefly but eventually consistent. Acceptable for the use case.

## Migration Plan

This change is purely additive at the public-API level (three new endpoints, no removals or schema changes on existing endpoints). No data migration is required.

1. **Server: stack service + DI**
   - Add `IIssueUndoRedoService` + `IssueUndoRedoService` under `Features/Fleece/Services/`.
   - Register as singleton in DI.
   - Wire `IIssueUndoRedoService` into `IssuesController` constructor.

2. **Server: write-path wrapping**
   - Add the `recordUndo: bool = true` overload(s) to `IProjectFleeceService`.
   - Inside `ProjectFleeceService`, factor out a private "build inverse from before/after" helper and call it from every write method when `recordUndo` is true.
   - Update `FleeceChangeApplicationService` / `MockIssueServiceAdapter` to pass `recordUndo: false`.

3. **Server: endpoints**
   - Add the three controller methods inside a `#region History Operations` block (mirroring the deleted shape).
   - Recreate `IssueHistoryModels.cs` in `Homespun.Shared/Models/Fleece/` with `IssueHistoryState` (canUndo, canRedo, undoCount, redoCount) and `IssueHistoryOperationResponse` (success, errorMessage?, state).

4. **Web: hook + UI**
   - `npm run generate:api:fetch` to regenerate the typed client.
   - Add `use-issue-history.ts` (TanStack Query — `useQuery` for state, `useMutation` for undo/redo; invalidate `['issues', projectId]` on success).
   - Re-add `onUndo`, `onRedo`, `canUndo`, `canRedo` callbacks to `ToolbarShortcutCallbacks` in `use-toolbar-shortcuts.ts`. Re-add key handlers for `u`, `Ctrl+Z`, `Cmd+Z`, `Ctrl+Shift+Z`, `Cmd+Shift+Z` matching the deleted implementation.
   - Re-add the undo/redo buttons to the issues toolbar component with `disabled={!canUndo}` / `disabled={!canRedo}`.

5. **Tests**
   - Unit: `IssueUndoRedoServiceTests` covering push/undo/redo/clear-redo-on-push, bounded stack overflow, multi-event groups, status-based create inverse.
   - Unit: extend `IssuesControllerTests` with the three endpoint cases.
   - API: `IssuesApiTests` round-trip — create issue, edit, undo, GET shows reverted state, redo, GET shows re-applied state.
   - Web unit: `use-issue-history.test.ts` for the TanStack Query hook.
   - Web unit: extend `use-toolbar-shortcuts.test.ts` with the re-added keybindings.
   - Optional: Playwright e2e covering the toolbar buttons.

6. **Pre-PR checklist (unchanged but worth running clean)**
   - `dotnet test`
   - `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook`

**Rollback**: revert the PR. No data migration to undo. The compensating-event pairs already in change files are valid event sequences; they replay correctly even without the server-side stack present (the stack only governs which undos are *available*, not whether already-appended inverses are valid).

## Open Questions

- **Do we want to expose the undo stack as a SignalR signal so multi-tab clients can react to other tabs pushing/popping?** Leaning no for v1 — the TanStack Query `state` endpoint is polled on mutation and that covers the in-tab case. Cross-tab is a marginal use case; revisit if user feedback says it matters.
- **Should `MoveSeriesSiblingAsync`'s inverse capture every sibling's full `ParentIssues` collection, or just the moved issue's row?** Conservative answer: full collection of every affected sibling. The move can cascade `sortOrder` updates across N siblings under the same parent; restoring only the moved issue leaves the others mis-ordered. Cover with a dedicated unit test.
- **What's the right behaviour when `recordUndo: false` is passed but the call would have produced an undoable change?** Two options: (a) silently skip the push (current proposal), (b) log a debug warning so we can detect call-site bugs. Going with (a) for simplicity; reviewers can opt for (b) if the diff looks risky.
- **Does mock-mode need its own `IIssueUndoRedoService` registration or does the real one work?** The real one works — it's in-memory, no I/O, no Fleece dependency. Same instance can service both mock and live modes.
