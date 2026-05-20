## Why

Issue-graph SVG edges fall out of sync with row positions whenever the row list changes for any reason other than expand/collapse — switching between "tree" and "next" view modes, or adding/removing rows, leaves edges pointing at the previous layout's row Y coordinates. Users currently have to refresh the page to recover. The root cause is that `TaskGraphEdges` derives edge geometry from `el.offsetTop` reads against ref-tracked row elements during the render phase (before React commits the new DOM), and the only mechanism that forces a re-measurement after commit listens to `expandedIds` alone. The fix needs to be structural: as long as edge geometry depends on race-prone DOM reads, every new layout-affecting state requires remembering to extend the re-measurement dep array, and we will keep shipping this class of bug.

## What Changes

- Edge geometry SHALL be derived as a pure function of `(renderLines, edges, expandedPanelHeights)` instead of from DOM `offsetTop` reads. Y positions SHALL come from `row * ROW_HEIGHT + ROW_HEIGHT/2 + cumulativeExpandedPanelOffset(row)`; X positions SHALL come from `getLaneCenterX(lane)` against the layout-supplied lane index.
- `TaskGraphEdge.startRow`, `startLane`, `endRow`, `endLane`, `pivotLane` — emitted by the layout engine today but currently consumed only by `pivotLane` — SHALL become the load-bearing source of truth for edge endpoint coordinates. Edge endpoint lookup SHALL no longer indirect through an issue-id → DOM-element map.
- `TaskGraphExpandedDetails` (the only variable-height element below an expanded row) SHALL attach a `ResizeObserver` and report its rendered height upward via an `onHeightChange(issueId, height)` prop. The parent (`TaskGraphView`) SHALL store these heights as React state so updates trigger natural re-renders.
- The initial-mount height of every expanded panel SHALL be seeded synchronously via `useLayoutEffect` reading `offsetHeight`, so the first paint after expansion uses the correct cumulative offset instead of waiting for the asynchronous `ResizeObserver` callback.
- **BREAKING (internal contract)** the `tick` state, the `useLayoutEffect([expandedIds])` re-measurement pass, and the `rowRefs: Map<string, HTMLDivElement>` plumbing on `TaskGraphIssueRow` SHALL be removed. These exist solely to compensate for race-prone DOM measurement and are made redundant by the pure-function pipeline.
- Tests and stories that hand-mock `rowRefs` or hand-compute row Y positions SHALL be updated to feed `expandedPanelHeights` (or rely on the default zero-heights case for non-expanded layouts).

## Capabilities

### New Capabilities
<!-- None. This change refines an existing capability. -->

### Modified Capabilities
- `fleece-issue-tracking`: adds a requirement constraining how the web client's edge renderer derives edge endpoint geometry (pure function of layout coordinates + measured expanded-panel heights, not DOM `offsetTop` reads). Existing edge-rendering requirements (arc-cornered orthogonal paths, edge kinds) are unchanged; this change adds an upstream invariant about where the input coordinates come from.

## Impact

- **Affected code** (frontend only, all under `src/Homespun.Web`):
  - `src/features/issues/components/task-graph-svg.tsx` — `TaskGraphEdges` component: nodeMap memo becomes pure; `tick` / `useLayoutEffect([expandedIds])` removed; props change from `rowRefs` to `expandedPanelHeights`.
  - `src/features/issues/components/task-graph-view.tsx` — `rowRefs` Map removed; `expandedPanelHeights` state added; ref callback on `TaskGraphIssueRow` removed; new `onHeightChange` prop wired into `TaskGraphExpandedDetails`.
  - `src/features/issues/components/inline-issue-detail-row.tsx` (= `TaskGraphExpandedDetails`) — attaches `ResizeObserver`; calls `onHeightChange` synchronously on mount (via `useLayoutEffect`) and on every resize; unregisters on unmount.
  - `src/features/issues/components/task-graph-svg.test.tsx` — tests that currently mock `rowRefs` are updated to feed `expandedPanelHeights`.
  - `src/features/issues/components/task-graph-edges.stories.tsx` — story-side position math is simplified; storybook becomes the pure-function consumer's reference.
- **No backend changes.** The TS layout port under `services/layout/` and its golden-fixtures contract (`tests/Homespun.Web.LayoutFixtures/`) are unaffected — the layout engine already emits the row/lane coordinates this change starts consuming.
- **No API or wire-format changes.** `TaskGraphEdge`'s shape (already including `startRow`/`endRow`/`startLane`/`endLane`/`pivotLane`) is unchanged; this change just consumes fields that were previously plumbed through and ignored.
- **No new dependencies.** `ResizeObserver` is a standard browser API and is already mocked in the test setup (`src/test/setup.ts`).
- **Bug classes eliminated by construction**: view-mode flip stales edges; row add/remove stales edges; late content shifts inside expanded panels; "I added a layout-affecting state and forgot to bump tick" regressions.
- **Risk**: a newly-expanded panel that fails to seed its height synchronously would draw with `panelHeight = 0` for one frame. Mandatory `useLayoutEffect` seeding mitigates this.
