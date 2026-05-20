## Purpose

Integrate OpenSpec change lifecycle (propose → apply → archive) with Homespun's Fleece-issue-driven agent workflow. Links changes to issues via `.homespun.yaml` sidecars, scans branches for change state, surfaces progress in the issue graph, and adds an OpenSpec tab to the run-agent panel with auto-selected skill dispatch.
## Requirements
### Requirement: Branch scanner service

The system SHALL scan branches for OpenSpec change state and make it available to the issue graph. The scanner SHALL source linkage from per-clone Fleece `openspec=` tags (see "Change-to-issue linkage via Fleece tag"), not from on-disk sidecar files.

#### Scenario: On-demand scan with cache fallback

- **WHEN** the UI requests graph data and no cached snapshot exists (or cache is stale beyond 60s TTL)
- **THEN** the server SHALL perform a live disk scan of the on-disk clone
- **AND** SHALL cache the result keyed by `(projectId, branch)`
- **AND** the cache SHALL be internal to `BranchStateResolverService` (no public ingest endpoint)

#### Scenario: Tag-mapped linkage

- **WHEN** the scanner runs on a clone
- **THEN** it SHALL build a `change-name → issue-id` map from `openspec=` tags on every Fleece issue in that clone
- **AND** each `openspec/changes/<name>/` directory SHALL be classified as linked to `map[<name>]` when present, or silently skipped otherwise

#### Scenario: Archived change fallback

- **WHEN** a tagged change is no longer in `openspec/changes/` but exists in `openspec/changes/archive/`
- **THEN** the scanner SHALL match the archived folder name (with the `YYYY-MM-DD-` date prefix stripped) against the tag map
- **AND** a match SHALL classify the archived change as linked to the tagged issue
- **AND** the reconciliation layer SHALL auto-transition the fleece issue to `complete` status if it is not already in a terminal state

### Requirement: Issue graph change indicators

The system SHALL display branch and change status indicators on each issue row in the graph. The enrichment data SHALL be served from independent endpoints rather than bundled into the issue response: per-issue branch/change state via `GET /api/projects/{projectId}/openspec-states`, linked-PR state via `GET /api/projects/{projectId}/linked-prs`, and the per-issue branch fields embedded in the `IssueResponse` DTO from `GET /api/projects/{projectId}/issues`. Each enricher's branch resolution SHALL reuse a single per-request `BranchResolutionContext` (clones list + PR-to-branch dictionary) and SHALL NOT invoke `IGitCloneService.ListClonesAsync` more than once per request.

The web client assembles the indicator data by running parallel queries against each endpoint and joining at render time. Visual placement happens after the TS layout port runs — the *data* informing each indicator comes from the relevant endpoint above.

#### Scenario: Branch indicator colours
- **WHEN** an issue has no branch → gray branch symbol
- **WHEN** an issue has a branch but no change → white branch symbol
- **WHEN** an issue has a branch with a change → amber branch symbol

#### Scenario: Change status symbols
- **WHEN** no change exists → no symbol
- **WHEN** change exists, artifacts incomplete → red ◐
- **WHEN** all schema artifacts created → amber ◐
- **WHEN** all tasks checked → green ●
- **WHEN** change archived → blue ✓

#### Scenario: Issue node shape
- **WHEN** an issue has no linked change → round node (○)
- **WHEN** an issue has a linked change → square node (□)

#### Scenario: Per-request branch resolution avoids subprocess fan-out
- **WHEN** `IIssueGraphOpenSpecEnricher.GetOpenSpecStatesAsync` or `GetMainOrphanChangesAsync` is invoked
- **THEN** `IGitCloneService.ListClonesAsync(project.LocalPath)` SHALL be called at most once for that request regardless of N
- **AND** `IDataStore.GetPullRequestsByProject` SHALL be called at most once for that request

### Requirement: Virtual sub-issue rendering from tasks.md

The system SHALL parse `tasks.md` from linked changes and render phase-level roll-ups in the issue graph.

#### Scenario: Phase-level roll-up
- **WHEN** tasks.md contains `## N. Phase Name` headings with checkbox tasks
- **THEN** the graph SHALL show one virtual sub-node per phase heading
- **AND** each sub-node SHALL display `done/total` task counts

#### Scenario: Phase detail modal
- **WHEN** the user clicks a phase badge
- **THEN** a modal SHALL display all leaf tasks under that phase with their checkbox state

#### Scenario: Phases are not individually dispatchable
- **WHEN** a virtual phase sub-node is rendered
- **THEN** it SHALL be display-only with no dispatch action

### Requirement: OpenSpec tab in run-agent panel

The system SHALL provide an "OpenSpec" tab in the run-agent panel that replaces the former "Workflow" tab.

#### Scenario: All 8 OpenSpec skills are listed
- **WHEN** the user opens the OpenSpec tab for a change-linked issue
- **THEN** the tab SHALL list all 8 OpenSpec skills
- **AND** each SHALL be selectable for dispatch

#### Scenario: Auto-selection defaults
- **WHEN** no change exists or artifacts are incomplete → default to `openspec-explore`
- **WHEN** all schema artifacts are created → default to `openspec-apply-change`
- **WHEN** all tasks in tasks.md are checked → default to `openspec-archive-change`

#### Scenario: Readiness gating for apply, verify, sync, archive
- **WHEN** the user selects `apply`, `verify`, `sync`, or `archive`
- **AND** their prerequisites are not met
- **THEN** the skill SHALL be visually marked as blocked
- **AND** SHALL NOT be dispatchable
- **AND** `explore`, `propose`, `new-change`, and `continue-change` SHALL always be available

#### Scenario: Schema override injection
- **WHEN** the project uses a non-default schema (per `openspec/config.yaml`)
- **THEN** the dispatch SHALL inject `"use openspec schema '{schema}' for all openspec commands"` into the session's system prompt

### Requirement: Multi-change per branch

The system SHALL support multiple changes on a single branch, each linked to its own fleece issue via its own sidecar.

#### Scenario: Sibling changes under same issue
- **WHEN** multiple changes on a branch have sidecars pointing to the same fleece-id
- **THEN** the graph SHALL render each as a sibling node under that issue

#### Scenario: Changes linked to different issues on same branch
- **WHEN** changes on a branch have sidecars pointing to different fleece-ids
- **THEN** each change SHALL appear under its own respective issue in the graph

### Requirement: Artifact-state micro-cache

`ChangeScannerService.GetArtifactStateAsync` SHALL cache parsed `ChangeArtifactState` values keyed on the tuple `(clonePath, changeName, mtimeTuple)` where `mtimeTuple` is derived from the last-write times of `proposal.md`, `tasks.md`, and the `specs/` subtree. The scanner SHALL only invoke the `openspec status` subprocess when no cache entry matches the current mtime tuple.

#### Scenario: Repeated scan with unchanged files skips subprocess
- **WHEN** `GetArtifactStateAsync` is called twice for the same change directory with no file modifications between calls
- **THEN** the second call SHALL return the cached value
- **AND** `ICommandRunner.RunAsync` SHALL NOT be invoked for the second call

#### Scenario: File modification busts cache entry
- **WHEN** `tasks.md` under a cached change directory is modified
- **THEN** the next `GetArtifactStateAsync` call SHALL re-invoke `openspec status` and produce a fresh `ChangeArtifactState`

### Requirement: Task-graph spans cover the enrichment path

`IssueGraphOpenSpecEnricher.EnrichAsync`, `BranchStateResolverService.GetOrScanAsync`, `ChangeReconciliationService.ReconcileAsync`, `ChangeScannerService.ScanBranchAsync`, `ChangeScannerService.GetArtifactStateAsync`, `IssueBranchResolverService.ResolveIssueBranchAsync`, and `CommandRunner.RunAsync` SHALL each emit an `Activity` under a dedicated `ActivitySource` (`Homespun.OpenSpec` for OpenSpec enrichment work, `Homespun.Commands` for the command runner). Each span SHALL carry cardinality-safe tags only: `project.id`, `issue.id`, `change.name`, `cache.hit`, `branch.source`, `phase`, `cmd.name`, `cmd.exit_code`, `cmd.duration_ms`. Every new span name SHALL appear in `docs/traces/dictionary.md`.

The new `IssuesController.GetVisibleIssues` action and the new `IssueAncestorTraversalService.CollectVisible` SHALL each emit a span on `Homespun.Fleece` (or a dedicated `Homespun.Issues` source) tagged with `project.id`, `issue.count`, and `cache.hit=false` (no snapshot exists). New span names SHALL be added to `docs/traces/dictionary.md` in the same change.

#### Scenario: Visible-issue-set request span has child spans for enrichment work
- **WHEN** `GET /api/projects/{projectId}/issues` is served
- **THEN** the emitted trace SHALL include a top-level span for the controller action and child spans for `openspec.enrich`, ancestor traversal (e.g. `issues.collect_visible`), and at least one `openspec.scan.branch` if any visible issue has a clone

#### Scenario: Command runner span wraps every subprocess
- **WHEN** `CommandRunner.RunAsync` spawns an `openspec` or `git` subprocess
- **THEN** the subprocess invocation SHALL be surrounded by a `cmd.run` span tagged with `cmd.name` and `cmd.exit_code`

#### Scenario: Trace dictionary drift check enforces new span names
- **WHEN** a pull request adds a new span name but does not update `docs/traces/dictionary.md`
- **THEN** the existing drift-check test in the server suite SHALL fail

### Requirement: OpenSpec states endpoint

The system SHALL expose `GET /api/projects/{projectId}/openspec-states?issues=<id>,<id>` returning `IReadOnlyDictionary<string, IssueOpenSpecState>` keyed by Fleece issue id.

The optional `issues=` query param SHALL constrain the per-clone scan to the supplied subset (the frontend supplies the visible-set ids it just fetched). When omitted, the server SHALL scan all visible issues — defined as issues with `Status ∈ { Draft, Open, Progress, Review }` plus their ancestors, mirroring the issue-set endpoint's default filter.

`IssueOpenSpecState` SHALL contain the per-issue change phase, the change name, the schema name, and the phase summary list. The DTO SHALL NOT carry orphan information (the orphan-changes concept has been removed).

The endpoint SHALL be independently testable: a test SHALL not require seeding agent sessions, linked PRs, or graph layout — only on-disk OpenSpec change directories within project clones plus per-clone Fleece state.

#### Scenario: Issue with tagged change returns its state

- **WHEN** an issue has a working branch with a clone containing an OpenSpec change linked via an `openspec=<name>` tag on the issue
- **AND** the client calls `GET /api/projects/{projectId}/openspec-states?issues=<that-issue-id>`
- **THEN** the response SHALL contain a single entry keyed by that issue id with the change's state populated

#### Scenario: Issue without a clone returns no entry

- **WHEN** an issue has no working clone on disk
- **THEN** the response SHALL NOT contain an entry for that issue (the dictionary's absence-of-key signals "no OpenSpec data")

#### Scenario: issues= query param scopes the scan

- **WHEN** the client requests `?issues=a,b,c` on a project with 100 issues
- **THEN** the per-clone scan SHALL execute only for clones containing those three issues
- **AND** the response SHALL contain at most three entries

#### Scenario: Empty issues= param returns empty map

- **WHEN** the client requests `?issues=` (empty value)
- **THEN** the response SHALL be `{}` and 200

### Requirement: Change-to-issue linkage via Fleece tag

The system SHALL link OpenSpec changes to Fleece issues via an `openspec=<change-name>` tag on the Fleece issue. The tag SHALL be authored by the OpenSpec skills (`openspec-new-change`, `openspec-propose`, `openspec-explore`, etc.) via `fleece edit <id> --tags openspec=<name>`, following the convention injected into the agent system prompt by the Fleece CLI.

The scanner SHALL consult the per-clone Fleece projection (via `IProjectFleeceService.ListIssuesAsync(clonePath)`) to build a `change-name → issue-id` map from all `openspec=` tags, then match against on-disk `openspec/changes/*` directories. A change directory with a matching tag entry is linked to that issue; a change directory with no matching tag entry is silently skipped (no UI footprint, no "orphan" classification).

The tag write is the agent's responsibility, not the server's. The server SHALL NOT auto-write tags. There is no auto-link reconciliation path.

#### Scenario: Tagged change links to its issue

- **WHEN** a Fleece issue carries the tag `openspec=foo`
- **AND** the issue's branch clone contains `openspec/changes/foo/`
- **THEN** the scanner SHALL classify the `foo` change as linked to that issue

#### Scenario: Untagged change is silently skipped

- **WHEN** a clone contains `openspec/changes/bar/` and no Fleece issue in that clone carries the tag `openspec=bar`
- **THEN** the scanner SHALL NOT include the `bar` change in any issue's linked-change list
- **AND** the `bar` change SHALL NOT surface anywhere in the UI

#### Scenario: Same change name tagged on multiple issues

- **WHEN** two issues in the same clone both carry the tag `openspec=baz`
- **THEN** the scanner's tag map SHALL contain the last-write-wins entry for `baz`
- **AND** the discouragement of multiple `openspec=` tags per change is enforced by convention (the Fleece CLI prompt notes that multiple tags are permitted but discouraged), not by validation

