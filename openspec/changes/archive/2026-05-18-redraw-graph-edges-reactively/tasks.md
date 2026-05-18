## 1. Wire expanded-panel height reporting

- [x] 1.1 Add `onHeightChange?: (issueId: string, height: number) => void` prop to `InlineIssueDetailRow` (re-exported as `TaskGraphExpandedDetails`) in `src/Homespun.Web/src/features/issues/components/inline-issue-detail-row.tsx`.
- [x] 1.2 Inside `InlineIssueDetailRow`, attach a ref to the panel's outer container; in a `useLayoutEffect` that runs once on mount, read `offsetHeight` and invoke `onHeightChange(issueId, height)` synchronously before the first paint.
- [x] 1.3 Inside `InlineIssueDetailRow`, instantiate a `ResizeObserver` in a `useEffect` that observes the panel's outer container; on every callback, invoke `onHeightChange(issueId, newHeight)`. Disconnect on unmount.
- [x] 1.4 On unmount, invoke `onHeightChange(issueId, 0)` (or equivalent removal signal) so the parent can drop the entry from its heights map.

## 2. Hold panel heights as React state in TaskGraphView

- [x] 2.1 In `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx`, add a `useState<Map<string, number>>(new Map())` for `expandedPanelHeights`.
- [x] 2.2 Define a stable `handlePanelHeightChange(issueId, height)` callback (`useCallback`) that updates the map immutably: if `height === 0`, remove the key; otherwise set it. Pass this as `onHeightChange` to every rendered `TaskGraphExpandedDetails`.
- [x] 2.3 Verify with a console assertion (or temporary log, removed before commit) that mounting an expanded panel populates the map synchronously on the first paint.

## 3. Make TaskGraphEdges pure-function over layout coordinates

- [x] 3.1 In `src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx`, change `TaskGraphEdgesProps`: remove `rowRefs`; add `expandedPanelHeights: Map<string, number>` and `expandedIds: Set<string>` (keep `expandedIds` — it's needed to know which render lines have a panel below them).
- [x] 3.2 Inside `TaskGraphEdges`, derive `renderLineIndexByIssueId: Map<string, number>` and `cumulativeOffsetByRow: number[]` via a `useMemo([renderLines, expandedPanelHeights, expandedIds])`. `cumulativeOffsetByRow[k]` = sum of `expandedPanelHeights[issueId]` for every render-line index `j < k` whose issue is in `expandedIds`.
- [x] 3.3 Inside the edges `map(...)` render, derive `from = { x: getLaneCenterX(edge.startLane), y: edge.startRow * ROW_HEIGHT + ROW_HEIGHT/2 + cumulativeOffsetByRow[edge.startRow], color: ... }` and the matching `to` from `edge.endLane`/`edge.endRow`. Use these directly in `buildEdgePath(edge, from, to)`.
- [x] 3.4 Derive `from.color` from the issue at `renderLines[edge.startRow]` (look it up via `renderLineIndexByIssueId` or by indexing `renderLines` directly) so the existing color contract is preserved.
- [x] 3.5 Replace `totalHeight` derivation: compute as `renderLines.length * ROW_HEIGHT + sum(expandedPanelHeights.values())`. No DOM measurement.
- [x] 3.6 Delete the `tick`/`setTick`/`useLayoutEffect([expandedIds])` block (`task-graph-svg.tsx:278-281`). Delete the `eslint-disable` comments on the now-redundant deps.

## 4. Remove rowRefs plumbing from edges-related code paths

- [x] 4.1 In `task-graph-view.tsx`, remove the `rowRefs` prop passed to `TaskGraphEdges` (`:885`).
- [x] 4.2 Determine whether `rowRefs` is still needed for scroll-into-view callers (`task-graph-view.tsx:295`, `:589`, `:599`, `:611`, `:625`, `:636`, `:647`). If yes, rename the ref to `scrollAnchorRefs` to make the purpose explicit; the ref-callback on `TaskGraphIssueRow` stays for that purpose only. If `aria-rowindex` + container `scrollTop` math can replace it, drop the ref entirely.
- [x] 4.3 If `scrollAnchorRefs` is retained, leave the ref-callback at `task-graph-view.tsx:990-996` but rename the local. If dropped, remove the ref-callback and any `ref` prop on `TaskGraphIssueRow` that exists solely for it.

## 5. Update unit tests for TaskGraphEdges

- [x] 5.1 In `src/Homespun.Web/src/features/issues/components/task-graph-svg.test.tsx`, update every test that constructs `TaskGraphEdges` to pass `expandedPanelHeights={new Map()}` and `expandedIds={new Set()}` instead of a `rowRefs` Map.
- [x] 5.2 Add a test covering Decision 1's invariant: with no panels expanded, edge endpoint Y coordinates equal `row * ROW_HEIGHT + ROW_HEIGHT / 2` for each edge's `startRow`/`endRow`.
- [x] 5.3 Add a test covering the expanded-panel offset: with `expandedPanelHeights = new Map([['issue-a', 120]])` and `expandedIds = new Set(['issue-a'])` where `issue-a` is at render-line index 2, every edge with `startRow > 2` or `endRow > 2` has the corresponding endpoint Y offset by 120 relative to the unexpanded baseline.
- [x] 5.4 Add a regression test asserting that `TaskGraphIssueRow` and the inline-edit row variant each render at exactly `ROW_HEIGHT` (uniform-height invariant).

## 6. Update storybook

- [x] 6.1 In `src/Homespun.Web/src/features/issues/components/task-graph-edges.stories.tsx`, remove hand-computed `cy = i * ROW_HEIGHT + getRowCenterY()` math; pass `expandedPanelHeights`/`expandedIds` props instead.
- [x] 6.2 Add a Storybook variant that demonstrates the expanded-panel offset by passing a non-empty `expandedPanelHeights` map (e.g., one expanded row with a 100px panel) and showing edges correctly offset.
- [x] 6.3 Run `npm run build-storybook` and verify no story drift errors.

## 7. Manual verification

- [x] 7.1 Run the AppHost in dev-mock mode (`dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`).
- [x] 7.2 Navigate to a project's issues page; toggle between "tree" and "next" view modes. Confirm edges follow row positions on the first paint after the toggle, no refresh.
- [x] 7.3 Expand a row, then toggle view modes. Confirm edges remain correctly aligned both before and after the toggle.
- [x] 7.4 Add a new issue via inline create. Confirm edges referencing the new row's position render correctly on the same paint.
- [x] 7.5 Delete an issue (or move it out of the visible set). Confirm downstream edges reposition immediately.
- [x] 7.6 Expand a row whose `TaskGraphExpandedDetails` panel mounts with non-trivial content; visually inspect the first paint for any one-frame mis-alignment.

## 8. Pre-PR checklist

- [x] 8.1 `cd src/Homespun.Web && npm run lint:fix`
- [x] 8.2 `cd src/Homespun.Web && npm run format:check`
- [x] 8.3 `cd src/Homespun.Web && npm run typecheck`
- [x] 8.4 `cd src/Homespun.Web && npm test`
- [x] 8.5 `cd src/Homespun.Web && npm run test:e2e`
- [x] 8.6 `cd src/Homespun.Web && npm run build-storybook`
- [x] 8.7 `dotnet test` (no backend changes expected; this guards against accidental shared-code breakage).

## 9. Fleece + OpenSpec integration

- [x] 9.1 Tag Fleece issue `pwJaLv` with the `openspec=redraw-graph-edges-reactively` tag: `fleece edit pwJaLv --tags "openspec=redraw-graph-edges-reactively"`.
- [x] 9.2 Move issue `pwJaLv` to `progress` when implementation starts: `fleece edit pwJaLv -s progress`.
- [x] 9.3 Before opening the PR, set `pwJaLv` to `review` and link the PR number: `fleece edit pwJaLv -s review --linked-pr <pr-number>`. Commit `.fleece/` changes with the code or use `fleece commit --ci`.
