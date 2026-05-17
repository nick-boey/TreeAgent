## MODIFIED Requirements

### Requirement: Git-backed sync for .fleece/ files

The system SHALL support syncing `.fleece/` event-sourced storage with the git remote. Sync SHALL rely on git's natural merge semantics (per-session `change_{guid}.jsonl` files in `.fleece/changes/` have unique filenames so divergent edits never produce file-level conflicts) rather than a hand-rolled field-level merge. On the default branch only, sync SHALL invoke `fleece project` to compact change files into `.fleece/issues.jsonl`.

`.fleece/.active-change` and `.fleece/.replay-cache` SHALL remain in `.gitignore` (per-clone local state, not committed). The pre-commit hook installed by `fleece install` SHALL auto-stage `.fleece/changes/` on every commit, plus `.fleece/issues.jsonl` and `.fleece/tombstones.jsonl` on the default branch.

#### Scenario: Sync commits and pushes fleece changes
- **WHEN** `POST /api/fleece-sync/{projectId}/sync` is called
- **THEN** the server SHALL `git fetch origin`, `git merge --no-edit origin/<default>`, commit any staged `.fleece/` paths, push to the default branch, and reload the cache via `IProjectFleeceService.ReloadFromDiskAsync`

#### Scenario: Sync compacts on the default branch
- **WHEN** sync runs while the working tree is on the default branch
- **AND** the merge succeeds
- **THEN** the server SHALL shell out to `fleece project` to compact `.fleece/changes/` into `.fleece/issues.jsonl`
- **AND** SHALL commit the resulting snapshot in the same sync flow

#### Scenario: Sync on a feature branch does NOT compact
- **WHEN** sync runs while the working tree is on a non-default branch
- **THEN** the server SHALL NOT invoke `fleece project`
- **AND** change files SHALL remain in `.fleece/changes/` for compaction after the branch merges to main

#### Scenario: Divergent edits merge cleanly through event files
- **GIVEN** two clones have edited the same issue's different fields and committed locally
- **WHEN** one clone pulls the other's commit and runs `git merge`
- **THEN** the merge SHALL succeed without conflicts
- **AND** both per-clone `change_{guid}.jsonl` files SHALL coexist in `.fleece/changes/`
- **AND** replay SHALL produce the field-level last-writer-wins state for both edits

#### Scenario: Pull reloads cache from disk
- **WHEN** `POST /api/fleece-sync/{projectId}/pull` succeeds
- **THEN** the cache SHALL be reloaded via `IProjectFleeceService.ReloadFromDiskAsync`

#### Scenario: Non-fleece changes are reported
- **WHEN** sync detects non-`.fleece/` working-tree changes
- **THEN** `HasNonFleeceChanges` and `NonFleeceChangedFiles` SHALL be reported without committing them

#### Scenario: fleece project failure surfaces as warning, not hard failure
- **WHEN** the `fleece project` shell-out exits non-zero during sync on the default branch
- **THEN** the response SHALL still report success for the underlying merge+push
- **AND** SHALL include a warning that compaction was skipped
- **AND** SHALL log the failure to OTel for investigation

### Requirement: Client-side ordinal-string sortOrder midpoint aligned with Fleece.Core

The web client SHALL fabricate an ordinal-string sort key for the synthetic's parent edge using a midpoint-string algorithm that produces values matching `Fleece.Core`'s ordinal-string sort-order writer for any input pair. The algorithm SHALL be exposed at `src/Homespun.Web/src/features/issues/services/sort-order-midpoint.ts` as `midpoint(prev: string, next: string): string` and SHALL satisfy:

- `prev < midpoint(prev, next) < next` under the same ordinal-byte comparator the layout uses (`issue-layout-service.ts:121-131`);
- if no midpoint exists at the current length (adjacent codepoints), the result SHALL be one character longer with a midpoint character appended;
- `prev = ""` produces a value strictly less than `next`;
- `next = ""` (no successor) produces a value strictly greater than `prev`;
- `prev === next` SHALL fail-fast (caller bug).

The fabricated value SHALL be used only for the client-preview layout. The wire-level POST SHALL continue to send `parentIssueId` + `siblingIssueId` + `insertBefore`; the server retains authority over the canonical sort key.

The wire-format JSON property for the parent-edge sort key on `ParentIssueRef` SHALL be `lexOrder` (renamed from `sortOrder` in Fleece.Core 3.1). The TS layout-fixture port and any client-side `IssueResponse` typings SHALL read the `lexOrder` key. C# code SHALL continue to access the `SortOrder` property name on `Fleece.Core.Models.ParentIssueRef`; the property's serialized name is handled by `[JsonPropertyName("lexOrder")]` inside the library.

#### Scenario: midpoint produces a value strictly between neighbours
- **WHEN** `midpoint("a", "c")` is called
- **THEN** the result SHALL satisfy `"a" < result < "c"` under ordinal-byte comparison

#### Scenario: midpoint extends length when codepoints are adjacent
- **WHEN** `midpoint("a", "b")` is called
- **THEN** the result SHALL be a string of length 2 or more whose first character is `"a"` and whose remainder lands strictly between empty-suffix and `"b"`'s suffix at the comparator level

#### Scenario: parity with Fleece.Core writer for sample pairs
- **WHEN** the parity test runs against a fixed sample of 30+ pairs
- **THEN** the TS implementation SHALL produce the same value the C# `Fleece.Core` writer would emit for the same insertion request, byte-for-byte

#### Scenario: client and server preview agree on relative ordering
- **GIVEN** the user creates a pending issue between siblings `A` and `B`
- **WHEN** the SignalR `IssueChanged` echo arrives carrying the server's chosen sort key
- **THEN** the relative ordering of the new issue versus `A` and `B` SHALL match the client preview
- **AND** the rendered row position SHALL NOT jump

#### Scenario: Wire format uses lexOrder on ParentIssueRef
- **WHEN** a server response includes an issue with parent references
- **THEN** each parent reference SHALL serialize the sort key as `"lexOrder"` (not `"sortOrder"`)
- **AND** the TS port SHALL accept the `lexOrder` key

## REMOVED Requirements

### Requirement: Undo/redo issue history

**Reason**: The snapshot-based design (full-issue JSONL snapshots in a `.history/` sidecar, re-applied via raw JSONL writes through `ProjectFleeceService.ApplyHistorySnapshotAsync`) is incompatible with Fleece 3.1's event-sourced model — every write must flow through `IFleeceService` and reach the active change file. Re-applying a snapshot by writing `issues_{hash}.jsonl` directly is exactly the kind of "bypass the library" pattern this change removes. The feature was also best-effort: confirmed flaky in practice, with low user engagement.

**Migration**: The `/api/projects/{projectId}/issues/history/undo` and `/api/projects/{projectId}/issues/history/redo` endpoints are removed; clients should remove undo/redo UI affordances and keybindings. The follow-up redesign is tracked in Fleece issue `nr5lA9` ("Re-implement undo/redo via compensating events on Fleece 3.1.x"). The proposed compensating-event approach (append the inverse event to the active change file; keep an in-memory redo stack server-side) composes correctly with event replay and `fleece project` compaction, but its design — particularly cross-session semantics and behaviour around projection boundaries — needs its own change.
