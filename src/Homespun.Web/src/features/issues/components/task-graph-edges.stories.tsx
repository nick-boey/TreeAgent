import type { Meta, StoryObj } from '@storybook/react-vite'

import { IssueStatus, IssueType, ExecutionMode } from '@/api'
import {
  TaskGraphEdges,
  ROW_HEIGHT,
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

function issueLine(overrides: Partial<TaskGraphIssueRenderLine>): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: overrides.issueId ?? 'a',
    title: overrides.title ?? 'Issue',
    description: null,
    branchName: null,
    lane: 0,
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
        const cy = i * ROW_HEIGHT + getRowCenterY()
        const cx = getLaneCenterX(line.lane)
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
        maxLanes={maxLanes}
      />
    </div>
  )
}

const meta: Meta<typeof Frame> = {
  title: 'features/issues/TaskGraphEdges',
  component: Frame,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof Frame>

export const SeriesChain: Story = {
  args: {
    maxLanes: 2,
    lines: [
      issueLine({ issueId: 'p', title: 'Parent', lane: 1 }),
      issueLine({ issueId: 'c1', title: 'Child 1', lane: 0 }),
      issueLine({ issueId: 'c2', title: 'Child 2', lane: 0 }),
      issueLine({ issueId: 'c3', title: 'Child 3', lane: 0 }),
    ],
    edges: [
      {
        from: 'c1',
        to: 'p',
        kind: 'SeriesCornerToParent',
        startRow: 1,
        startLane: 0,
        endRow: 0,
        endLane: 1,
        pivotLane: null,
        sourceAttach: 'Top',
        targetAttach: 'Bottom',
      },
      {
        from: 'c2',
        to: 'c1',
        kind: 'SeriesSibling',
        startRow: 2,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Top',
        targetAttach: 'Bottom',
      },
      {
        from: 'c3',
        to: 'c2',
        kind: 'SeriesSibling',
        startRow: 3,
        startLane: 0,
        endRow: 2,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Top',
        targetAttach: 'Bottom',
      },
    ],
  },
}

export const ParallelChildren: Story = {
  args: {
    maxLanes: 2,
    lines: [
      issueLine({ issueId: 'p', title: 'Parent', lane: 1 }),
      issueLine({ issueId: 'c1', title: 'Child 1', lane: 0 }),
      issueLine({ issueId: 'c2', title: 'Child 2', lane: 0 }),
      issueLine({ issueId: 'c3', title: 'Child 3', lane: 0 }),
    ],
    edges: [
      {
        from: 'c1',
        to: 'p',
        kind: 'ParallelChildToSpine',
        startRow: 1,
        startLane: 0,
        endRow: 0,
        endLane: 1,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Bottom',
      },
      {
        from: 'c2',
        to: 'p',
        kind: 'ParallelChildToSpine',
        startRow: 2,
        startLane: 0,
        endRow: 0,
        endLane: 1,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Bottom',
      },
      {
        from: 'c3',
        to: 'p',
        kind: 'ParallelChildToSpine',
        startRow: 3,
        startLane: 0,
        endRow: 0,
        endLane: 1,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Bottom',
      },
    ],
  },
}

export const MultiParentFanIn: Story = {
  args: {
    maxLanes: 3,
    lines: [
      issueLine({ issueId: 'p1', title: 'Parent 1', lane: 2 }),
      issueLine({ issueId: 'p2', title: 'Parent 2', lane: 1 }),
      issueLine({ issueId: 'c', title: 'Child', lane: 0 }),
    ],
    edges: [
      {
        from: 'c',
        to: 'p1',
        kind: 'SeriesCornerToParent',
        startRow: 2,
        startLane: 0,
        endRow: 0,
        endLane: 2,
        pivotLane: null,
        sourceAttach: 'Top',
        targetAttach: 'Bottom',
      },
      {
        from: 'c',
        to: 'p2',
        kind: 'SeriesCornerToParent',
        startRow: 2,
        startLane: 0,
        endRow: 1,
        endLane: 1,
        pivotLane: null,
        sourceAttach: 'Top',
        targetAttach: 'Bottom',
      },
    ],
  },
}
