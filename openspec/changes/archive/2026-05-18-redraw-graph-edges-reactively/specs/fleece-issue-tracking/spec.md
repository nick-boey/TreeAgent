## ADDED Requirements

### Requirement: Edge endpoint geometry derived purely from layout coordinates

The web client's edge renderer (`task-graph-svg.tsx::TaskGraphEdges`) SHALL derive edge endpoint coordinates as a pure function of the layout result plus a small set of measured expanded-panel heights. It SHALL NOT read row positions from the DOM at render time (no `el.offsetTop` reads, no `getBoundingClientRect` calls against row elements during render).

The X coordinate for an edge endpoint SHALL be `getLaneCenterX(lane)` where `lane` is `edge.startLane` for the source endpoint and `edge.endLane` for the target endpoint. These fields are emitted by `IssueLayoutService` on every `TaskGraphEdge`.

The Y coordinate for an edge endpoint SHALL be `row * ROW_HEIGHT + ROW_HEIGHT / 2 + cumulativeExpandedOffset(row)` where `row` is `edge.startRow` for the source endpoint and `edge.endRow` for the target endpoint. `cumulativeExpandedOffset(row)` SHALL be the sum of every currently-mounted expanded panel's measured height for render-line indices `j < row` whose corresponding issue is in the expanded set.

`TaskGraphEdge.pivotLane` SHALL continue to drive the pivot column for `ParallelChildToSpine` paths as today; this requirement only constrains endpoint derivation.

Implementation invariants this requirement relies on:

- Every issue row variant (`TaskGraphIssueRow`, the inline-edit row variant, the pending-issue row variant) SHALL render at exactly `ROW_HEIGHT`. The only render-line variant permitted to have variable height is the optional `TaskGraphExpandedDetails` panel mounted conditionally below an expanded row.
- The mechanism that previously bumped a re-measurement tick on expand/collapse (the `useLayoutEffect([expandedIds])` block) SHALL be removed, along with the `rowRefs` Map plumbing on `TaskGraphIssueRow` that fed its DOM reads. Edge geometry SHALL react to layout, expansion, and panel-height changes purely through normal React state propagation.

#### Scenario: View mode flip from tree to next does not require a refresh
- **WHEN** the user is viewing the issue graph in "tree" mode and toggles to "next" mode (or vice versa)
- **THEN** the SVG edges SHALL render against the new row positions on the first paint after the mode change
- **AND** no edge SHALL retain the previous mode's Y coordinates until a page refresh

#### Scenario: Adding a row repositions edges immediately
- **WHEN** a new issue row is added to the visible set (via mutation or SignalR `IssueChanged` echo)
- **THEN** every edge whose `startRow` or `endRow` indexes the new or repositioned rows SHALL render at the layout-correct Y coordinate on the same paint that mounts the new row
- **AND** no manual re-measurement pass SHALL be required

#### Scenario: Removing a row repositions edges immediately
- **WHEN** an issue row is removed from the visible set
- **THEN** every edge whose endpoints reference rows whose `startRow`/`endRow` indices shifted SHALL render at the new layout-correct Y coordinate on the same paint that unmounts the removed row

#### Scenario: Expanding a row pushes downstream edges by the panel's measured height
- **WHEN** the user expands a row at render-line index `k` whose `TaskGraphExpandedDetails` panel measures `h` pixels tall
- **THEN** every edge whose `startRow > k` or `endRow > k` SHALL have its corresponding endpoint Y coordinate offset by `h` relative to the unexpanded layout

#### Scenario: Newly expanded panel does not flicker edges by one frame
- **WHEN** a user expands a row and the `TaskGraphExpandedDetails` panel mounts
- **THEN** the panel SHALL report its `offsetHeight` synchronously via `useLayoutEffect` before the first paint
- **AND** edges crossing past the expanded row SHALL render with the correct cumulative offset on that first paint
- **AND** edges SHALL NOT briefly draw at the pre-expansion positions before the asynchronous `ResizeObserver` callback fires

#### Scenario: Late content shift inside an expanded panel reflows downstream edges
- **WHEN** content inside an already-mounted `TaskGraphExpandedDetails` panel changes height after initial mount (for example, async-loaded content or font swap)
- **THEN** the panel's `ResizeObserver` SHALL fire and report the new height via `onHeightChange`
- **AND** downstream edges SHALL re-render at the updated cumulative offset on the next paint

#### Scenario: Edge endpoint lookup does not go through an issue-id map
- **WHEN** `buildEdgePath` is invoked for any edge
- **THEN** the `from` and `to` coordinates passed in SHALL be derived directly from `edge.startRow`/`startLane` and `edge.endRow`/`endLane` plus the expanded-panel heights map
- **AND** no intermediate `Map<issueId, {x, y}>` populated by walking `renderLines` SHALL be required

#### Scenario: TaskGraphEdges renders correctly with no rows expanded
- **WHEN** the issue graph is rendered with `expandedIds` empty (no expanded panels mounted)
- **THEN** every edge endpoint Y coordinate SHALL equal `row * ROW_HEIGHT + ROW_HEIGHT / 2` for its corresponding `startRow`/`endRow`
- **AND** the rendered SVG paths SHALL match the existing arc-cornered orthogonal edge contract (`Arc-cornered orthogonal edge rendering` requirement) byte-for-byte for the same inputs
