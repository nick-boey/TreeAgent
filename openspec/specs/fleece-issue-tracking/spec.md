# fleece-issue-tracking

## Purpose

Homespun's integration with the external Fleece library for local, file-based issue tracking. Provides an HTTP API over `Fleece.Core`'s event-sourced storage (`.fleece/issues.jsonl` snapshot + per-session `.fleece/changes/change_{guid}.jsonl` event logs), a project-aware cache, a git-backed sync layer that relies on `git merge` and `fleece project` compaction, an Issues Agent session flow for Claude-driven issue mutations (with diff + accept/conflict review), and an interactive task-graph UI. All issues live under `.fleece/` in the project working tree.
## Requirements
### Requirement: Issue CRUD API surface

The system SHALL expose issue CRUD over `/api` with endpoints for list, get, create, update, and delete, scoped to projects. List operations SHALL use `GET /api/projects/{projectId}/issues` returning the visible issue set (per the "Visible issue set endpoint" requirement).

#### Scenario: List issues for a project
- **WHEN** a client calls `GET /api/projects/{projectId}/issues`
- **THEN** the response SHALL contain the visible issue set per the "Visible issue set endpoint with ancestor-of-active filter" requirement

#### Scenario: Create issue with hierarchy positioning
- **WHEN** a client POSTs to `/api/issues` with a valid `CreateIssueRequest`
- **THEN** the server SHALL persist the issue via `Fleece.Core`
- **AND** SHALL broadcast `IssueChanged({kind: 'created', issueId, issue})` on `NotificationHub`
- **AND** SHALL return the issue with timestamps

#### Scenario: Create issue queues branch-id generation
- **WHEN** no `workingBranchId` is provided but a `title` is present
- **THEN** `IBranchIdBackgroundService.QueueBranchIdGenerationAsync` SHALL be invoked

#### Scenario: Create issue with parent positioning
- **WHEN** a `parentIssueId` is provided with optional `siblingIssueId` and `insertBefore`
- **THEN** `AddParentAsync` SHALL be invoked for hierarchy positioning

#### Scenario: Update auto-assigns current user
- **WHEN** `PUT /api/issues/{issueId}` is called and the issue has no assignee
- **AND** the request doesn't specify one and `dataStore.UserEmail` is configured
- **THEN** the server SHALL auto-assign the current user's email
- **AND** SHALL broadcast `IssueChanged({kind: 'updated', issueId, issue})` regardless of whether fields were "patchable" or "topology-affecting" — the split is removed

### Requirement: Hierarchy management with cycle detection

The system SHALL support set-parent, remove-parent, remove-all-parents, and move-sibling operations with cycle detection. All hierarchy mutations SHALL emit `IssueChanged({kind: 'updated', ...})` on success.

#### Scenario: Set parent succeeds for valid relationship
- **WHEN** a client POSTs to `/api/issues/{childId}/set-parent` with a valid parent
- **THEN** the parent SHALL be set and `IssueChanged({kind: 'updated', issueId: childId, issue})` SHALL be broadcast

#### Scenario: Cycle detection rejects invalid relationship
- **WHEN** a set-parent would create a cycle
- **THEN** the response SHALL be `400 Bad Request` with the cycle message from `Fleece.Core`

#### Scenario: Move sibling rejects invalid conditions
- **WHEN** `move-sibling` is called on an issue with multiple parents or no parent
- **THEN** the response SHALL be `400 Bad Request`

### Requirement: Agent-run surface with atomic deduplication

The system SHALL expose `POST /api/issues/{issueId}/run` returning `202 Accepted` with atomic duplicate prevention.

#### Scenario: First agent run returns 202
- **WHEN** no active session exists for the issue
- **THEN** the branch name SHALL be resolved, the agent SHALL be queued for background startup
- **AND** the response SHALL be `202 Accepted`

#### Scenario: Duplicate agent run returns 409
- **WHEN** an active session already exists for the issue
- **THEN** the response SHALL be `409 Conflict` with `AgentAlreadyRunningResponse`

### Requirement: Issues Agent session lifecycle

The system SHALL support creating Issues Agent sessions, computing diffs, accepting/resolving conflicts, and cancelling.

#### Scenario: Create Issues Agent session
- **WHEN** `POST /api/issues-agent/session` is called
- **THEN** the server SHALL pull latest main, create a clone, start a session of type `IssueAgentModification`

#### Scenario: Get diff between main and session branches
- **WHEN** `GET /api/issues-agent/{sessionId}/diff` is called
- **THEN** the server SHALL compare `.fleece/` issues and return per-issue `IssueChangeDto` entries

#### Scenario: Accept changes with conflict resolution
- **WHEN** `POST /api/issues-agent/{sessionId}/accept` is called
- **THEN** `IFleeceChangeApplicationService.ApplyChangesAsync` SHALL run
- **AND** conflicts SHALL be surfaced via `IssueConflictDto`

#### Scenario: Cancel discards session changes
- **WHEN** `POST /api/issues-agent/{sessionId}/cancel` is called
- **THEN** the session SHALL be stopped and its clone cleaned up

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

### Requirement: Task graph filter query language

The filter query parsed by `filter-query-parser.ts` SHALL support structured predicates and free-text search.

#### Scenario: Filter by status, type, priority, assignee
- **WHEN** the user types `type:bug priority:1` into the toolbar filter
- **THEN** only matching issues SHALL remain in the graph

#### Scenario: Me keyword resolves to current user
- **WHEN** the filter contains `assignee:me`
- **THEN** `me` SHALL resolve to the configured `userEmail`

#### Scenario: Free-text searches title and description
- **WHEN** the filter contains plain text without a predicate
- **THEN** it SHALL match against issue `title` and `description`

### Requirement: Visible issue set endpoint with ancestor-of-active filter

The system SHALL expose `GET /api/projects/{projectId}/issues` returning `IReadOnlyList<IssueResponse>` — the project's visible issue set. The endpoint SHALL accept the following optional query parameters:

- `include` — comma-separated issue ids to include in the response **regardless of status**, plus all of their ancestors (transitively). Empty or omitted means no overrides.
- `includeOpenPrLinked` — boolean; when `true`, every issue id linked to an open PR (per `IPullRequestStateService.GetOpenPrLinkedIssueIds(projectId)`) SHALL be included in the response, plus all of their ancestors.
- `includeAll` — boolean; when `true`, the visibility filter SHALL be bypassed entirely and all issues SHALL be returned (preserves the legacy "list all issues" behaviour for internal callers).
- `status` / `type` / `priority` — preserved as-is. When `includeAll=false` (default), these filters apply *after* visibility filtering. When `includeAll=true`, they apply to the raw list.

The "visible set" SHALL be computed as the transitive parent-closure of the seed set, where seeds = `{ issues with Status ∈ { Draft, Open, Progress, Review } } ∪ explicitInclude ∪ (includeOpenPrLinked ? openPrLinkedIds : ∅)`. The traversal SHALL use a visited set to guarantee O(N) cost and inherent cycle safety.

The endpoint SHALL return issues only. It SHALL NOT include decoration data (`agentStatuses`, `linkedPrs`, `openSpecStates`, `mergedPrs`, `orphanChanges`). It SHALL NOT compute or return any layout output (no `Lane`, `Row`, `Edges`, `TotalLanes`, `TotalRows`).

#### Scenario: Open issues are returned without their closed siblings
- **WHEN** a project has issues `A (Open)` and `B (Closed)` with no parent relationship between them and neither has open descendants
- **THEN** `GET /api/projects/{projectId}/issues` SHALL return `[A]`
- **AND** `B` SHALL NOT appear in the response

#### Scenario: Closed ancestor of open descendant is included
- **WHEN** a project has `Root (Closed) → Child (Closed) → Leaf (Open)`
- **THEN** the response SHALL include all three issues

#### Scenario: Closed leaf is excluded when no descendant is open
- **WHEN** a project has `Root (Open) → Child (Closed)` with no further descendants
- **THEN** the response SHALL include `Root` only

#### Scenario: Multi-parent diamond pulls in all ancestors
- **WHEN** an open issue `X` has two parents `A (Closed)` and `B (Closed)`, each rooted at the same grandparent `G (Closed)`
- **THEN** the response SHALL include `X`, `A`, `B`, and `G`

#### Scenario: Cycle in parent chain returns visible set without exception
- **WHEN** a project's parent graph contains a cycle (defensively present despite Fleece's cycle detection on writes)
- **THEN** the endpoint SHALL return a 200 response with a valid issue set, traversing each ancestor at most once
- **AND** SHALL NOT throw or return 500

#### Scenario: Explicit include override pulls in closed issue and its ancestors
- **WHEN** a client sends `GET /api/projects/{projectId}/issues?include=closed-issue-id`
- **AND** `closed-issue-id` exists with `Status = Complete` and parent `closed-parent-id`
- **THEN** the response SHALL include both `closed-issue-id` and `closed-parent-id`

#### Scenario: includeOpenPrLinked pulls in PR-linked issues and their ancestors
- **WHEN** a client sends `GET /api/projects/{projectId}/issues?includeOpenPrLinked=true`
- **AND** an open PR is linked to a `Status = Closed` issue whose parent is also closed
- **THEN** the response SHALL include the PR-linked issue and its closed parent

#### Scenario: includeAll=true bypasses the visibility filter
- **WHEN** a client sends `GET /api/projects/{projectId}/issues?includeAll=true`
- **THEN** the response SHALL include every issue in the project regardless of status
- **AND** parent-closure traversal SHALL NOT run

#### Scenario: status filter applies after visibility filter
- **WHEN** a client sends `GET /api/projects/{projectId}/issues?status=Progress`
- **THEN** the response SHALL include only issues with `Status = Progress` from the visible set
- **AND** SHALL NOT include `Progress` issues that were excluded by visibility (e.g. impossible by definition since `Progress` is in the open seed)
- **AND** SHALL NOT include closed-ancestor issues even if they would otherwise be in the visible set

#### Scenario: Endpoint returns issues only, no decorations
- **WHEN** the endpoint returns a 200 response
- **THEN** the response body SHALL be a JSON array of `IssueResponse` objects directly (not wrapped in an envelope)
- **AND** the response body SHALL NOT contain `agentStatuses`, `linkedPrs`, `openSpecStates`, `mergedPrs`, or `orphanChanges` fields

#### Scenario: Empty project returns empty array
- **WHEN** a project has no issues
- **THEN** the response SHALL be `[]`
- **AND** SHALL return 200 (not 404)

### Requirement: Linked PRs endpoint

The system SHALL expose `GET /api/projects/{projectId}/linked-prs` returning `IReadOnlyDictionary<string, LinkedPr>` keyed by Fleece issue id. Each entry SHALL contain `Number: int`, `Url: string`, `Status: string` (PR status enum string-name).

Source data: `IDataStore.GetPullRequestsByProject(projectId)` filtered by entries that have both `FleeceIssueId` (non-empty) and `GitHubPRNumber` (non-null). Entries without both SHALL be excluded.

The endpoint SHALL be independently testable: an integration test for this endpoint SHALL not require seeding agent sessions, OpenSpec changes, or graph layout — only the PR-state data store.

#### Scenario: Returns map keyed by Fleece issue id
- **WHEN** the project has a tracked PR with `FleeceIssueId = "issue-1"` and `GitHubPRNumber = 42`
- **THEN** the response SHALL contain key `"issue-1"` mapping to `{Number: 42, Url: "...", Status: "..."}`

#### Scenario: PR without FleeceIssueId is excluded
- **WHEN** the project has a tracked PR with no `FleeceIssueId`
- **THEN** the response SHALL NOT contain an entry for that PR

#### Scenario: PR without GitHubPRNumber is excluded
- **WHEN** the project has a tracked PR with `FleeceIssueId` but no `GitHubPRNumber`
- **THEN** the response SHALL NOT contain an entry for that PR

#### Scenario: Empty project returns empty map
- **WHEN** the project has no tracked PRs
- **THEN** the response SHALL be `{}` and 200

### Requirement: Agent statuses endpoint

The system SHALL expose `GET /api/projects/{projectId}/agent-statuses` returning `IReadOnlyDictionary<string, AgentStatusData>` keyed by Fleece issue id (the `EntityId` of the session).

Source data: `ISessionStore.GetByProjectId(projectId)`. Sessions SHALL be filtered to those with non-empty `EntityId` and grouped by `EntityId`. When multiple sessions share an `EntityId`, the most recent by `LastActivityAt` SHALL be selected.

The endpoint SHALL be independently testable: a test SHALL not require seeding issues, PRs, OpenSpec changes, or graph layout — only the session store.

#### Scenario: Active session returns one entry per issue
- **WHEN** an active session exists with `EntityId = "issue-1"`
- **THEN** the response SHALL contain key `"issue-1"` mapping to the session's `AgentStatusData`

#### Scenario: Multiple sessions for one issue: most-recent wins
- **WHEN** two sessions share `EntityId = "issue-1"` with different `LastActivityAt` timestamps
- **THEN** the response SHALL contain a single entry derived from the more recent session

#### Scenario: Session without EntityId is excluded
- **WHEN** a session has empty or null `EntityId`
- **THEN** the response SHALL NOT contain an entry for it

#### Scenario: Empty project returns empty map
- **WHEN** the project has no sessions
- **THEN** the response SHALL be `{}` and 200

### Requirement: Client-side graph layout via TypeScript port of Fleece.Core

The web client SHALL compute task-graph layout (lane assignment, row assignment, edge generation, multi-parent appearance counts) entirely client-side via a TypeScript port of `Fleece.Core.GraphLayoutService<TNode>` and `IssueLayoutService`. The port SHALL live under `src/Homespun.Web/src/features/issues/services/layout/` and SHALL expose:

- `layoutForTree(issues, options): GraphLayoutResult<Issue>` — issue-tree layout, equivalent to `IIssueLayoutService.LayoutForTree(InactiveVisibility.Hide)`.
- `layoutForNext(issues, matchedIds, options): GraphLayoutResult<Issue>` — next-mode layout, equivalent to `IIssueLayoutService.LayoutForNext`.

`GraphLayoutResult<T>` SHALL be a discriminated union of `{ ok: true; layout: GraphLayout<T> }` and `{ ok: false; cycle: string[] }`. The cycle case carries the cycle path (issue ids in order) as reported by the algorithm.

The port SHALL produce structurally-identical output to Fleece.Core for any input shared between them: same node row/lane assignments, same edge `kind`/`pivotLane`/attach-side values, same multi-parent appearance ordering. Equivalence SHALL be enforced by a cross-stack golden-fixture test (see "Cross-stack golden-fixture parity tests" requirement).

The web client SHALL NOT receive lane/row/edge data from the server. The web client SHALL NOT call any deleted `/api/graph/{projectId}/*` endpoints.

#### Scenario: layoutForTree assigns rows in post-order emission
- **WHEN** `layoutForTree` is called with a 3-node series chain `A → B → C` (parent → child → grandchild)
- **THEN** the returned `nodes[]` SHALL have `C` at row 0, `B` at row 1, `A` at row 2 (children emit before parents)
- **AND** lanes SHALL be `C: 0, B: 1, A: 2` for IssueGraph mode (leaf at lane 0)

#### Scenario: layoutForTree emits SeriesSibling and SeriesCornerToParent edges
- **WHEN** `layoutForTree` is called with a parent + 3 series children
- **THEN** `edges[]` SHALL contain 2 `SeriesSibling` edges (child-to-child) and 1 `SeriesCornerToParent` edge (last-child-to-parent)

#### Scenario: layoutForTree emits ParallelChildToSpine for parallel children
- **WHEN** `layoutForTree` is called with a parent + 3 parallel children
- **THEN** `edges[]` SHALL contain 3 `ParallelChildToSpine` edges
- **AND** all 3 children SHALL share the same starting lane

#### Scenario: Multi-parent issue has appearanceIndex and totalAppearances
- **WHEN** an issue `X` has two parents `A` and `B`, both rendered in the layout
- **THEN** `nodes[]` SHALL contain two `PositionedNode` entries for `X` with `appearanceIndex` 1 and 2 and `totalAppearances` 2

#### Scenario: Cycle returns failure result
- **WHEN** `layoutForTree` is called with input containing a parent cycle
- **THEN** the result SHALL be `{ ok: false, cycle: [...] }` with the cycle path in order
- **AND** the renderer SHALL surface this as a degraded-mode banner without crashing

#### Scenario: Empty input returns empty layout
- **WHEN** `layoutForTree` is called with `[]`
- **THEN** the result SHALL be `{ ok: true, layout: { nodes: [], edges: [], totalRows: 0, totalLanes: 0 } }`

#### Scenario: layoutForNext pulls in ancestors of matched leaves
- **WHEN** `layoutForNext` is called with issues `A → B → C` and `matchedIds = {C}`
- **THEN** the layout SHALL include `A`, `B`, and `C`

#### Scenario: ViewMode toggle is a pure client transformation
- **WHEN** the user toggles between Tree and Next modes
- **THEN** the web client SHALL re-run the layout port with different parameters against the cached issue set
- **AND** SHALL NOT issue any network request as part of the toggle

### Requirement: Cross-stack golden-fixture parity tests

The repository SHALL maintain a set of layout fixture inputs and corresponding C#-emitted reference outputs to detect drift between Fleece.Core and the TypeScript port. Fixtures SHALL live under `tests/Homespun.Web.LayoutFixtures/fixtures/` as paired `*.input.json` (issue set) and `*.expected.json` (layout output) files.

The fixture-emitter test (`tests/Homespun.Web.LayoutFixtures/EmitFixturesTests.cs`) SHALL run against the live `IIssueLayoutService` from the Fleece.Core dependency. With `UPDATE_FIXTURES=1` the test SHALL write `*.expected.json` files; without it the test SHALL compare emitted output against the existing files and assert structural equality.

A TypeScript test (`golden-fixtures.test.ts`) SHALL load each `*.input.json`, run the TS port, and structurally diff against the corresponding `*.expected.json`. Mismatches SHALL fail the test.

The fixture set SHALL cover at minimum:

- Simple tree, deep tree, multi-parent diamond, series chain, parallel children, mixed series/parallel siblings, cycle (failure case), empty input, single node, large input, `LayoutForNext` matched-leaves scenario, `LayoutForNext` large input.

When the project upgrades the `Fleece.Core` NuGet package, the workflow SHALL be: run `dotnet test --filter Category=Fixtures /p:UpdateFixtures=true`, review the diff in `*.expected.json`, update the TS port to match if the algorithm changed, and ship the upgrade with both fixtures and port aligned.

#### Scenario: Read-only fixture test catches algorithm drift
- **WHEN** the fixture-emitter test runs without `UPDATE_FIXTURES=1` after a Fleece.Core upgrade that changed lane assignment
- **THEN** the test SHALL fail with a structural diff highlighting the changed nodes/edges
- **AND** the failure message SHALL identify which fixture(s) drifted

#### Scenario: TypeScript golden-fixture test catches port regressions
- **WHEN** `npm test` runs `golden-fixtures.test.ts` and the TS port produces output that differs from `*.expected.json`
- **THEN** the test SHALL fail with the structural diff
- **AND** the test SHALL identify which fixture(s) the port disagrees with

### Requirement: Arc-cornered orthogonal edge rendering

The web client's edge renderer (`task-graph-svg.tsx::buildEdgePath`) SHALL produce orthogonal SVG paths with quarter-circle arcs at every direction change instead of hard right-angle corners. The corner radius SHALL be `min(6px, halfLaneWidth, halfRowHeight)` to ensure arcs never overflow lane or row boundaries.

The renderer SHALL handle three `EdgeKind` values:

- `series-sibling`: vertical line between sibling rows. May render straight (no corners) when source and target attach sides are co-linear; otherwise applies a single corner arc at the bend.
- `series-corner-to-parent`: vertical-then-horizontal path with one corner arc at the bend.
- `parallel-child-to-spine`: horizontal-to-pivot-then-vertical-then-horizontal path with two corner arcs (one at each direction change).

The renderer SHALL preserve lane fidelity — every path segment is axis-aligned except at corner arcs. No bezier interpolation is used.

#### Scenario: Right-angle corner renders as quarter-circle arc
- **WHEN** an edge has kind `series-corner-to-parent` with start `(0, 100)` and end `(50, 200)` and corner at `(0, 200)`
- **THEN** the SVG path SHALL contain a `Q` (or equivalent arc) command at the corner with radius ≤ 6px

#### Scenario: Corner radius clips to half-lane spacing in tight layouts
- **WHEN** the lane width is 8px
- **THEN** the corner radius SHALL clip to 4px to prevent the arc overflowing lane boundaries

#### Scenario: Pure-vertical sibling edge renders without corners
- **WHEN** a `series-sibling` edge has start `(50, 100)` and end `(50, 200)` (same lane)
- **THEN** the SVG path SHALL be a straight `M 50 100 L 50 200` with no `Q` commands

### Requirement: Unified IssueChanged SignalR event with idempotent client merge

The server SHALL emit a single SignalR event `IssueChanged` for every issue mutation, replacing the previous split between `IssuesChanged` (topology) and `IssueFieldsPatched` (field patch). The event payload SHALL be:

```
{
  projectId: string,
  kind: 'created' | 'updated' | 'deleted' | 'bulk-changed',
  issueId: string | null,    // null for 'bulk-changed'
  issue: IssueResponse | null  // present for 'created' and 'updated'; null for 'deleted' and 'bulk-changed'
}
```

The server SHALL emit this event from a single hub-extension method `BroadcastIssueChanged(projectId, kind, issueId, issue)` reused by every mutation site. The method SHALL NOT depend on any task-graph snapshot store (the snapshot infrastructure is removed by this change).

The web client's `useIssues` hook SHALL apply the event to its local issue cache idempotently:

- `created` / `updated`: replace by `issueId` in the cache.
- `deleted`: remove from the cache by `issueId`.
- `bulk-changed`: refetch the full issue set from `GET /api/projects/{projectId}/issues`.

Echo handling: when the client triggers a mutation via `POST /api/issues` (or any mutation route), it SHALL apply the response body to the cache; the same cache MAY also receive an `IssueChanged` echo for the same mutation. Both writes SHALL be applied without dedup. Replace-by-id is idempotent and the second write produces no observable state change.

#### Scenario: Created event adds the issue to the cache
- **WHEN** the client receives `IssueChanged({kind: 'created', issueId: 'abc', issue: {...}})`
- **THEN** the local cache SHALL contain the new issue keyed by `'abc'`

#### Scenario: Deleted event removes the issue from the cache
- **WHEN** the client receives `IssueChanged({kind: 'deleted', issueId: 'abc', issue: null})`
- **THEN** the cache SHALL no longer contain an entry keyed by `'abc'`

#### Scenario: Local mutation applies POST response and SignalR echo
- **WHEN** the client sends `POST /api/issues` and receives a 200 with the canonical issue
- **AND** subsequently receives `IssueChanged({kind: 'created', ...})` for the same issue
- **THEN** both writes SHALL apply to the cache without error
- **AND** the final cache state SHALL be identical to applying either write alone (idempotency)

#### Scenario: Bulk-changed event triggers refetch
- **WHEN** the client receives `IssueChanged({kind: 'bulk-changed', issueId: null, issue: null})` (e.g. from `FleeceIssueSyncController.Pull`)
- **THEN** the client SHALL invalidate the `['issues', projectId, ...]` query and refetch the full issue set

#### Scenario: SignalR reconnect refetches the issue set
- **WHEN** the SignalR connection drops and reconnects
- **THEN** `useIssues` SHALL invalidate its cache and refetch from `GET /api/projects/{projectId}/issues`

### Requirement: Pending issue rendered as a virtual layout participant

When the user is creating a new issue inline in the task graph (`o` / `shift+o`), the web client SHALL inject a synthetic "pending issue" node into the layout pipeline so that the issue's row, lane, and edges are computed by the same `IssueLayoutService` that lays out real issues. The synthetic SHALL be a discriminated `LayoutNode` of kind `'pending-issue'` (`PendingIssueLayoutNode`) defined in `src/Homespun.Web/src/features/issues/services/layout/nodes.ts` alongside the existing `'issue'` kind.

The synthetic SHALL:

- be the single source of truth for the in-progress new issue's hierarchy and visual position; the legacy `renderInlineEditor()` graft path that splices `<InlineIssueEditor>` into the row list at a fixed `insertAtIndex` SHALL be removed;
- carry `childSequencing: 'series'` (it has no children at edit time) and a `parentIssues` slot consumed by `IssueLayoutService.runEngine` for children-bucket placement;
- never be exposed to the layout-fixture parity tests in `tests/Homespun.Web.LayoutFixtures/` — synthetic injection is TS-only and the C# reference layout MUST NOT receive a `pending-issue` node;
- always render regardless of any active task graph filter, search query, or next-mode `marker !== Actionable` predicate.

The web client SHALL NOT introduce a parallel layout path or a second engine entry point for the synthetic. `computeLayoutFromIssues({ issues, …, pendingIssue? })` SHALL remain the single layout entry point; injection happens by appending a synthetic `LayoutIssue` to the engine input.

#### Scenario: synthetic is positioned as sibling-below in default state
- **WHEN** the user presses `o` while issue `S` is selected
- **THEN** `computeLayoutFromIssues` SHALL be called with `pendingIssue` describing `mode = 'sibling-below'` referencing `S`
- **AND** the synthetic SHALL appear in the rendered layout immediately below `S` with the same parent and lane as a sibling of `S`
- **AND** the synthetic's row SHALL be rendered by mounting `<InlineIssueEditor>` at the engine-assigned row/lane

#### Scenario: synthetic node never appears in golden fixtures
- **WHEN** the layout-fixture parity tests under `tests/Homespun.Web.LayoutFixtures/` run
- **THEN** none of the input fixtures SHALL contain a `pending-issue` node
- **AND** the C# reference layout SHALL NOT be expected to emit `pending-issue` output

#### Scenario: filter / search / actionable bypass keeps synthetic visible
- **WHEN** an active filter or search query reduces the visible set, OR the view is in next mode and the synthetic's reference is non-actionable
- **THEN** the post-layout filter pass SHALL preserve any render line where `isPendingIssueRenderLine(line)` is true
- **AND** the synthetic SHALL render and SHALL be scrolled into view

#### Scenario: legacy renderInlineEditor graft path is removed
- **WHEN** the codebase is built with this change applied
- **THEN** there SHALL be no `renderInlineEditor()` function (or equivalent DOM-graft path) that splices `<InlineIssueEditor>` into the row list outside the layout-engine output
- **AND** the `PendingNewIssue` type SHALL NOT contain `pendingChildId` or `pendingParentId` fields

### Requirement: Hierarchy state machine for inline issue creation

The web client SHALL drive synthetic-node hierarchy via a 3-state-per-`o`-press machine. The state machine values, transitions, and per-mode mappings SHALL be exactly:

```
TREE MODE
  o          : sibling-below S  ─⭾→ child-of S   (cancel: ⇧⭾ → sibling-below S)
  ⇧o         : sibling-above S  ─⇧⭾→ parent-of S  (cancel: ⭾  → sibling-above S)
                                       (replaces S's primary parent edge)

NEXT MODE
  o          : sibling-below S  ─⭾→ parent-of S   (cancel: ⇧⭾ → sibling-below S)
                                       (replaces S's primary parent edge)
  ⇧o         : sibling-above S  ─⇧⭾→ child-of S   (cancel: ⭾  → sibling-above S)
```

For every transition, the web client SHALL recompute the synthetic's `parentIssues[]` and (when entering a `parent-of` state) patch a copy of the reference issue's `parentIssues[]` so that `S` becomes a child of the synthetic and the synthetic inherits `S`'s prior slot under its old parent. The reference patch SHALL be local to the layout call — it MUST NOT mutate the cached `IssueResponse`.

All other key combinations while editing SHALL be no-ops (e.g. `o` then `Shift+Tab` from the default sibling state does nothing). Pressing the active promotion key a second time SHALL also be a no-op (e.g. `Tab` after entering `child-of` does not deepen further).

#### Scenario: Tree mode `o + Tab` makes synthetic a child of S
- **WHEN** the user presses `o` while `S` is selected, then presses `Tab`
- **THEN** the synthetic's `parentIssues[0].parentIssue` SHALL equal `S.id`
- **AND** the synthetic SHALL render below `S` indented as a child of `S`
- **AND** there SHALL be no visual jump in the synthetic's row position compared to its sibling-below default

#### Scenario: Tree mode `Shift+O + Shift+Tab` reparents S under synthetic
- **GIVEN** issue `S` has a parent `P` with sortOrder `s_old` under `P`
- **WHEN** the user presses `Shift+O` while `S` is selected, then presses `Shift+Tab`
- **THEN** the synthetic's `parentIssues[0]` SHALL equal `{ parentIssue: P.id, sortOrder: s_old }` (taking S's old slot)
- **AND** the patched `S` (in the layout-only copy) SHALL have `parentIssues[0] = { parentIssue: synthetic.id, sortOrder: <fabricated> }`
- **AND** the synthetic SHALL render above `S` as `S`'s parent

#### Scenario: Next mode `o + Tab` makes synthetic the parent of S
- **GIVEN** the view mode is Next and issue `S` has a parent `P` with sortOrder `s_old` under `P`
- **WHEN** the user presses `o` while `S` is selected, then presses `Tab`
- **THEN** the synthetic's `parentIssues[0]` SHALL equal `{ parentIssue: P.id, sortOrder: s_old }`
- **AND** `S` SHALL be reparented under the synthetic
- **AND** the synthetic SHALL render below `S` (consistent with next-mode parent placement)

#### Scenario: Next mode `Shift+O + Shift+Tab` makes synthetic a child of S
- **WHEN** the view mode is Next and the user presses `Shift+O` while `S` is selected, then presses `Shift+Tab`
- **THEN** the synthetic's `parentIssues[0].parentIssue` SHALL equal `S.id`
- **AND** the synthetic SHALL render above `S` (consistent with next-mode child placement)

#### Scenario: Cancel-back-to-sibling reverts the synthetic
- **GIVEN** the synthetic is in any `child-of` or `parent-of` state
- **WHEN** the user presses the cancel key for the active sequence (`Shift+Tab` after `o+Tab`, `Tab` after `Shift+O+Shift+Tab`)
- **THEN** the synthetic SHALL revert to the corresponding default `sibling-below` or `sibling-above` state
- **AND** any reference-issue patch from a prior `parent-of` transition SHALL be undone

#### Scenario: Wrong-direction key from default state is a no-op
- **GIVEN** the synthetic is in `sibling-below` state after pressing `o`
- **WHEN** the user presses `Shift+Tab`
- **THEN** the synthetic state SHALL NOT change and the layout SHALL NOT be re-run

#### Scenario: Repeat promotion key is a no-op
- **GIVEN** the synthetic is in `child-of` state
- **WHEN** the user presses `Tab` again
- **THEN** the synthetic state SHALL NOT change and the layout SHALL NOT be re-run

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

### Requirement: Editor focus suppresses global graph shortcuts

While the synthetic editor's input element holds keyboard focus, the global toolbar shortcut hook (`use-toolbar-shortcuts`) SHALL NOT dispatch `o` / `shift+o` / arrow / Tab / Shift+Tab events to the graph navigation handlers. Those keys SHALL flow into the input or the synthetic's local `onKeyDown` handler instead.

The synthetic's own `onKeyDown` handler SHALL own Tab / Shift+Tab (drive the state machine), Enter (commit via `useCreateIssue`), and Escape (cancel and clear `pendingNewIssue`).

There SHALL be at most one synthetic at any time. Pressing `o` or `shift+o` while editing SHALL type the literal character into the title input; it SHALL NOT spawn a second synthetic.

#### Scenario: typing `o` in the editor inserts the character
- **GIVEN** the synthetic editor is focused and the title is empty
- **WHEN** the user presses `o`
- **THEN** the title SHALL become `"o"`
- **AND** no second synthetic SHALL be created

#### Scenario: arrow keys move the cursor inside the editor
- **GIVEN** the synthetic editor is focused with a multi-character title
- **WHEN** the user presses Left or Right arrow
- **THEN** the input cursor SHALL move within the title text
- **AND** the graph selection SHALL NOT change

#### Scenario: Escape cancels and clears the synthetic
- **WHEN** the user presses Escape while editing
- **THEN** `pendingNewIssue` SHALL be set to `null`
- **AND** the synthetic SHALL be removed from the next layout result
- **AND** focus SHALL return to the graph container

#### Scenario: Enter commits via the existing create-issue mutation
- **WHEN** the user presses Enter while editing with a non-empty title
- **THEN** `useCreateIssue` SHALL be called with `parentIssueId` + `siblingIssueId` + `insertBefore` derived from the synthetic's current state-machine state
- **AND** on success the synthetic SHALL be cleared and the SignalR-echoed real issue SHALL appear in the layout in the same relative position

