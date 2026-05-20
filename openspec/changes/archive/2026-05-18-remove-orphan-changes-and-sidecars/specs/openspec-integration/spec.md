## ADDED Requirements

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

## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Change-to-issue linkage via sidecar

**Reason**: Sidecar-based linkage is superseded by Fleece `openspec=<change-name>` tags, which are now the canonical link source (the convention is documented in the agent system prompt injected by the Fleece CLI). Maintaining both mechanisms is dead weight.

**Migration**: New work links changes to issues by running `fleece edit <id> --tags openspec=<name>` in the branch clone. The OpenSpec skills do this automatically. Pre-existing branches carrying `.homespun.yaml` sidecars but no tag will display as unlinked; users restore the indicator by issuing the same `fleece edit` command. The sidecar files themselves are left in place as inert artifacts; the scanner no longer reads them.

### Requirement: Orphan change handling

**Reason**: The user determined that the bottom-of-graph "Orphaned Changes" section was not useful in practice. Without an orphan classification, no UI need surface.

**Migration**: Changes without a linking `openspec=` tag are silently skipped by the scanner and do not appear anywhere in the UI. To bring an unlinked change into the graph, run `fleece edit <id> --tags openspec=<change-name>` in the branch clone.

### Requirement: Link-picker dialog with filter and containment-based highlights

**Reason**: The picker existed solely to commit orphan-to-issue links via the now-removed `POST /api/openspec/changes/link` endpoint. With orphan classification gone, the picker has no use.

**Migration**: Use `fleece edit <id> --tags openspec=<name>` to link a change to an issue. The web UI provides no equivalent picker; tag authorship lives in the CLI and the agent skills.

### Requirement: Branchless link mode discovers and writes every clone in one request

**Reason**: The `POST /api/openspec/changes/link` endpoint is removed in its entirety along with sidecar writes.

**Migration**: Tag-based linkage requires only a single `fleece edit` call on the active clone; no multi-clone fan-out is needed because the Fleece event log propagates the tag wherever the issue is read.

### Requirement: Orphan link is a single branchless server call

**Reason**: Hook `useLinkOrphan` and its single-call contract are removed along with the link endpoint and the orphan UI.

**Migration**: No client-side link mutation remains. Tag writes happen via the Fleece CLI (typically inside an agent session).

### Requirement: Orphan changes endpoint

**Reason**: `GET /api/projects/{projectId}/orphan-changes` is removed along with the orphan classification it served.

**Migration**: No replacement endpoint. The `openspec-states` endpoint continues to serve per-issue linked-change state; there is no project-wide "unlinked changes" listing.
