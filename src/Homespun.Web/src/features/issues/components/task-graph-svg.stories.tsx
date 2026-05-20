/**
 * Edge-rendering stories that drive `TaskGraphEdges` (in `task-graph-svg.tsx`)
 * with synthetic edge arrays — no server data, no `useTaskGraph()` query.
 *
 * Phase B owns the algorithm port; the edge-renderer rewrite (Phase B/C bridge,
 * task 4.x) will swap `buildEdgePath` to arc-cornered orthogonal paths. These
 * stories outlast that swap: they exercise each `EdgeKind` plus a dense-routing
 * scenario and a tight-spacing scenario, which is exactly what the Phase B/C
 * bridge needs to validate visually.
 */

import type { Meta, StoryObj } from '@storybook/react-vite'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import {
  ROW_HEIGHT,
  TaskGraphEdges,
  calculateSvgWidth,
  getLaneCenterX,
  getRowCenterY,
} from './task-graph-svg'
import {
  TaskGraphMarkerType,
  type TaskGraphEdge,
  type TaskGraphIssueRenderLine,
  type TaskGraphRenderLine,
} from '../services'

function issueLine(
  overrides: Partial<TaskGraphIssueRenderLine> & Pick<TaskGraphIssueRenderLine, 'issueId' | 'lane'>
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    title: overrides.issueId,
    description: null,
    branchName: null,
    marker: TaskGraphMarkerType.Open,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    hasDescription: false,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    parentIssueId: null,
    appearanceIndex: 1,
    totalAppearances: 1,
    ...overrides,
  }
}

function NodeMarkers({ lines, maxLanes }: { lines: TaskGraphRenderLine[]; maxLanes: number }) {
  const width = calculateSvgWidth(maxLanes)
  return (
    <svg
      width={width}
      height={ROW_HEIGHT * lines.length}
      style={{ position: 'absolute', top: 0, left: 0 }}
      aria-hidden="true"
    >
      {lines.map((line, i) => {
        if (line.type !== 'issue') return null
        const cx = getLaneCenterX(line.lane)
        const cy = i * ROW_HEIGHT + getRowCenterY()
        return <circle key={i} cx={cx} cy={cy} r={6} fill="#3b82f6" />
      })}
    </svg>
  )
}

function Frame({
  lines,
  edges,
  maxLanes,
}: {
  lines: TaskGraphRenderLine[]
  edges: TaskGraphEdge[]
  maxLanes: number
}) {
  const width = calculateSvgWidth(maxLanes)
  return (
    <div
      style={{
        position: 'relative',
        width,
        height: ROW_HEIGHT * lines.length,
        background: '#fff',
      }}
    >
      <NodeMarkers lines={lines} maxLanes={maxLanes} />
      <TaskGraphEdges
        edges={edges}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={maxLanes}
      />
    </div>
  )
}

const meta: Meta<typeof Frame> = {
  title: 'features/issues/TaskGraphSvg/EdgeKinds',
  component: Frame,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Frame>

/**
 * `seriesSibling`: vertical run between two siblings sitting in the same lane.
 */
export const SeriesSibling: Story = {
  args: {
    maxLanes: 1,
    lines: [issueLine({ issueId: 'a', lane: 0 }), issueLine({ issueId: 'b', lane: 0 })],
    edges: [
      {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
    ],
  },
}

/**
 * `seriesCornerToParent`: vertical run from the last child up, then a
 * horizontal hop into the parent's left side.
 */
export const SeriesCornerToParent: Story = {
  args: {
    maxLanes: 2,
    lines: [issueLine({ issueId: 'child', lane: 0 }), issueLine({ issueId: 'parent', lane: 1 })],
    edges: [
      {
        from: 'child',
        to: 'parent',
        kind: 'SeriesCornerToParent',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 1,
        pivotLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Left',
      },
    ],
  },
}

/**
 * `parallelChildToSpine`: horizontal hop from the child's right side onto the
 * parent's spine, then a vertical run down to the parent.
 */
export const ParallelChildToSpine: Story = {
  args: {
    maxLanes: 2,
    lines: [issueLine({ issueId: 'child', lane: 0 }), issueLine({ issueId: 'parent', lane: 1 })],
    edges: [
      {
        from: 'child',
        to: 'parent',
        kind: 'ParallelChildToSpine',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 1,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Top',
      },
    ],
  },
}

/**
 * Many edges in a single frame — a series chain of leaves under a series
 * parent + a parallel sub-tree, plus a multi-parent fan-in. Useful for eyeballing
 * how the renderer handles densely packed paths.
 */
export const ManyEdges: Story = {
  args: {
    maxLanes: 4,
    lines: [
      issueLine({ issueId: 'leaf1', lane: 0 }),
      issueLine({ issueId: 'leaf2', lane: 0 }),
      issueLine({ issueId: 'leaf3', lane: 0 }),
      issueLine({ issueId: 'sParent', lane: 1 }),
      issueLine({ issueId: 'pLeaf1', lane: 0 }),
      issueLine({ issueId: 'pLeaf2', lane: 0 }),
      issueLine({ issueId: 'pParent', lane: 2 }),
      issueLine({ issueId: 'root', lane: 3 }),
    ],
    edges: [
      // Series sub-tree: leaf1 → leaf2 → leaf3 (siblings) and leaf3 → sParent (corner).
      {
        from: 'leaf1',
        to: 'leaf2',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
      {
        from: 'leaf2',
        to: 'leaf3',
        kind: 'SeriesSibling',
        startRow: 1,
        startLane: 0,
        endRow: 2,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
      {
        from: 'leaf3',
        to: 'sParent',
        kind: 'SeriesCornerToParent',
        startRow: 2,
        startLane: 0,
        endRow: 3,
        endLane: 1,
        pivotLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Left',
      },
      // Parallel sub-tree: pLeaf1, pLeaf2 → pParent (each a parallelChildToSpine).
      {
        from: 'pLeaf1',
        to: 'pParent',
        kind: 'ParallelChildToSpine',
        startRow: 4,
        startLane: 0,
        endRow: 6,
        endLane: 2,
        pivotLane: 2,
        sourceAttach: 'Right',
        targetAttach: 'Top',
      },
      {
        from: 'pLeaf2',
        to: 'pParent',
        kind: 'ParallelChildToSpine',
        startRow: 5,
        startLane: 0,
        endRow: 6,
        endLane: 2,
        pivotLane: 2,
        sourceAttach: 'Right',
        targetAttach: 'Top',
      },
      // Both branches feed root.
      {
        from: 'sParent',
        to: 'root',
        kind: 'SeriesCornerToParent',
        startRow: 3,
        startLane: 1,
        endRow: 7,
        endLane: 3,
        pivotLane: 1,
        sourceAttach: 'Bottom',
        targetAttach: 'Left',
      },
      {
        from: 'pParent',
        to: 'root',
        kind: 'SeriesCornerToParent',
        startRow: 6,
        startLane: 2,
        endRow: 7,
        endLane: 3,
        pivotLane: 2,
        sourceAttach: 'Bottom',
        targetAttach: 'Left',
      },
    ],
  },
}

/**
 * Tight spacing: one parallelChildToSpine where the lane width and row height
 * are both close to the corner radius. Phase B/C bridge will use this to verify
 * the corner radius is clipped to `min(6, halfLane, halfRow)`.
 */
export const TightSpacing: Story = {
  args: {
    maxLanes: 1,
    lines: [issueLine({ issueId: 'a', lane: 0 }), issueLine({ issueId: 'b', lane: 0 })],
    edges: [
      {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
    ],
  },
}
