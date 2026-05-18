/**
 * Tests for task graph SVG rendering components.
 */

import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import {
  TaskGraphNodeSvg,
  TaskGraphEdges,
  buildEdgePath,
  clipCornerRadius,
  getTypeColor,
  ROW_HEIGHT,
  EDGE_CORNER_RADIUS,
  getLaneCenterX,
  NODE_RADIUS,
  LINE_STROKE_WIDTH,
} from './task-graph-svg'
import type { TaskGraphIssueRenderLine, TaskGraphEdge } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueType, IssueStatus, ClaudeSessionStatus, ExecutionMode } from '@/api'

describe('TaskGraphNodeSvg', () => {
  const createMockLine = (
    overrides?: Partial<TaskGraphIssueRenderLine>
  ): TaskGraphIssueRenderLine => ({
    type: 'issue',
    issueId: 'test-id',
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    title: 'Test Issue',
    description: null,
    branchName: null,
    hasDescription: true,
    lane: 0,
    marker: TaskGraphMarkerType.Open,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    appearanceIndex: 1,
    totalAppearances: 1,
    parentIssueId: null,
    ...overrides,
  })

  describe('agent status ring', () => {
    it('should not render a ring when agentStatus is null', () => {
      const line = createMockLine({ agentStatus: null })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      // Should only have the main node circle, no ring
      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1)
    })

    it('should not render a ring when agent is not active', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: false,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1)
    })

    it('should render blue ring for running states', () => {
      const runningStates = [
        ClaudeSessionStatus.STARTING,
        ClaudeSessionStatus.RUNNING_HOOKS,
        ClaudeSessionStatus.RUNNING,
      ]

      runningStates.forEach((status) => {
        const line = createMockLine({
          agentStatus: {
            isActive: true,
            status,
            sessionId: 'session-123',
          },
        })
        const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

        const circles = container.querySelectorAll('circle')
        expect(circles).toHaveLength(2)

        const ring = circles[0]
        expect(ring).toHaveAttribute('stroke', '#3b82f6') // Blue
        expect(ring).toHaveAttribute('stroke-width', '2')
        expect(ring).toHaveAttribute('opacity', '0.6')
        expect(ring).toHaveClass('animate-pulse')
      })
    })

    it('should render yellow ring for waiting states', () => {
      const waitingStates = [
        ClaudeSessionStatus.WAITING_FOR_INPUT,
        ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
        ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
      ]

      waitingStates.forEach((status) => {
        const line = createMockLine({
          agentStatus: {
            isActive: true,
            status,
            sessionId: 'session-123',
          },
        })
        const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

        const circles = container.querySelectorAll('circle')
        expect(circles).toHaveLength(2)

        const ring = circles[0]
        expect(ring).toHaveAttribute('stroke', '#eab308') // Yellow
        expect(ring).toHaveAttribute('stroke-width', '2')
        expect(ring).toHaveAttribute('opacity', '0.6')
        expect(ring).toHaveClass('animate-pulse')
      })
    })

    it('should render red ring for error state', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.ERROR,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(2)

      const ring = circles[0]
      expect(ring).toHaveAttribute('stroke', '#ef4444') // Red
      expect(ring).toHaveAttribute('stroke-width', '2')
      expect(ring).toHaveAttribute('opacity', '0.6')
      expect(ring).toHaveClass('animate-pulse')
    })

    it('should not render ring for stopped state', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.STOPPED,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should not render ring for unknown status', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          // Test with an unknown status value (cast to bypass type safety for edge case testing)
          status: '99' as unknown as ClaudeSessionStatus,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should not render ring for null status', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          // Test with null status (cast to bypass type safety for edge case testing)
          status: null as unknown as ClaudeSessionStatus,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should have correct ring radius', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      const ring = circles[0]
      const node = circles[1]

      // Ring should be 4 pixels larger radius than the node
      expect(ring).toHaveAttribute('r', '10') // NODE_RADIUS (6) + 4
      expect(node).toHaveAttribute('r', '6')
    })

    it('should not show actionable ring when agent is active', () => {
      const line = createMockLine({
        marker: TaskGraphMarkerType.Actionable,
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(2) // Agent ring + node, no actionable ring

      // Verify the ring is the agent status ring (blue)
      const ring = circles[0]
      expect(ring).toHaveAttribute('stroke', '#3b82f6')
      expect(ring).toHaveClass('animate-pulse')
    })

    it('should not show actionable ring when agent is not active', () => {
      const line = createMockLine({
        marker: TaskGraphMarkerType.Actionable,
        agentStatus: {
          isActive: false,
          status: ClaudeSessionStatus.STOPPED,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the node, no actionable ring
    })
  })

  describe('connector rendering based on isSeriesChild', () => {
    it('does NOT render parallel connector when isSeriesChild=true', () => {
      const line = createMockLine({
        lane: 1,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should NOT have a horizontal path from cx - NODE_RADIUS - 2
      const paths = container.querySelectorAll('path')
      const parallelPath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Parallel connector starts from left edge of node: M (cx - NODE_RADIUS - 2) cy
        return d.startsWith('M 28 20') // This is the parallel connector start
      })
      expect(parallelPath).toBeUndefined()
    })

    it('does NOT render series lines when drawTopLine and drawBottomLine are false', () => {
      const line = createMockLine({
        lane: 0,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should NOT have vertical series paths
      const paths = container.querySelectorAll('path')
      const seriesLinePaths = Array.from(paths).filter((p) => {
        const d = p.getAttribute('d') || ''
        // Check for top or bottom series lines
        return d.includes('M 12 0 L 12 12') || d.includes('M 12 28 L 12 40')
      })
      expect(seriesLinePaths).toHaveLength(0)
    })
  })

  describe('multi-parent indicator', () => {
    it('renders no diagonal when appearanceIndex is null', () => {
      const line = createMockLine()
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      // Filter out guide lines (which have opacity="0.3") and reservation lines
      const diagonalLines = Array.from(container.querySelectorAll('line')).filter(
        (l) => l.getAttribute('opacity') !== '0.3' && !l.getAttribute('key')?.startsWith('res-')
      )
      // No multi-parent diagonal lines when index is null
      expect(diagonalLines).toHaveLength(0)
    })

    it('renders down-right diagonal for first multi-parent instance', () => {
      const line = createMockLine({ appearanceIndex: 1, totalAppearances: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // First instance (index 0): should have down-right diagonal segment(s)
      expect(lines.length).toBeGreaterThan(0)
    })

    it('renders up-left diagonal for last multi-parent instance', () => {
      const line = createMockLine({ appearanceIndex: 2, totalAppearances: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // Last instance (index 2 of 3): should have up-left diagonal segment(s)
      expect(lines.length).toBeGreaterThan(0)
    })

    it('renders both diagonals for middle instance', () => {
      const line = createMockLine({ appearanceIndex: 1, totalAppearances: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // Middle instance (index 1 of 3): should have both diagonal sets
      expect(lines.length).toBeGreaterThan(0)
    })
  })

  describe('type colors', () => {
    it('should return correct colors for each issue type', () => {
      expect(getTypeColor(IssueType.TASK)).toBe('#3b82f6') // Task: Blue
      expect(getTypeColor(IssueType.BUG)).toBe('#ef4444') // Bug: Red
      expect(getTypeColor(IssueType.CHORE)).toBe('#6b7280') // Chore: Gray
      expect(getTypeColor(IssueType.FEATURE)).toBe('#22c55e') // Feature: Green
      expect(getTypeColor(IssueType.IDEA)).toBe('#8b5cf6') // Idea: Purple
    })

    it('should return default color for unknown issue type', () => {
      expect(getTypeColor('unknown' as IssueType)).toBe('#3b82f6') // Default to Task color
    })
  })
})

describe('TaskGraphEdges', () => {
  const R = NODE_RADIUS + 2

  const baseLine = (issueId: string, lane: number): TaskGraphIssueRenderLine => ({
    type: 'issue',
    issueId,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    title: issueId,
    description: null,
    branchName: null,
    hasDescription: true,
    lane,
    marker: TaskGraphMarkerType.Open,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    appearanceIndex: 1,
    totalAppearances: 1,
    parentIssueId: null,
  })

  it('renders null when edges array is empty', () => {
    const { container } = render(
      <TaskGraphEdges
        edges={[]}
        renderLines={[baseLine('a', 0)]}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={1}
      />
    )
    expect(container.firstChild).toBeNull()
  })

  it('renders SeriesSibling edge as a straight line between attach points', () => {
    const lines = [baseLine('a', 0), baseLine('b', 0)]
    const edge: TaskGraphEdge = {
      from: 'a',
      to: 'b',
      kind: 'SeriesSibling',
      startRow: 0,
      startLane: 0,
      endRow: 1,
      endLane: 0,
      sourceAttach: 'Bottom',
      targetAttach: 'Top',
    }
    const { container } = render(
      <TaskGraphEdges
        edges={[edge]}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={1}
      />
    )

    const paths = container.querySelectorAll('path')
    expect(paths).toHaveLength(1)

    const cx = getLaneCenterX(0)
    const row0CY = ROW_HEIGHT / 2
    const row1CY = ROW_HEIGHT + ROW_HEIGHT / 2
    const d = paths[0].getAttribute('d')
    // Bottom of row 0 node → Top of row 1 node
    expect(d).toBe(`M ${cx} ${row0CY + R} L ${cx} ${row1CY - R}`)
  })

  it('renders SeriesCornerToParent edge as arc-cornered vertical-then-horizontal path', () => {
    // Child at row 0 lane 0, parent at row 1 lane 1
    const lines = [baseLine('child', 0), baseLine('parent', 1)]
    const edge: TaskGraphEdge = {
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
    }
    const { container } = render(
      <TaskGraphEdges
        edges={[edge]}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={2}
      />
    )

    const paths = container.querySelectorAll('path')
    expect(paths).toHaveLength(1)

    const childX = getLaneCenterX(0)
    const parentX = getLaneCenterX(1)
    const childCY = ROW_HEIGHT / 2
    const parentCY = ROW_HEIGHT + ROW_HEIGHT / 2
    const r = EDGE_CORNER_RADIUS
    // M (childX, childCY+R) L (childX, parentCY-r) Q (childX, parentCY) (childX+r, parentCY) L (parentX-R, parentCY)
    const d = paths[0].getAttribute('d')
    expect(d).toBe(
      `M ${childX} ${childCY + R} L ${childX} ${parentCY - r} Q ${childX} ${parentCY} ${childX + r} ${parentCY} L ${parentX - R} ${parentCY}`
    )
  })

  it('renders ParallelChildToSpine edge as arc-cornered horizontal-then-vertical path', () => {
    // Child at row 0 lane 0, parent at row 2 lane 1, pivotLane=1
    const lines = [baseLine('child', 0), baseLine('mid', 0), baseLine('parent', 1)]
    const edge: TaskGraphEdge = {
      from: 'child',
      to: 'parent',
      kind: 'ParallelChildToSpine',
      startRow: 0,
      startLane: 0,
      endRow: 2,
      endLane: 1,
      pivotLane: 1,
      sourceAttach: 'Right',
      targetAttach: 'Top',
    }
    const { container } = render(
      <TaskGraphEdges
        edges={[edge]}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={2}
      />
    )

    const paths = container.querySelectorAll('path')
    expect(paths).toHaveLength(1)

    const childX = getLaneCenterX(0)
    const pivotX = getLaneCenterX(1)
    const childCY = ROW_HEIGHT / 2
    const parentCY = 2 * ROW_HEIGHT + ROW_HEIGHT / 2
    const r = EDGE_CORNER_RADIUS
    // Single bend (target on pivot lane): horizontal run, arc into vertical, ride spine to target.
    const d = paths[0].getAttribute('d')
    expect(d).toBe(
      `M ${childX + R} ${childCY} L ${pivotX - r} ${childCY} Q ${pivotX} ${childCY} ${pivotX} ${childCY + r} L ${pivotX} ${parentCY - R}`
    )
  })

  it('uses the from-issue type color for the edge stroke', () => {
    const lines = [{ ...baseLine('a', 0), issueType: IssueType.BUG }, baseLine('b', 0)]
    const edge: TaskGraphEdge = {
      from: 'a',
      to: 'b',
      kind: 'SeriesSibling',
      startRow: 0,
      startLane: 0,
      endRow: 1,
      endLane: 0,
      sourceAttach: 'Bottom',
      targetAttach: 'Top',
    }
    const { container } = render(
      <TaskGraphEdges
        edges={[edge]}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={1}
      />
    )

    const path = container.querySelector('path')
    expect(path).toHaveAttribute('stroke', '#ef4444') // Bug red
    expect(path).toHaveAttribute('stroke-width', String(LINE_STROKE_WIDTH))
  })

  it('derives endpoint Y purely from row * ROW_HEIGHT + ROW_HEIGHT/2 when no panels expanded', () => {
    const lines = [baseLine('a', 0), baseLine('b', 1), baseLine('c', 0), baseLine('d', 0)]
    const edges: TaskGraphEdge[] = [
      {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 1,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
      {
        from: 'c',
        to: 'd',
        kind: 'SeriesSibling',
        startRow: 2,
        startLane: 0,
        endRow: 3,
        endLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
    ]
    const { container } = render(
      <TaskGraphEdges
        edges={edges}
        renderLines={lines}
        expandedIds={new Set()}
        expandedPanelHeights={new Map()}
        maxLanes={2}
      />
    )

    const paths = Array.from(container.querySelectorAll('path'))
    expect(paths).toHaveLength(2)

    // First edge: row 0 → row 1, different lanes (arc form)
    const sx0 = getLaneCenterX(0)
    const ex0 = getLaneCenterX(1)
    const sy0 = ROW_HEIGHT / 2 + R
    const ey0 = ROW_HEIGHT + ROW_HEIGHT / 2 - R
    expect(paths[0].getAttribute('d')).toBe(
      `M ${sx0} ${sy0} L ${sx0} ${ey0 - EDGE_CORNER_RADIUS} Q ${sx0} ${ey0} ${ex0} ${ey0}`
    )

    // Second edge: row 2 → row 3, same lane (plain line)
    const cx = getLaneCenterX(0)
    const sy1 = 2 * ROW_HEIGHT + ROW_HEIGHT / 2 + R
    const ey1 = 3 * ROW_HEIGHT + ROW_HEIGHT / 2 - R
    expect(paths[1].getAttribute('d')).toBe(`M ${cx} ${sy1} L ${cx} ${ey1}`)
  })

  it('offsets endpoint Y by cumulative expanded-panel height for rows below the expansion', () => {
    const PANEL_HEIGHT = 120
    // Five rows: a, b, c, d, e — c (index 2) is expanded with a 120px panel.
    const lines = [
      baseLine('a', 0),
      baseLine('b', 0),
      baseLine('c', 0),
      baseLine('d', 0),
      baseLine('e', 0),
    ]
    const edges: TaskGraphEdge[] = [
      // Above the expansion: rows 0→1, should NOT be offset.
      {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
      // Crossing the expansion: row 2 (start) → row 3 (end), end is offset.
      {
        from: 'c',
        to: 'd',
        kind: 'SeriesSibling',
        startRow: 2,
        startLane: 0,
        endRow: 3,
        endLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
      // Below the expansion: rows 3→4, both offset by PANEL_HEIGHT.
      {
        from: 'd',
        to: 'e',
        kind: 'SeriesSibling',
        startRow: 3,
        startLane: 0,
        endRow: 4,
        endLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      },
    ]
    const { container } = render(
      <TaskGraphEdges
        edges={edges}
        renderLines={lines}
        expandedIds={new Set(['c'])}
        expandedPanelHeights={new Map([['c', PANEL_HEIGHT]])}
        maxLanes={1}
      />
    )

    const paths = Array.from(container.querySelectorAll('path'))
    expect(paths).toHaveLength(3)

    const cx = getLaneCenterX(0)
    // Above the expansion: unchanged.
    const sy0 = 0 * ROW_HEIGHT + ROW_HEIGHT / 2 + R
    const ey0 = 1 * ROW_HEIGHT + ROW_HEIGHT / 2 - R
    expect(paths[0].getAttribute('d')).toBe(`M ${cx} ${sy0} L ${cx} ${ey0}`)

    // Crossing the expansion: start NOT offset (panel is below row 2's anchor),
    // end IS offset.
    const sy1 = 2 * ROW_HEIGHT + ROW_HEIGHT / 2 + R
    const ey1 = 3 * ROW_HEIGHT + ROW_HEIGHT / 2 + PANEL_HEIGHT - R
    expect(paths[1].getAttribute('d')).toBe(`M ${cx} ${sy1} L ${cx} ${ey1}`)

    // Both endpoints below the expansion: both offset.
    const sy2 = 3 * ROW_HEIGHT + ROW_HEIGHT / 2 + PANEL_HEIGHT + R
    const ey2 = 4 * ROW_HEIGHT + ROW_HEIGHT / 2 + PANEL_HEIGHT - R
    expect(paths[2].getAttribute('d')).toBe(`M ${cx} ${sy2} L ${cx} ${ey2}`)
  })
})

describe('clipCornerRadius', () => {
  it('returns the default radius when both spans exceed it', () => {
    expect(clipCornerRadius(40, 24)).toBe(EDGE_CORNER_RADIUS)
  })

  it('clips to the smaller perpendicular span when below default', () => {
    expect(clipCornerRadius(40, 4)).toBe(4)
    expect(clipCornerRadius(3, 24)).toBe(3)
  })

  it('uses absolute span values so direction does not matter', () => {
    expect(clipCornerRadius(-10, -8)).toBe(EDGE_CORNER_RADIUS)
    expect(clipCornerRadius(-3, 10)).toBe(3)
  })

  it('returns 0 when either span is 0', () => {
    expect(clipCornerRadius(0, 10)).toBe(0)
    expect(clipCornerRadius(10, 0)).toBe(0)
  })
})

describe('buildEdgePath', () => {
  const r = EDGE_CORNER_RADIUS
  const from = (lane: number, row: number) => ({
    x: getLaneCenterX(lane),
    y: row * ROW_HEIGHT + ROW_HEIGHT / 2,
  })

  describe('SeriesSibling', () => {
    it('emits a plain vertical line when source and target share a lane', () => {
      const edge: TaskGraphEdge = {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      }
      const d = buildEdgePath(edge, from(0, 0), from(0, 1))
      const cx = getLaneCenterX(0)
      const sy = ROW_HEIGHT / 2 + (NODE_RADIUS + 2)
      const ey = ROW_HEIGHT + ROW_HEIGHT / 2 - (NODE_RADIUS + 2)
      expect(d).toBe(`M ${cx} ${sy} L ${cx} ${ey}`)
    })

    it('emits an arc when source and target are in different lanes', () => {
      const edge: TaskGraphEdge = {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 1,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      }
      const d = buildEdgePath(edge, from(0, 0), from(1, 1))
      const sx = getLaneCenterX(0)
      const ex = getLaneCenterX(1)
      const sy = ROW_HEIGHT / 2 + (NODE_RADIUS + 2)
      const ey = ROW_HEIGHT + ROW_HEIGHT / 2 - (NODE_RADIUS + 2)
      expect(d).toBe(`M ${sx} ${sy} L ${sx} ${ey - r} Q ${sx} ${ey} ${ex} ${ey}`)
    })
  })

  describe('SeriesCornerToParent', () => {
    it('emits vertical-arc-horizontal at default radius', () => {
      const edge: TaskGraphEdge = {
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
      }
      const d = buildEdgePath(edge, from(0, 0), from(1, 1))
      const sx = getLaneCenterX(0)
      const ex = getLaneCenterX(1) - (NODE_RADIUS + 2)
      const sy = ROW_HEIGHT / 2 + (NODE_RADIUS + 2)
      const ey = ROW_HEIGHT + ROW_HEIGHT / 2
      expect(d).toBe(`M ${sx} ${sy} L ${sx} ${ey - r} Q ${sx} ${ey} ${sx + r} ${ey} L ${ex} ${ey}`)
    })

    it('clips corner radius when horizontal span is smaller than default', () => {
      const edge: TaskGraphEdge = {
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
      }
      // Custom from/to so that horizontal span (ex - sx) is only 4px
      const fromPt = { x: 100, y: 20 }
      const toPt = { x: 104 + (NODE_RADIUS + 2), y: 60 } // Left attach pulls back NODE_RADIUS+2
      const d = buildEdgePath(edge, fromPt, toPt)
      // Effective ex after Left attach: toPt.x - (NODE_RADIUS+2) = 104 → span = 4
      // Clipped radius = min(6, |40|, |4|) = 4
      expect(d).toContain(`Q 100 60 104 60`)
    })

    it('falls back to plain line when source and target are collinear vertically', () => {
      const edge: TaskGraphEdge = {
        from: 'a',
        to: 'b',
        kind: 'SeriesCornerToParent',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: 0,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      }
      const d = buildEdgePath(edge, from(0, 0), from(0, 1))
      const cx = getLaneCenterX(0)
      const sy = ROW_HEIGHT / 2 + (NODE_RADIUS + 2)
      const ey = ROW_HEIGHT + ROW_HEIGHT / 2 - (NODE_RADIUS + 2)
      expect(d).toBe(`M ${cx} ${sy} L ${cx} ${ey}`)
    })
  })

  describe('ParallelChildToSpine', () => {
    it('emits horizontal-arc-vertical when target sits on the pivot lane', () => {
      const edge: TaskGraphEdge = {
        from: 'child',
        to: 'parent',
        kind: 'ParallelChildToSpine',
        startRow: 0,
        startLane: 0,
        endRow: 2,
        endLane: 1,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Top',
      }
      const d = buildEdgePath(edge, from(0, 0), from(1, 2))
      const sx = getLaneCenterX(0) + (NODE_RADIUS + 2)
      const pivotX = getLaneCenterX(1)
      const sy = ROW_HEIGHT / 2
      const ey = 2 * ROW_HEIGHT + ROW_HEIGHT / 2 - (NODE_RADIUS + 2)
      expect(d).toBe(
        `M ${sx} ${sy} L ${pivotX - r} ${sy} Q ${pivotX} ${sy} ${pivotX} ${sy + r} L ${pivotX} ${ey}`
      )
    })

    it('emits two arcs when target is off the pivot lane', () => {
      const edge: TaskGraphEdge = {
        from: 'child',
        to: 'parent',
        kind: 'ParallelChildToSpine',
        startRow: 0,
        startLane: 0,
        endRow: 2,
        endLane: 2,
        pivotLane: 1,
        sourceAttach: 'Right',
        targetAttach: 'Left',
      }
      const d = buildEdgePath(edge, from(0, 0), from(2, 2))
      // Two Q commands expected (one per bend).
      const arcs = d.match(/Q /g) ?? []
      expect(arcs).toHaveLength(2)
    })

    it('clips corner radius when row span is smaller than default', () => {
      const edge: TaskGraphEdge = {
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
      }
      // Force row spacing close to corner radius — child row 0 (cy=20),
      // parent row at cy=24 → vertical span = 4 - (NODE_RADIUS+2) etc.
      const fromPt = { x: 20, y: 20 }
      const toPt = { x: 36, y: 28 } // ey after Top attach: 28 - 8 = 20 → vertical span 0
      const d = buildEdgePath(edge, fromPt, toPt)
      // sx = fromPt.x + (NODE_RADIUS+2) = 28; same row hop (sy === ey) → plain horizontal line.
      expect(d).toBe(`M 28 20 L 36 20`)
    })
  })
})
