/**
 * SVG rendering for task graph nodes and connectors.
 */

import { memo, useMemo } from 'react'
import type React from 'react'
import { ClaudeSessionStatus, IssueType } from '@/api'
import type { IssueType as IssueTypeEnum } from '@/api'
import type { TaskGraphIssueRenderLine, TaskGraphRenderLine, TaskGraphEdge } from '../services'

// Constants matching TimelineSvgRenderer.cs
export const LANE_WIDTH = 24
export const ROW_HEIGHT = 40
export const NODE_RADIUS = 6
export const LINE_STROKE_WIDTH = 2
export const EDGE_CORNER_RADIUS = 6

/** Type colors matching the issue acceptance criteria */
const TYPE_COLORS: Record<string, string> = {
  [IssueType.TASK]: '#3b82f6', // Task: Blue
  [IssueType.BUG]: '#ef4444', // Bug: Red
  [IssueType.CHORE]: '#6b7280', // Chore: Gray
  [IssueType.FEATURE]: '#22c55e', // Feature: Green
  [IssueType.IDEA]: '#8b5cf6', // Idea: Purple
  [IssueType.VERIFY]: '#3b82f6', // Verify: Blue (same as Task)
}

export function getTypeColor(issueType: IssueTypeEnum): string {
  return TYPE_COLORS[issueType] ?? TYPE_COLORS[IssueType.TASK]
}

/**
 * Maps agent status to ring color based on status value.
 * Returns null if no ring should be shown.
 * Handles string status values.
 */
function getAgentStatusColor(status: string | null): string | null {
  if (!status) return null

  // Map status to colors using the enum values
  switch (status) {
    case ClaudeSessionStatus.STARTING:
    case ClaudeSessionStatus.RUNNING_HOOKS:
    case ClaudeSessionStatus.RUNNING:
      return '#3b82f6' // Blue
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return '#eab308' // Yellow
    case ClaudeSessionStatus.ERROR:
      return '#ef4444' // Red
    case ClaudeSessionStatus.STOPPED:
    default:
      return null // No ring for Stopped or unknown status
  }
}

export function calculateSvgWidth(maxLanes: number): number {
  return LANE_WIDTH * Math.max(maxLanes, 1) + LANE_WIDTH / 2
}

export function getLaneCenterX(laneIndex: number): number {
  return LANE_WIDTH / 2 + laneIndex * LANE_WIDTH
}

export function getRowCenterY(): number {
  return ROW_HEIGHT / 2
}

interface TaskGraphNodeSvgProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  /**
   * When true, the node renders as a square instead of a circle. Used to
   * signal that the issue has a linked OpenSpec change (see
   * openspec-integration spec §Issue node shape).
   */
  squareNode?: boolean
}

/**
 * Renders an SVG for a task graph issue node (shape + indicators only; connectors are
 * handled by the TaskGraphEdges overlay).
 */
export const TaskGraphNodeSvg = memo(function TaskGraphNodeSvg({
  line,
  maxLanes,
  squareNode = false,
}: TaskGraphNodeSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const cx = getLaneCenterX(line.lane)
  const cy = getRowCenterY()
  const nodeColor = getTypeColor(line.issueType)

  const isOutlineOnly = !line.hasDescription

  return (
    <svg width={width} height={ROW_HEIGHT} className="shrink-0" aria-hidden="true">
      <LaneGuideLines maxLanes={maxLanes} />

      {/* Agent status ring */}
      {line.agentStatus?.isActive &&
        (() => {
          const color = getAgentStatusColor(line.agentStatus.status)
          return color ? (
            <circle
              cx={cx}
              cy={cy}
              r={NODE_RADIUS + 4}
              fill="none"
              stroke={color}
              strokeWidth={2}
              opacity={0.6}
              className="animate-pulse"
            />
          ) : null
        })()}

      {/* Node shape: square when linked to an OpenSpec change, round otherwise */}
      {squareNode ? (
        <rect
          x={cx - NODE_RADIUS}
          y={cy - NODE_RADIUS}
          width={NODE_RADIUS * 2}
          height={NODE_RADIUS * 2}
          fill={isOutlineOnly ? 'none' : nodeColor}
          stroke={nodeColor}
          strokeWidth={2}
          data-testid="task-graph-node-square"
        />
      ) : isOutlineOnly ? (
        <circle cx={cx} cy={cy} r={NODE_RADIUS} fill="none" stroke={nodeColor} strokeWidth={2} />
      ) : (
        <circle cx={cx} cy={cy} r={NODE_RADIUS} fill={nodeColor} />
      )}

      {/* Multi-parent diagonal indicator (issue appears in multiple parent chains) */}
      {line.totalAppearances > 1 && (
        <MultiParentIndicator
          cx={cx}
          cy={cy}
          nodeColor={nodeColor}
          appearanceIndex={line.appearanceIndex}
          totalAppearances={line.totalAppearances}
        />
      )}
    </svg>
  )
})

export function LaneGuideLines({ maxLanes }: { maxLanes: number }) {
  return (
    <>
      {Array.from({ length: maxLanes }, (_, i) => (
        <line
          key={`guide-${i}`}
          x1={getLaneCenterX(i)}
          y1={0}
          x2={getLaneCenterX(i)}
          y2={ROW_HEIGHT}
          stroke="#e5e7eb"
          strokeWidth={1}
          opacity={0.3}
        />
      ))}
    </>
  )
}

interface MultiParentIndicatorProps {
  cx: number
  cy: number
  nodeColor: string
  appearanceIndex: number
  totalAppearances: number
}

/**
 * Renders diagonal lines indicating multi-parent issue instances.
 * First instance: diagonal down-right. Last instance: diagonal up-left. Middle: both.
 * Uses stepped opacity segments for a fade effect.
 */
function MultiParentIndicator({
  cx,
  cy,
  nodeColor,
  appearanceIndex,
  totalAppearances,
}: MultiParentIndicatorProps) {
  const lineLength = ROW_HEIGHT / 2
  const segmentCount = 3
  const segmentLength = lineLength / segmentCount
  const isFirst = appearanceIndex <= 1
  const isLast = appearanceIndex >= totalAppearances

  // Down-right diagonal (shown on first and middle instances)
  const showDownRight = !isLast
  // Up-left diagonal (shown on last and middle instances)
  const showUpLeft = !isFirst

  const segments: React.ReactNode[] = []

  if (showDownRight) {
    for (let i = 0; i < segmentCount; i++) {
      const opacity = 0.5 - (i * 0.5) / segmentCount
      const x1 = cx + NODE_RADIUS + 2 + i * segmentLength * 0.7
      const y1 = cy + NODE_RADIUS + 2 + i * segmentLength * 0.7
      const x2 = cx + NODE_RADIUS + 2 + (i + 1) * segmentLength * 0.7
      const y2 = cy + NODE_RADIUS + 2 + (i + 1) * segmentLength * 0.7
      segments.push(
        <line
          key={`mp-dr-${i}`}
          x1={x1}
          y1={y1}
          x2={x2}
          y2={y2}
          stroke={nodeColor}
          strokeWidth={1.5}
          opacity={opacity}
        />
      )
    }
  }

  if (showUpLeft) {
    for (let i = 0; i < segmentCount; i++) {
      const opacity = 0.5 - (i * 0.5) / segmentCount
      const x1 = cx - NODE_RADIUS - 2 - i * segmentLength * 0.7
      const y1 = cy - NODE_RADIUS - 2 - i * segmentLength * 0.7
      const x2 = cx - NODE_RADIUS - 2 - (i + 1) * segmentLength * 0.7
      const y2 = cy - NODE_RADIUS - 2 - (i + 1) * segmentLength * 0.7
      segments.push(
        <line
          key={`mp-ul-${i}`}
          x1={x1}
          y1={y1}
          x2={x2}
          y2={y2}
          stroke={nodeColor}
          strokeWidth={1.5}
          opacity={opacity}
        />
      )
    }
  }

  return <>{segments}</>
}

interface TaskGraphEdgesProps {
  edges: TaskGraphEdge[]
  renderLines: TaskGraphRenderLine[]
  /** Set of issue ids whose `TaskGraphExpandedDetails` panel is currently mounted below them. */
  expandedIds: Set<string>
  /** Measured `offsetHeight` of each currently-mounted expanded panel, keyed by issueId. */
  expandedPanelHeights: Map<string, number>
  maxLanes: number
}

/**
 * Renders all graph edges as a single absolutely-positioned SVG overlay spanning
 * the full height of the issue list. Each edge maps to one <path> element; geometry
 * is derived purely from `(renderLines, edges, expandedPanelHeights)` — see
 * `openspec/changes/redraw-graph-edges-reactively/specs/fleece-issue-tracking/spec.md`.
 * No DOM `offsetTop` reads during render.
 */
export const TaskGraphEdges = memo(function TaskGraphEdges({
  edges,
  renderLines,
  expandedIds,
  expandedPanelHeights,
  maxLanes,
}: TaskGraphEdgesProps) {
  // Cumulative pixel offset to add to every row Y at index >= k from expanded
  // panels mounted above it. Computed as a prefix-sum so per-edge lookup is O(1).
  const cumulativeOffsetByRow = useMemo(() => {
    const offsets = new Array<number>(renderLines.length + 1)
    offsets[0] = 0
    let running = 0
    for (let i = 0; i < renderLines.length; i++) {
      offsets[i] = running
      const line = renderLines[i]
      if (line.type === 'issue' && expandedIds.has(line.issueId)) {
        running += expandedPanelHeights.get(line.issueId) ?? 0
      }
    }
    offsets[renderLines.length] = running
    return offsets
  }, [renderLines, expandedPanelHeights, expandedIds])

  const totalHeight = useMemo(() => {
    let panelTotal = 0
    for (const issueId of expandedIds) {
      panelTotal += expandedPanelHeights.get(issueId) ?? 0
    }
    return renderLines.length * ROW_HEIGHT + panelTotal
  }, [renderLines, expandedPanelHeights, expandedIds])

  const width = calculateSvgWidth(maxLanes)

  if (edges.length === 0) return null

  const rowY = (row: number): number =>
    row * ROW_HEIGHT + ROW_HEIGHT / 2 + (cumulativeOffsetByRow[row] ?? 0)

  return (
    <svg
      width={width}
      height={totalHeight}
      style={{ position: 'absolute', top: 0, left: 0, pointerEvents: 'none' }}
      aria-hidden="true"
    >
      {edges.map((edge, i) => {
        const startLine = renderLines[edge.startRow]
        const strokeColor =
          startLine && startLine.type === 'issue' ? getTypeColor(startLine.issueType) : '#3b82f6'

        const from = {
          x: getLaneCenterX(edge.startLane),
          y: rowY(edge.startRow),
        }
        const to = {
          x: getLaneCenterX(edge.endLane),
          y: rowY(edge.endRow),
        }

        const d = buildEdgePath(edge, from, to)
        return (
          <path
            key={`edge-${i}`}
            d={d}
            stroke={strokeColor}
            strokeWidth={LINE_STROKE_WIDTH}
            fill="none"
          />
        )
      })}
    </svg>
  )
})

function getAttachPoint(cx: number, cy: number, side: string): [number, number] {
  const r = NODE_RADIUS + 2
  switch (side) {
    case 'Top':
      return [cx, cy - r]
    case 'Bottom':
      return [cx, cy + r]
    case 'Left':
      return [cx - r, cy]
    case 'Right':
      return [cx + r, cy]
    default:
      return [cx, cy]
  }
}

/**
 * Quarter-arc corner radius for orthogonal edges. Clipped to never overrun the
 * shorter of the two perpendicular spans so tight spacing doesn't draw the arc
 * past the target attach point.
 */
export function clipCornerRadius(spanA: number, spanB: number): number {
  const limit = Math.min(Math.abs(spanA), Math.abs(spanB))
  return Math.max(0, Math.min(EDGE_CORNER_RADIUS, limit))
}

export function buildEdgePath(
  edge: TaskGraphEdge,
  from: { x: number; y: number },
  to: { x: number; y: number }
): string {
  const [sx, sy] = getAttachPoint(from.x, from.y, edge.sourceAttach)
  const [ex, ey] = getAttachPoint(to.x, to.y, edge.targetAttach)

  switch (edge.kind) {
    case 'SeriesSibling': {
      // Same-lane vertical run: no corner, plain line.
      if (sx === ex) {
        return `M ${sx} ${sy} L ${ex} ${ey}`
      }
      // Off-axis sibling: vertical run, then quarter-arc into target attach.
      const r = clipCornerRadius(ey - sy, ex - sx)
      const verticalEnd = ey - Math.sign(ey - sy) * r
      return `M ${sx} ${sy} L ${sx} ${verticalEnd} Q ${sx} ${ey} ${ex} ${ey}`
    }

    case 'SeriesCornerToParent': {
      // Degenerate cases: source and target collinear → plain line.
      if (sx === ex || sy === ey) {
        return `M ${sx} ${sy} L ${ex} ${ey}`
      }
      // Vertical from source down to (sx, ey - r), arc into (sx + r·sign, ey),
      // horizontal to target.
      const r = clipCornerRadius(ey - sy, ex - sx)
      const verticalEnd = ey - Math.sign(ey - sy) * r
      const arcEnd = sx + Math.sign(ex - sx) * r
      return `M ${sx} ${sy} L ${sx} ${verticalEnd} Q ${sx} ${ey} ${arcEnd} ${ey} L ${ex} ${ey}`
    }

    case 'ParallelChildToSpine': {
      const pivotX = edge.pivotLane != null ? getLaneCenterX(edge.pivotLane) : to.x

      // Source already on the spine: pure vertical from source down to target.
      if (sx === pivotX) {
        if (ex === pivotX) {
          return `M ${sx} ${sy} L ${ex} ${ey}`
        }
        // Source above pivot → vertical run then arc into horizontal toward target.
        const r = clipCornerRadius(ey - sy, ex - pivotX)
        const verticalEnd = ey - Math.sign(ey - sy) * r
        const arcEnd = pivotX + Math.sign(ex - pivotX) * r
        return `M ${sx} ${sy} L ${pivotX} ${verticalEnd} Q ${pivotX} ${ey} ${arcEnd} ${ey} L ${ex} ${ey}`
      }

      // Source on the same row as the target (same-row hop): plain horizontal.
      if (sy === ey) {
        return `M ${sx} ${sy} L ${ex} ${ey}`
      }

      // First bend: horizontal run from source toward pivot, arc into vertical.
      const r1 = clipCornerRadius(pivotX - sx, ey - sy)
      const horizontalEnd = pivotX - Math.sign(pivotX - sx) * r1
      const verticalArcEnd = sy + Math.sign(ey - sy) * r1

      let d = `M ${sx} ${sy} L ${horizontalEnd} ${sy} Q ${pivotX} ${sy} ${pivotX} ${verticalArcEnd}`

      // Target sits on the spine: ride the spine straight down to ey.
      if (ex === pivotX) {
        return `${d} L ${pivotX} ${ey}`
      }

      // Second bend: vertical run, arc out of spine, horizontal to target.
      const r2 = clipCornerRadius(ey - verticalArcEnd, ex - pivotX)
      const verticalEnd = ey - Math.sign(ey - verticalArcEnd) * r2
      const arcEnd = pivotX + Math.sign(ex - pivotX) * r2
      d += ` L ${pivotX} ${verticalEnd} Q ${pivotX} ${ey} ${arcEnd} ${ey} L ${ex} ${ey}`
      return d
    }

    default:
      return `M ${sx} ${sy} L ${ex} ${ey}`
  }
}
