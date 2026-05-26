## ADDED Requirements

### Requirement: Undo/redo issue history via compensating events

The system SHALL support undo and redo of user-initiated issue mutations using compensating events appended to the active change file. Undo/redo state SHALL be held in process memory, scoped per `projectId`, and SHALL NOT survive server restart. Each user-initiated HTTP mutation SHALL push exactly one stack entry, regardless of how many internal `IssueEvent`s it produced.

The system SHALL expose three endpoints under `/api/projects/{projectId}/issues/history`:

- `GET /state` SHALL return `IssueHistoryState { canUndo, canRedo, undoCount, redoCount }`.
- `POST /undo` SHALL pop the top of the undo stack, append its inverse events via `IEventStore.AppendEventsAsync`, push the original event group onto the redo stack, and return `IssueHistoryOperationResponse { success: true, state }`. When the undo stack is empty, the endpoint SHALL return `{ success: false, errorMessage: "Nothing to undo", state }`.
- `POST /redo` SHALL pop the top of the redo stack, append its forward events, push the corresponding undo entry back onto the undo stack, and return the same response shape. When the redo stack is empty, the endpoint SHALL return `{ success: false, errorMessage: "Nothing to redo", state }`.

Successful undo and redo SHALL broadcast a bulk `IssueChanged({ kind: 'updated', issueId: null, issue: null })` on `NotificationHub` so connected clients invalidate every issue cache for the project.

The inverse of an `IssueEvent` group SHALL be constructed as follows:

- For a `Create` operation, the inverse SHALL be `Set(id, status, Deleted)` (soft-delete). Redo SHALL re-apply `Set(id, status, <originalStatus>)` rather than re-emitting a `CreateEvent` (the issue already exists in the snapshot).
- For a scalar property update, the inverse SHALL be `Set(id, property, <beforeValue>)` for every property the forward write actually changed.
- For a soft-delete (`Set(id, status, Deleted)`), the inverse SHALL be `Set(id, status, <beforeStatus>)`.
- For an `AddParent` operation, the inverse SHALL be a `Remove` event on the `parentIssues` collection. For `RemoveParent`, the inverse SHALL be an `Add`.
- For `SetParent(addToExisting=false)` or `RemoveAllParents`, the inverse SHALL be a `Set` of the full prior `parentIssues` collection.
- For `MoveSeriesSibling`, the inverse SHALL capture the full prior `parentIssues` collection of every sibling whose `sortOrder` changed and emit a `Set` event per affected sibling.

The undo and redo stacks SHALL each be bounded at 100 entries. When a push would exceed the bound, the oldest entry at the bottom of the stack SHALL be silently dropped.

Any new user-initiated mutation SHALL clear the redo stack.

Undo SHALL operate under last-writer-wins semantics: the appended inverse events SHALL carry the current server timestamp, and the system SHALL NOT validate that the affected fields' on-disk values still match the values captured when the inverse was built. Concurrent edits from other users / agents MAY be overwritten by undo and MAY mask redo effects.

Mutations SHALL be excluded from the stack when triggered by non-user paths — specifically: git-sync–driven changes (`FleeceIssuesSyncService`), agent-merge / conflict-resolution writes (`FleeceChangeApplicationService` `Manual` path), mock-mode seeding (`MockIssueServiceAdapter`, `FleeceIssueSeeder`), and any future automated-write call sites. These call sites SHALL pass `recordUndo: false` (or its equivalent) through the `IProjectFleeceService` mutation surface.

#### Scenario: State endpoint reports empty stacks
- **GIVEN** no mutations have occurred in this server process
- **WHEN** a client calls `GET /api/projects/{projectId}/issues/history/state`
- **THEN** the response SHALL be `{ canUndo: false, canRedo: false, undoCount: 0, redoCount: 0 }`

#### Scenario: Undo reverses the most recent mutation via a compensating event
- **GIVEN** an issue `A` has been updated from `status=Open` to `status=Progress` by a user
- **WHEN** `POST /api/projects/{projectId}/issues/history/undo` is called
- **THEN** the server SHALL append `Set(A, status, Open)` to the active change file via `IEventStore.AppendEventsAsync`
- **AND** SHALL push the forward `Set(A, status, Progress)` onto the redo stack
- **AND** SHALL broadcast `IssueChanged({ kind: 'updated', issueId: null, issue: null })`
- **AND** the response SHALL be `{ success: true, state: { canUndo: false, canRedo: true, ... } }`

#### Scenario: Undo of a Create operation soft-deletes the issue
- **GIVEN** a user has just created issue `B` with `{ title: "X", type: Task, status: Open }`
- **WHEN** `POST /api/projects/{projectId}/issues/history/undo` is called
- **THEN** the server SHALL append `Set(B, status, Deleted)` (not a `HardDeleteEvent` against tombstones)
- **AND** the issue SHALL remain in the snapshot with `status=Deleted`
- **AND** a subsequent redo SHALL re-apply `Set(B, status, Open)` rather than re-emitting a `CreateEvent`

#### Scenario: Redo re-applies the undone state
- **GIVEN** the user has undone an edit to issue `C`
- **WHEN** `POST /api/projects/{projectId}/issues/history/redo` is called
- **THEN** the original forward events SHALL be appended via `IEventStore.AppendEventsAsync`
- **AND** the redo stack entry SHALL move back onto the undo stack
- **AND** the response SHALL be `{ success: true, state: { canUndo: true, ... } }`

#### Scenario: Empty undo stack returns success=false
- **WHEN** `POST /api/projects/{projectId}/issues/history/undo` is called and the stack is empty
- **THEN** the response SHALL be `{ success: false, errorMessage: "Nothing to undo", state: { canUndo: false, canRedo: <preserved>, ... } }`
- **AND** no events SHALL be appended

#### Scenario: New mutation truncates the redo stack
- **GIVEN** the user has undone three edits, producing `undoCount = N, redoCount = 3`
- **WHEN** the user makes a new edit through any HTTP mutation
- **THEN** the redo stack SHALL be cleared
- **AND** `GET /state` SHALL report `redoCount = 0`

#### Scenario: One HTTP mutation produces one stack entry regardless of internal event count
- **WHEN** a user creates an issue with a parent positioning (one HTTP call, internally `CreateEvent` + `AddEvent(parentIssues)`)
- **THEN** the undo stack SHALL grow by exactly 1 entry
- **AND** a subsequent undo SHALL apply both inverse events in a single `IEventStore.AppendEventsAsync` call

#### Scenario: Move-sibling undo restores every affected sibling's sortOrder
- **GIVEN** siblings `A`, `B`, `C` under parent `P` with sort orders `aaa`, `bbb`, `ccc`
- **WHEN** the user moves `B` down via `MoveSeriesSiblingAsync` and then undoes
- **THEN** the undo SHALL emit `Set(parentIssues)` events for every sibling whose `sortOrder` changed
- **AND** the resulting on-disk sort orders SHALL match the pre-move state byte-for-byte under ordinal-byte comparison

#### Scenario: Stack survives multi-event groups when individual events would conflict
- **GIVEN** a forward group `{ Set(D, status, Open), Add(D, parentIssues, P) }`
- **WHEN** the user undoes and then redoes
- **THEN** both inverses SHALL be applied as one append, and both forwards as one append
- **AND** the issue's final state SHALL match the state immediately after the original forward write

#### Scenario: Best-effort undo overwrites concurrent edits
- **GIVEN** user U1 sets `A.status=Open` at t=1 and pushes the inverse `Set(A.status, Draft)`
- **AND** user U2 sets `A.status=Review` at t=2
- **WHEN** U1 undoes at t=3
- **THEN** the server SHALL append `Set(A.status, Draft)` with a t=3 timestamp
- **AND** replay SHALL produce `A.status=Draft` (U1's undo wins via LWW)

#### Scenario: Server restart clears the stacks
- **GIVEN** undo and redo stacks contain entries
- **WHEN** the server process restarts
- **THEN** `GET /state` SHALL return `{ canUndo: false, canRedo: false, undoCount: 0, redoCount: 0 }`

#### Scenario: Bounded stack evicts oldest entries
- **GIVEN** the undo stack already contains 100 entries
- **WHEN** the user makes a new user-initiated mutation
- **THEN** the entry at the bottom of the stack SHALL be dropped
- **AND** the new entry SHALL be pushed at the top
- **AND** `undoCount` SHALL remain at 100

#### Scenario: Sync-driven changes do not enter the stack
- **WHEN** `POST /api/fleece-sync/{projectId}/sync` produces issue mutations via `git merge`
- **THEN** no entries SHALL be pushed onto the undo stack
- **AND** subsequent `GET /state` SHALL reflect only previous user mutations

#### Scenario: Agent-merge writes do not enter the stack
- **WHEN** `POST /api/issues-agent/{sessionId}/accept` applies an agent's change file via `git merge`
- **THEN** no entries SHALL be pushed onto the undo stack regardless of how many issues the agent changed

#### Scenario: Undo broadcasts a bulk IssueChanged event
- **WHEN** an undo or redo succeeds
- **THEN** the server SHALL broadcast `IssueChanged({ kind: 'updated', issueId: null, issue: null })` on `NotificationHub`
- **AND** clients SHALL invalidate every issue cache for the project on receipt
