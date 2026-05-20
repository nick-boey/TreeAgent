## Context

The web client renders the issue task-graph as a list of fixed-height rows with an absolutely-positioned SVG overlay drawing edges between them. Edge geometry is computed by `TaskGraphEdges` (`src/Homespun.Web/src/features/issues/components/task-graph-svg.tsx:268-347`):

1. A `nodeMap: Map<issueId, {x, y, color}>` is built inside a `useMemo` by walking `renderLines` and reading `el.offsetTop` from a `rowRefs: useRef<Map<string, HTMLDivElement>>` populated by ref-callbacks on each `TaskGraphIssueRow`.
2. `buildEdgePath(edge, from, to)` consumes `from`/`to` (looked up from `nodeMap` by issue id) to emit an SVG path.

This design has two interlocking flaws:

- The `nodeMap` memo runs during the render phase, before React has committed the new DOM. So when `renderLines` changes (mode flip from "tree" to "next", row add/remove), `rowRefs.current` still holds DOM elements whose `offsetTop` reflects the *previous* layout. The memo computes wrong Y coordinates.
- A `useLayoutEffect` exists specifically to force a post-commit re-measure pass via a `tick` counter, but its dep array is `[expandedIds]` only. It fires on expand/collapse (which doesn't change `renderLines`) but not on the cases that *do* change `renderLines`. `rowRefs` is also listed as a dep, but it's a stable ref object — React compares by identity and never sees a change.

The reported user-visible bug is that switching view modes or adding rows leaves edges at the old positions until a page refresh forces a clean first render.

The layout engine already emits everything needed to compute Y positions without measuring the DOM. `TaskGraphEdge` (`services/task-graph-layout.ts:47-58`) carries `startRow`, `startLane`, `endRow`, `endLane`, and `pivotLane`. These are populated in both the legacy server-laid-out path (lines 204-215) and the new client-side path (lines 611-622). Today only `pivotLane` is consumed at render time; the row/lane indices are plumbed through and ignored.

The only thing the layout engine cannot know is the rendered height of an expanded `TaskGraphExpandedDetails` panel (re-exported from `InlineIssueDetailRow`, conditionally mounted at `task-graph-view.tsx:1022`). The panel is a flexbox with dynamic content; its height depends on the issue's data. This is the genuine reason a DOM measurement exists somewhere in the pipeline. It is also the *only* such reason — every other row variant (regular `TaskGraphIssueRow`, the inline-edit row variant) pins `style={{ height: ROW_HEIGHT }}`.

## Goals / Non-Goals

**Goals:**
- Eliminate stale edges on view-mode flips and row add/remove without manual dep-array maintenance.
- Make edge geometry a pure function of layout output plus a small, well-bounded set of measured panel heights.
- Remove the `tick` / `rowRefs` / `useLayoutEffect([expandedIds])` machinery that exists solely to compensate for race-prone DOM reads.
- Preserve every existing visual behaviour: edge paths, arc-cornered orthogonal shape, lane fidelity, expand/collapse animation, inline editing.

**Non-Goals:**
- No changes to the layout engine itself (TS port under `services/layout/`) or its golden-fixtures contract.
- No changes to the `TaskGraphEdge` wire format. The shift is which fields are *consumed*, not which fields exist.
- No UX changes to row expansion. Expanded details continue to render in-flow and push subsequent rows down — same as today.
- No virtualization work. The list is unvirtualized today; this change does not introduce or preclude virtualization.
- No backend or API changes.

## Decisions

### Decision 1: Pure-function edge geometry from layout coordinates

`TaskGraphEdges` computes endpoint coordinates as:

```
Y(row) = row * ROW_HEIGHT + ROW_HEIGHT / 2 + cumulativeExpandedOffset(row)
X(lane) = getLaneCenterX(lane)
```

where `cumulativeExpandedOffset(row) = Σ panelHeight[issueId] for every renderLine index j < row whose issue is currently expanded`. `from` and `to` are derived from `edge.startRow`/`edge.startLane` and `edge.endRow`/`edge.endLane` respectively — no issue-id → DOM-element indirection.

**Rationale:** the layout engine is already authoritative about row order and lane assignment. Treating it as authoritative at the render boundary removes the entire class of "did we remember to bump tick?" bugs. The cumulative-offset prefix sum is O(N) per render to build; lookup per edge is O(1).

**Alternatives considered:**
- *Minimal fix: add `renderLines` to the `useLayoutEffect` dep list (Option A in exploration).* Two-line patch. Ships the bug fix today but keeps the race-prone measurement architecture and pays an indefinite "remember to extend the dep array" tax forever. Rejected as the chosen approach but acceptable as an interim if this change cannot ship in one PR.
- *ResizeObserver on the container, retain DOM measurement (Option B).* Catches one more bug class (late content shifts inside expanded panels) but does not remove the coupling to `offsetTop`. Sits in an awkward middle.
- *Pure index math, ignore expanded-panel heights (Option C).* Would break the instant any row is expanded. Rejected — the user confirmed expansion is reasonably common.
- *Move expanded details out of flow (Option D).* Cleanest architecturally but a UX change (popover / side panel) that needs product agreement. Rejected as scope creep.

### Decision 2: ResizeObserver per expanded panel, height stored as React state

Each mounted `TaskGraphExpandedDetails` attaches its own `ResizeObserver` and invokes a new prop `onHeightChange(issueId: string, height: number)`. `TaskGraphView` holds `expandedPanelHeights: Map<string, number>` as React state (via `useState`, not `useRef`) so updates trigger natural re-renders. On unmount the panel invokes `onHeightChange(issueId, 0)` (or a sibling `onUnmount(issueId)`) so the entry is removed from the map.

**Rationale:** ownership matches reality — each panel is the only component that knows its own height. A single container-wide observer (the variant E2 considered in exploration) is centralised but forces the parent to reach into children via `querySelector`, which is a worse coupling than passing a callback down. Storing as state, not a ref, means the geometry memo's deps work the way React intends — no manual `tick`.

**Alternatives considered:**
- *Single ResizeObserver on the relative container.* Centralised but requires reading children's `offsetHeight` from outside their owning component. Rejected — worse ownership boundary.
- *Map as `useRef` plus a separate `tick` state.* Re-introduces the exact pattern we are removing. Rejected.

### Decision 3: Synchronous height seeding via useLayoutEffect inside the panel

On mount, the expanded panel SHALL read its own `offsetHeight` inside a `useLayoutEffect` and call `onHeightChange` synchronously, before the browser paints. This avoids a one-frame flicker where edges crossing past a newly-expanded row are drawn assuming `panelHeight = 0` until the asynchronous `ResizeObserver` callback fires.

**Rationale:** `ResizeObserver` callbacks are dispatched in a microtask after layout — not in the same task as the paint that exposed the new layout. Without synchronous seeding, the first paint of a newly-expanded row would use the wrong cumulative offset. `useLayoutEffect` runs synchronously between commit and paint, which is the exact slot needed.

**Alternatives considered:**
- *Rely on ResizeObserver alone.* Simpler but accepts the one-frame flicker. Rejected — visible regression versus current behaviour.
- *Set the height in a ref-callback at attach time.* Works but couples height reporting to ref attachment rather than to the panel's lifecycle. The `useLayoutEffect` route keeps the height-reporting logic local to the panel component.

### Decision 4: Remove rowRefs and the tick machinery entirely

The `rowRefs: useRef<Map<string, HTMLDivElement>>(new Map())` declaration in `task-graph-view.tsx:148`, every `rowRefs.current.set` / `rowRefs.current.get` call site, the ref-callback on `TaskGraphIssueRow` (`task-graph-view.tsx:990-996`), the `tick`/`setTick`/`useLayoutEffect([expandedIds])` block in `TaskGraphEdges` (`task-graph-svg.tsx:278-281`), and the `rowRefs` prop on `TaskGraphEdges` SHALL be removed. These exist solely to support DOM-position measurement and are made redundant by Decisions 1-3.

**Exception:** `rowRefs` is also used by keyboard-navigation `scrollIntoView` calls (`task-graph-view.tsx:295`, `:589`, `:599`, `:611`, `:625`, `:636`, `:647`). These callers need a reference to the row's DOM element for scrolling, which is a different purpose from edge measurement. They SHALL continue to work — either by retaining the ref-callback for this purpose under a renamed `scrollAnchorRefs` (clearer intent) or by switching scroll-into-view to use the `aria-rowindex` + container `scrollTop` math directly. Implementation chooses whichever is simpler.

**Rationale:** retaining `rowRefs` only for edge measurement would defeat the whole point of the change. Retaining it for an unrelated reason (scroll targets) is fine — the bug class being eliminated is about *geometry consumers misreading mid-commit DOM state*, not about holding DOM refs in general.

### Decision 5: Test and storybook updates ship with the change

`task-graph-svg.test.tsx` currently constructs `rowRefs` Maps to drive `TaskGraphEdges` under test (~17 occurrences of `TaskGraphEdge` in test setup). These tests SHALL be updated to feed `expandedPanelHeights` (or rely on the implicit empty map when no rows are expanded). Edge-position assertions SHALL switch from measuring rendered geometry against mocked `offsetTop` values to comparing rendered geometry against the pure-function output for the same inputs.

`task-graph-edges.stories.tsx` currently hand-computes `cy = i * ROW_HEIGHT + getRowCenterY()` (line 53). The story SHALL stop computing positions and let the component derive them from layout coordinates. The story becomes a reference example of the new pure-function consumer pattern.

**Rationale:** mid-flight test scaffolding is the largest concrete chunk of churn in this change. Shipping it in the same PR keeps the contract intact and prevents "tests pass with old mocks against new behaviour" drift.

## Risks / Trade-offs

- **[Risk] One-frame flicker on newly-expanded rows if height seeding is skipped.** → Mitigated by Decision 3: seeding via `useLayoutEffect` is mandatory and covered by a scenario in the spec delta. Reviewer attention should flag any code path that mounts an expanded panel without a synchronous seed.
- **[Risk] Test churn is larger than the production change.** → Accepted. The proposal explicitly scopes the test/storybook updates in. Skipping them would let stale mocks mask a real regression.
- **[Risk] Variable-height content that future maintainers add to a regular row (not the expanded panel) silently breaks the pure-function assumption.** → Mitigated by Decision 1's contract: rows other than `TaskGraphExpandedDetails` MUST be exactly `ROW_HEIGHT`. A unit test SHALL assert `TaskGraphIssueRow` and the inline-edit row variant render at `ROW_HEIGHT` so this invariant is regression-tested.
- **[Risk] Late-loading content inside an expanded panel (e.g., async-loaded markdown, image inflation) was implicitly broken today and remains broken after Decision 3 alone.** → Mitigated by `ResizeObserver`: subsequent height changes after the seed fire the observer and trigger a re-render. Decision 2's per-panel observer covers this naturally.
- **[Trade-off] One extra render per panel resize.** → Acceptable. Expansions are user-driven, infrequent, and bounded by the number of currently-expanded rows. The render is cheap (pure memo recompute, no DOM thrash).
- **[Trade-off] Scroll-into-view callers still need DOM refs.** → Decision 4's exception keeps a ref map under a clearer name; no behaviour change for users.

## Migration Plan

This is an internal-only refactor with no public API surface. Migration is the PR diff itself — no feature flag, no phased rollout, no data migration. Rollback is `git revert` of the PR.

Order of changes within the PR (recommended for reviewability):

1. Add `expandedPanelHeights` state + `onHeightChange` prop wiring on `TaskGraphView` and `TaskGraphExpandedDetails`. New pipeline runs in parallel with the existing one but the rendered edges still come from the old path. Tests still pass.
2. Switch `TaskGraphEdges` to consume `expandedPanelHeights` and layout coordinates. Remove the old `rowRefs` prop. Update its tests in the same commit.
3. Remove `rowRefs` from `TaskGraphView` (or rename it to `scrollAnchorRefs` if scroll-into-view depends on it). Remove ref-callback on `TaskGraphIssueRow`. Update storybook.
4. Run full pre-PR checklist from `/workdir/CLAUDE.md`: `npm run lint:fix`, `format:check`, `typecheck`, `npm test`, `npm test:e2e`, `npm run build-storybook`.

Verification: load the issues page, expand a row, then toggle between "tree" and "next" view modes. Edges should match new row positions immediately, no refresh required. Repeat with a row added or removed mid-session.
