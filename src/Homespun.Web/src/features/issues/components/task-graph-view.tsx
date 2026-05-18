/**
 * TaskGraphView - Primary visualization for issues on a project.
 *
 * Renders issues in a lane-based hierarchical graph with SVG connectors
 * showing parent-child relationships.
 */

import {
  memo,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  forwardRef,
  useImperativeHandle,
} from 'react'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { ErrorFallback } from '@/components/error-boundary'
import { IssueRowSkeleton } from './issue-row-skeleton'
import { IssuesEmptyState } from './issues-empty-state'
import {
  computeLayoutFromIssues,
  isIssueRenderLine,
  isPendingIssueRenderLine,
  getRenderKey,
  applyFilter,
  TaskGraphMarkerType,
  type ParsedFilter,
} from '../services'
import {
  useIssues,
  useLinkedPrs,
  useAgentStatuses,
  useOpenSpecStates,
  useOrphanChanges,
  useCreateIssue,
  useUpdateIssue,
} from '../hooks'
import {
  KeyboardEditMode,
  EditCursorPosition,
  MoveOperationType,
  ViewMode,
  type PendingNewIssue,
  type InlineEditState,
} from '../types'
import { TaskGraphIssueRow, TaskGraphExpandedDetails } from './task-graph-row'
import { InlineIssueEditor } from './inline-issue-editor'
import { OrphanedChangesList } from './orphan-changes'
import { aggregateOrphansFromInputs } from '../services/orphan-aggregation'
import { ROW_HEIGHT, LANE_WIDTH, getTypeColor, TaskGraphEdges } from './task-graph-svg'

export interface TaskGraphViewProps {
  projectId: string
  depth?: number
  searchQuery?: string
  selectedIssueId?: string | null
  onSelectIssue?: (issueId: string | null) => void
  onEditIssue?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  /** Called when clicking the Open Session button to navigate to an existing session */
  onOpenSession?: (sessionId: string) => void
  /** Active move operation type (AsChildOf or AsParentOf) */
  moveOperation?: MoveOperationType | null
  /** Source issue ID for the move operation */
  moveSourceIssueId?: string | null
  /** Called when a move target is selected */
  onMoveComplete?: (targetIssueId: string) => void
  /** Called when move operation is cancelled */
  onMoveCancel?: () => void
  /** Applied filter for client-side filtering */
  appliedFilter?: ParsedFilter | null
  /** Called when filter match count changes */
  onFilterMatchCountChange?: (count: number) => void
  /** View mode for the task graph (next or tree) */
  viewMode?: ViewMode
  className?: string
}

/** Ref handle for TaskGraphView - exposes imperative methods */
export interface TaskGraphViewRef {
  /** Create a new issue above the selected issue, or at the top if none selected */
  createAbove: () => void
  /** Create a new issue below the selected issue, or at the bottom if none selected */
  createBelow: () => void
}

/**
 * Main TaskGraphView component.
 *
 * Displays issues in a lane-based graph visualization with:
 * - SVG connectors showing parent-child relationships
 * - Series vs parallel execution mode indicators
 * - Real-time updates via SignalR
 * - Vim-like keyboard navigation
 * - Inline issue creation and editing
 * - Expand/collapse for inline details
 */
export const TaskGraphView = memo(
  forwardRef<TaskGraphViewRef, TaskGraphViewProps>(function TaskGraphView(
    {
      projectId,
      depth = 3,
      searchQuery = '',
      selectedIssueId,
      onSelectIssue,
      onEditIssue,
      onRunAgent,
      onOpenSession,
      moveOperation,
      moveSourceIssueId,
      onMoveComplete,
      onMoveCancel,
      appliedFilter,
      onFilterMatchCountChange,
      viewMode = ViewMode.Tree,
      className,
    },
    ref
  ) {
    // Per-resource hooks fetched in parallel (HTTP/2 multiplexing makes the
    // 6-roundtrip cost negligible). Each hook subscribes to the unified
    // `IssueChanged` SignalR channel and either applies the merge directly
    // (issues) or invalidates its query key.
    const issuesHook = useIssues(projectId, { includeOpenPrLinked: true })
    const issues = issuesHook.issues ?? []
    const issueIds = useMemo(() => issues.map((i) => i.id ?? '').filter(Boolean), [issues])
    const linkedPrsHook = useLinkedPrs(projectId)
    const agentStatusesHook = useAgentStatuses(projectId)
    const openSpecStatesHook = useOpenSpecStates(projectId, issueIds)
    const orphanChangesHook = useOrphanChanges(projectId)
    const isLoading = issuesHook.isLoading
    const isError = issuesHook.isError
    const refetch = issuesHook.refetch

    // Expanded rows state
    const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

    // Measured heights of currently-mounted expanded panels. Keyed by issueId;
    // height is the panel's `offsetHeight`. Seeded synchronously by each panel's
    // `useLayoutEffect` and updated by its `ResizeObserver`. Drives edge
    // cumulative-offset math in `TaskGraphEdges` — see
    // `openspec/changes/redraw-graph-edges-reactively/design.md` decision 2.
    const [expandedPanelHeights, setExpandedPanelHeights] = useState<Map<string, number>>(
      () => new Map()
    )

    const handlePanelHeightChange = useCallback((issueId: string, height: number) => {
      setExpandedPanelHeights((prev) => {
        const current = prev.get(issueId)
        if (height === 0) {
          if (current === undefined) return prev
          const next = new Map(prev)
          next.delete(issueId)
          return next
        }
        if (current === height) return prev
        const next = new Map(prev)
        next.set(issueId, height)
        return next
      })
    }, [])

    // Edit mode state
    const [editMode, setEditMode] = useState<KeyboardEditMode>(KeyboardEditMode.Viewing)
    const [pendingNewIssue, setPendingNewIssue] = useState<PendingNewIssue | null>(null)
    const [pendingEdit, setPendingEdit] = useState<InlineEditState | null>(null)

    // Refs for keyboard navigation
    const containerRef = useRef<HTMLDivElement>(null)
    const scrollAnchorRefs = useRef<Map<string, HTMLDivElement>>(new Map())

    // Create issue mutation. The editor is closed synchronously in
    // handleSave / handleSaveAndEdit before the mutation is awaited, so no
    // onSuccess close callback is needed here — the hook's optimistic
    // update keeps the new row visible during the network round-trip.
    const { createIssue, isCreating } = useCreateIssue({ projectId })

    // Update issue mutation
    const { mutateAsync: updateIssue } = useUpdateIssue({
      onSuccess: () => {
        // Reset edit mode after successful update
        setEditMode(KeyboardEditMode.Viewing)
        setPendingEdit(null)
      },
    })

    // Compute render lines + edges via the client-side TS layout port.
    // Memoised on issue set + viewMode + filter — decoration maps affect
    // render only, not layout, so they are excluded from the dep tuple.
    const layoutResult = useMemo(() => {
      return computeLayoutFromIssues({
        issues,
        linkedPrs: linkedPrsHook.linkedPrs ?? null,
        agentStatuses: agentStatusesHook.agentStatuses ?? null,
        viewMode,
        pendingIssue: pendingNewIssue
          ? {
              mode: pendingNewIssue.mode,
              referenceIssueId: pendingNewIssue.referenceIssueId,
              title: pendingNewIssue.title,
              viewMode,
            }
          : null,
      })
    }, [
      issues,
      linkedPrsHook.linkedPrs,
      agentStatusesHook.agentStatuses,
      viewMode,
      pendingNewIssue,
    ])
    const unfilteredRenderLines = layoutResult.lines
    const edges = layoutResult.edges
    const layoutCycle = layoutResult.ok ? null : layoutResult.cycle

    // Build a lookup of issue IDs to their full issue data for filtering.
    const issueDataMap = useMemo(() => {
      const map = new Map<string, (typeof issues)[number]>()
      for (const issue of issues) {
        if (issue.id) map.set(issue.id, issue)
      }
      return map
    }, [issues])

    // Suppress unused deps from the legacy server-positioned-node API.
    void depth

    // Apply filter to render lines (keeping separators and PRs for context)
    const renderLines = useMemo(() => {
      if (!appliedFilter) return unfilteredRenderLines

      return unfilteredRenderLines.filter((line) => {
        // Always keep pending-issue lines (inline editor must always be visible)
        if (isPendingIssueRenderLine(line)) return true

        // Always keep non-issue lines (PRs, separators, load more)
        if (!isIssueRenderLine(line)) return true

        // Handle isNext filter - check if issue is actionable via marker
        if (appliedFilter.isNext) {
          if (line.marker !== TaskGraphMarkerType.Actionable) {
            return false
          }
        }

        // Get the full issue data for filtering
        const issueData = issueDataMap.get(line.issueId)
        if (!issueData) return true // Keep if we can't find the issue

        return applyFilter(issueData, appliedFilter)
      })
    }, [unfilteredRenderLines, appliedFilter, issueDataMap])

    // Report filter match count
    useEffect(() => {
      if (!appliedFilter || !onFilterMatchCountChange) return

      const matchCount = renderLines.filter(isIssueRenderLine).length
      onFilterMatchCountChange(matchCount)
    }, [renderLines, appliedFilter, onFilterMatchCountChange])

    // Compute max lanes for SVG sizing.
    const maxLanes = useMemo(() => {
      return Math.max(1, ...renderLines.filter(isIssueRenderLine).map((line) => line.lane + 1))
    }, [renderLines])

    // Issue render lines only (used for create/move/edit operations + nav).
    const issueRenderLines = useMemo(() => {
      return renderLines.filter(isIssueRenderLine)
    }, [renderLines])

    // Lines navigable by keyboard.
    const navigableLines = issueRenderLines

    // Search match count
    const searchMatchCount = useMemo(() => {
      if (!searchQuery) return 0
      const lowerQuery = searchQuery.toLowerCase()
      return renderLines.filter(
        (line) => isIssueRenderLine(line) && line.title.toLowerCase().includes(lowerQuery)
      ).length
    }, [renderLines, searchQuery])

    const selectedIndex = useMemo(() => {
      if (!selectedIssueId) return -1
      return navigableLines.findIndex((line) => line.issueId === selectedIssueId)
    }, [selectedIssueId, navigableLines])

    const selectedRenderLine = useMemo(() => {
      if (selectedIndex < 0) return null
      return navigableLines[selectedIndex] ?? null
    }, [selectedIndex, navigableLines])

    // SignalR subscriptions live on each per-resource hook; no extra wiring
    // is needed here.

    // Toggle expanded state for an issue
    const toggleExpanded = useCallback((issueId: string) => {
      setExpandedIds((prev) => {
        const next = new Set(prev)
        if (next.has(issueId)) {
          next.delete(issueId)
        } else {
          next.add(issueId)
        }
        return next
      })
    }, [])

    // Handle navigating to first instance of a multi-parent issue
    const handleSelectFirstInstance = useCallback(
      (issueId: string) => {
        onSelectIssue?.(issueId)
        const firstLine = issueRenderLines.find((l) => l.issueId === issueId)
        if (firstLine) {
          const key = getRenderKey(firstLine)
          scrollAnchorRefs.current.get(key)?.scrollIntoView({ block: 'nearest' })
        }
      },
      [onSelectIssue, issueRenderLines]
    )

    // Handle row click
    const handleRowClick = useCallback(
      (issueId: string) => {
        // If in creating mode, clicking elsewhere cancels
        if (editMode === KeyboardEditMode.CreatingNew) {
          setEditMode(KeyboardEditMode.Viewing)
          setPendingNewIssue(null)
        }

        // If a move operation is active and clicking a different issue, complete the move
        if (moveOperation && moveSourceIssueId && issueId !== moveSourceIssueId) {
          onMoveComplete?.(issueId)
          return
        }

        onSelectIssue?.(issueId)
      },
      [onSelectIssue, editMode, moveOperation, moveSourceIssueId, onMoveComplete]
    )

    // ============================================================================
    // Inline Issue Creation Handlers
    // ============================================================================

    const handleCreateBelow = useCallback(() => {
      if (selectedIndex < 0) return
      const referenceIssue = issueRenderLines[selectedIndex]
      if (!referenceIssue) return

      setPendingNewIssue({
        mode: 'sibling-below',
        referenceIssueId: referenceIssue.issueId,
        title: '',
        viewMode,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [selectedIndex, issueRenderLines, viewMode])

    const handleCreateAbove = useCallback(() => {
      if (selectedIndex < 0) return
      const referenceIssue = issueRenderLines[selectedIndex]
      if (!referenceIssue) return

      setPendingNewIssue({
        mode: 'sibling-above',
        referenceIssueId: referenceIssue.issueId,
        title: '',
        viewMode,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [selectedIndex, issueRenderLines, viewMode])

    // Expose imperative methods via ref
    useImperativeHandle(
      ref,
      () => ({
        createAbove: () => {
          if (selectedIndex >= 0) {
            handleCreateAbove()
          }
        },
        createBelow: () => {
          if (selectedIndex >= 0) {
            handleCreateBelow()
          }
        },
      }),
      [selectedIndex, handleCreateAbove, handleCreateBelow]
    )

    const handleCancelEdit = useCallback(() => {
      setEditMode(KeyboardEditMode.Viewing)
      setPendingNewIssue(null)
      setPendingEdit(null)
      // Return focus to container
      containerRef.current?.focus()
    }, [])

    const handleTitleChange = useCallback((title: string) => {
      setPendingNewIssue((prev) => (prev ? { ...prev, title } : null))
      setPendingEdit((prev) => (prev ? { ...prev, title } : null))
    }, [])

    type PendingMode = 'sibling-below' | 'sibling-above' | 'child-of' | 'parent-of'

    const stateTransition = useCallback(
      (currentMode: PendingMode, key: 'Tab' | 'ShiftTab'): PendingMode => {
        // Tab from either sibling mode → parent-of (new issue wraps reference as parent)
        if ((currentMode === 'sibling-below' || currentMode === 'sibling-above') && key === 'Tab') {
          return 'parent-of'
        }
        // ShiftTab from either sibling mode → child-of (new issue nests under reference)
        if (
          (currentMode === 'sibling-below' || currentMode === 'sibling-above') &&
          key === 'ShiftTab'
        ) {
          return 'child-of'
        }
        // Undo: ShiftTab from parent-of → back to sibling-below
        if (currentMode === 'parent-of' && key === 'ShiftTab') return 'sibling-below'
        // Undo: Tab from child-of → back to sibling-above
        if (currentMode === 'child-of' && key === 'Tab') return 'sibling-above'
        // All other transitions are no-ops (e.g., second Tab from parent-of)
        return currentMode
      },
      []
    )

    const handleModeTransition = useCallback(
      (key: 'Tab' | 'ShiftTab') => () => {
        setPendingNewIssue((prev) => {
          if (!prev) return null
          const nextMode = stateTransition(prev.mode, key)
          return { ...prev, mode: nextMode }
        })
      },
      [stateTransition]
    )

    const buildCreateParams = useCallback(
      (pending: PendingNewIssue) => {
        const refIssue = issues.find(
          (i) => i.id?.toLowerCase() === pending.referenceIssueId.toLowerCase()
        )
        const refParentId = refIssue?.parentIssues?.[0]?.parentIssue ?? undefined

        switch (pending.mode) {
          case 'child-of':
            return {
              parentIssueId: pending.referenceIssueId,
              siblingIssueId: undefined as string | undefined,
              insertBefore: undefined as boolean | undefined,
              childIssueId: undefined as string | undefined,
            }
          case 'parent-of':
            return {
              parentIssueId: refParentId,
              siblingIssueId: pending.referenceIssueId,
              insertBefore: false,
              childIssueId: pending.referenceIssueId,
            }
          case 'sibling-below':
            return {
              parentIssueId: refParentId,
              siblingIssueId: pending.referenceIssueId,
              insertBefore: false,
              childIssueId: undefined as string | undefined,
            }
          case 'sibling-above':
            return {
              parentIssueId: refParentId,
              siblingIssueId: pending.referenceIssueId,
              insertBefore: true,
              childIssueId: undefined as string | undefined,
            }
        }
      },
      [issues]
    )

    const handleSave = useCallback(async () => {
      if (!pendingNewIssue?.title.trim()) {
        handleCancelEdit()
        return
      }

      const title = pendingNewIssue.title.trim()
      const params = buildCreateParams(pendingNewIssue)

      // Close the editor synchronously so a second click / Enter cannot
      // re-trigger the mutation. The hook's optimistic update keeps the new
      // row visible during the network round-trip.
      setEditMode(KeyboardEditMode.Viewing)
      setPendingNewIssue(null)
      containerRef.current?.focus()

      try {
        await createIssue({ title, ...params })
      } catch {
        // Hook rolls back the optimistic insert; nothing to restore here.
      }
    }, [pendingNewIssue, createIssue, handleCancelEdit, buildCreateParams])

    const handleSaveAndEdit = useCallback(async () => {
      if (!pendingNewIssue?.title.trim()) {
        handleCancelEdit()
        return
      }

      const title = pendingNewIssue.title.trim()
      const params = buildCreateParams(pendingNewIssue)

      setEditMode(KeyboardEditMode.Viewing)
      setPendingNewIssue(null)

      try {
        const issue = await createIssue({ title, ...params })
        if (issue?.id) {
          onEditIssue?.(issue.id)
        }
      } catch {
        // Hook rolls back the optimistic insert; nothing to restore here.
      }
    }, [pendingNewIssue, createIssue, handleCancelEdit, onEditIssue, buildCreateParams])

    // ============================================================================
    // Inline Editing Handlers (for existing issues)
    // ============================================================================

    const handleStartEditAtStart = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: selectedRenderLine.title,
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.Start,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    const handleStartEditAtEnd = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: selectedRenderLine.title,
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.End,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    const handleStartReplace = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: '',
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.Replace,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    // ============================================================================
    // Keyboard Navigation
    // ============================================================================

    const handleKeyDown = useCallback(
      (event: React.KeyboardEvent<HTMLDivElement>) => {
        // If a move operation is active, Escape cancels it
        if (moveOperation && event.key === 'Escape') {
          event.preventDefault()
          onMoveCancel?.()
          return
        }

        // If in editing mode, don't handle navigation keys
        if (editMode !== KeyboardEditMode.Viewing) {
          // Only handle Escape to cancel
          if (event.key === 'Escape') {
            event.preventDefault()
            handleCancelEdit()
          }
          return
        }

        // If nothing selected, select first navigable row on any nav key
        if (!selectedIssueId && navigableLines.length > 0) {
          if (['ArrowDown', 'ArrowUp', 'j', 'k'].includes(event.key)) {
            event.preventDefault()
            onSelectIssue?.(navigableLines[0].issueId)
            return
          }
        }

        if (!selectedIssueId) return

        const currentIndex = navigableLines.findIndex((line) => line.issueId === selectedIssueId)
        if (currentIndex === -1) return

        const currentLine = navigableLines[currentIndex]

        switch (event.key) {
          case 'ArrowDown':
          case 'j': {
            event.preventDefault()
            const nextIndex = Math.min(currentIndex + 1, navigableLines.length - 1)
            const nextLine = navigableLines[nextIndex]
            onSelectIssue?.(nextLine.issueId)
            scrollAnchorRefs.current
              .get(getRenderKey(nextLine))
              ?.scrollIntoView({ block: 'nearest' })
            break
          }

          case 'ArrowUp':
          case 'k': {
            event.preventDefault()
            const prevIndex = Math.max(currentIndex - 1, 0)
            const prevLine = navigableLines[prevIndex]
            onSelectIssue?.(prevLine.issueId)
            scrollAnchorRefs.current
              .get(getRenderKey(prevLine))
              ?.scrollIntoView({ block: 'nearest' })
            break
          }

          case 'ArrowLeft':
          case 'h': {
            event.preventDefault()
            const parentId = currentLine.parentIssueId
            if (parentId) {
              const parentLine = issueRenderLines.find((line) => line.issueId === parentId)
              if (parentLine) {
                onSelectIssue?.(parentLine.issueId)
                scrollAnchorRefs.current
                  .get(getRenderKey(parentLine))
                  ?.scrollIntoView({ block: 'nearest' })
              }
            }
            break
          }

          case 'ArrowRight':
          case 'l': {
            event.preventDefault()
            const childLine = issueRenderLines.find(
              (line) => line.parentIssueId === currentLine.issueId
            )
            if (childLine) {
              onSelectIssue?.(childLine.issueId)
              scrollAnchorRefs.current
                .get(getRenderKey(childLine))
                ?.scrollIntoView({ block: 'nearest' })
            }
            break
          }

          case 'g': {
            if (!event.shiftKey) {
              event.preventDefault()
              const firstLine = navigableLines[0]
              if (firstLine) {
                onSelectIssue?.(firstLine.issueId)
                scrollAnchorRefs.current
                  .get(getRenderKey(firstLine))
                  ?.scrollIntoView({ block: 'nearest' })
              }
            }
            break
          }

          case 'G': {
            event.preventDefault()
            const lastLine = navigableLines[navigableLines.length - 1]
            if (lastLine) {
              onSelectIssue?.(lastLine.issueId)
              scrollAnchorRefs.current
                .get(getRenderKey(lastLine))
                ?.scrollIntoView({ block: 'nearest' })
            }
            break
          }

          case 'o': {
            if (!event.shiftKey) {
              event.preventDefault()
              handleCreateBelow()
            }
            break
          }

          case 'O': {
            event.preventDefault()
            handleCreateAbove()
            break
          }

          case 'i': {
            event.preventDefault()
            handleStartEditAtStart()
            break
          }

          case 'a': {
            event.preventDefault()
            handleStartEditAtEnd()
            break
          }

          case 'r': {
            event.preventDefault()
            handleStartReplace()
            break
          }

          case ' ': {
            event.preventDefault()
            toggleExpanded(selectedIssueId)
            break
          }

          case 'Enter':
          case 'e': {
            event.preventDefault()
            onEditIssue?.(selectedIssueId)
            break
          }

          case 'Escape': {
            event.preventDefault()
            if (expandedIds.has(selectedIssueId)) {
              toggleExpanded(selectedIssueId)
            } else {
              onSelectIssue?.(null)
            }
            break
          }
        }
      },
      [
        editMode,
        selectedIssueId,
        navigableLines,
        issueRenderLines,
        onSelectIssue,
        onEditIssue,
        toggleExpanded,
        expandedIds,
        handleCancelEdit,
        handleCreateBelow,
        handleCreateAbove,
        handleStartEditAtStart,
        handleStartEditAtEnd,
        handleStartReplace,
        moveOperation,
        onMoveCancel,
      ]
    )

    // ============================================================================
    // Type and Status Change Handlers
    // ============================================================================

    const handleTypeChange = useCallback(
      async (issueId: string, newType: IssueType) => {
        await updateIssue({
          issueId,
          data: { projectId, type: newType },
        })
      },
      [updateIssue, projectId]
    )

    const handleStatusChange = useCallback(
      async (issueId: string, newStatus: IssueStatus) => {
        await updateIssue({
          issueId,
          data: { projectId, status: newStatus },
        })
      },
      [updateIssue, projectId]
    )

    const handleExecutionModeChange = useCallback(
      async (issueId: string, newMode: ExecutionMode) => {
        await updateIssue({
          issueId,
          data: { projectId, executionMode: newMode },
        })
      },
      [updateIssue, projectId]
    )

    // ============================================================================
    // Inline Editor Row Rendering (pending-issue render line)
    // ============================================================================

    const renderPendingIssueRow = useCallback(
      (_lane: number) => {
        if (!pendingNewIssue) return null

        const svgWidth = LANE_WIDTH * maxLanes + 12

        return (
          <div
            key="pending-issue"
            data-testid="task-graph-inline-create-row"
            data-pending-editor=""
            className={cn(
              'flex items-center gap-2 transition-colors',
              'bg-muted ring-primary/50 ring-2'
            )}
            style={{ height: ROW_HEIGHT }}
          >
            {/* SVG placeholder for alignment */}
            <div style={{ width: svgWidth, flexShrink: 0 }} />

            {/* Type badge */}
            <span
              className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
              style={{
                backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
                color: getTypeColor(IssueType.TASK),
              }}
            >
              Task
            </span>

            {/* Inline editor */}
            <InlineIssueEditor
              title={pendingNewIssue.title}
              onTitleChange={handleTitleChange}
              onSave={handleSave}
              onSaveAndEdit={handleSaveAndEdit}
              onCancel={handleCancelEdit}
              onIndent={handleModeTransition('Tab')}
              onUnindent={handleModeTransition('ShiftTab')}
              placeholder="Enter new issue title..."
              cursorPosition={EditCursorPosition.Start}
              pendingMode={pendingNewIssue.mode}
            />
          </div>
        )
      },
      [
        pendingNewIssue,
        maxLanes,
        handleTitleChange,
        handleSave,
        handleSaveAndEdit,
        handleCancelEdit,
        handleModeTransition,
      ]
    )

    // ============================================================================
    // Rendering
    // ============================================================================

    // Render loading skeleton
    if (isLoading) {
      return (
        <div className={cn('space-y-1', className)} data-testid="task-graph-loading">
          {Array.from({ length: 5 }).map((_, i) => (
            <IssueRowSkeleton key={i} />
          ))}
        </div>
      )
    }

    // Render error state
    if (isError) {
      return (
        <ErrorFallback
          title="Failed to load issues"
          description="Unable to fetch the task graph. Please try again."
          variant="inline"
          onRetry={() => refetch()}
          className={className}
        />
      )
    }

    // Render empty state
    if (renderLines.length === 0) {
      return (
        <IssuesEmptyState
          onCreateIssue={() => createIssue({ title: 'New issue' })}
          isCreating={isCreating}
          className={className}
        />
      )
    }

    // Render task graph
    return (
      <div
        ref={containerRef}
        role="grid"
        tabIndex={0}
        aria-label="Task graph"
        aria-rowcount={renderLines.length}
        data-testid="task-graph"
        className={cn(
          'scrollbar-thin scrollbar-track-transparent scrollbar-thumb-muted overflow-x-auto focus-visible:outline-none',
          className
        )}
        onKeyDown={handleKeyDown}
      >
        {/* Relative container so TaskGraphEdges can be absolutely positioned */}
        <div style={{ position: 'relative' }}>
          <TaskGraphEdges
            edges={edges}
            renderLines={renderLines}
            expandedIds={expandedIds}
            expandedPanelHeights={expandedPanelHeights}
            maxLanes={maxLanes}
          />
          {renderLines.map((line, index) => {
            // Pending-issue synthetic node: render the inline editor at the
            // engine-assigned position.
            if (isPendingIssueRenderLine(line)) {
              return renderPendingIssueRow(line.lane)
            }

            if (isIssueRenderLine(line)) {
              const isSelected = selectedIssueId === line.issueId
              const isExpanded = expandedIds.has(line.issueId)
              const isEditing =
                editMode === KeyboardEditMode.EditingExisting &&
                pendingEdit?.issueId === line.issueId

              const renderKey = getRenderKey(line)

              return (
                <div key={renderKey}>
                  {/* (inline editor is now rendered as a pending-issue render line) */}

                  {/* Issue row (or inline edit if editing existing) */}
                  {isEditing && pendingEdit ? (
                    <div
                      data-testid="task-graph-issue-row"
                      data-issue-id={line.issueId}
                      className={cn(
                        'flex items-center gap-2 transition-colors',
                        'bg-muted ring-primary/50 ring-2'
                      )}
                      style={{ height: ROW_HEIGHT }}
                    >
                      {/* SVG placeholder for alignment */}
                      <div style={{ width: LANE_WIDTH * maxLanes + 12, flexShrink: 0 }} />

                      {/* Type badge */}
                      <span
                        className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
                        style={{
                          backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
                          color: getTypeColor(IssueType.TASK),
                        }}
                      >
                        Task
                      </span>

                      {/* ID */}
                      <span className="text-muted-foreground shrink-0 font-mono text-xs">
                        {line.issueId.substring(0, 6)}
                      </span>

                      {/* Inline editor */}
                      <InlineIssueEditor
                        title={pendingEdit.title}
                        onTitleChange={handleTitleChange}
                        onSave={async () => {
                          if (!pendingEdit.title.trim()) {
                            handleCancelEdit()
                            return
                          }
                          // Only save if title changed
                          if (pendingEdit.title.trim() !== pendingEdit.originalTitle) {
                            try {
                              await updateIssue({
                                issueId: line.issueId,
                                data: { projectId, title: pendingEdit.title.trim() },
                              })
                            } catch {
                              // Keep edit mode on error
                            }
                          } else {
                            handleCancelEdit()
                          }
                          containerRef.current?.focus()
                        }}
                        onSaveAndEdit={async () => {
                          if (!pendingEdit.title.trim()) {
                            handleCancelEdit()
                            return
                          }
                          // Save title if changed, then navigate to edit
                          if (pendingEdit.title.trim() !== pendingEdit.originalTitle) {
                            try {
                              await updateIssue({
                                issueId: line.issueId,
                                data: { projectId, title: pendingEdit.title.trim() },
                              })
                              onEditIssue?.(line.issueId)
                            } catch {
                              // Keep edit mode on error
                            }
                          } else {
                            onEditIssue?.(line.issueId)
                          }
                        }}
                        onCancel={handleCancelEdit}
                        onIndent={() => {}}
                        onUnindent={() => {}}
                        placeholder="Enter issue title..."
                        cursorPosition={pendingEdit.cursorPosition}
                      />
                    </div>
                  ) : (
                    <TaskGraphIssueRow
                      ref={(el) => {
                        if (el) {
                          scrollAnchorRefs.current.set(renderKey, el)
                        } else {
                          scrollAnchorRefs.current.delete(renderKey)
                        }
                      }}
                      line={line}
                      maxLanes={maxLanes}
                      projectId={projectId}
                      isSelected={isSelected}
                      isExpanded={isExpanded}
                      searchQuery={searchQuery}
                      onToggleExpand={() => toggleExpanded(line.issueId)}
                      onEdit={onEditIssue}
                      onRunAgent={onRunAgent}
                      onOpenSession={onOpenSession}
                      onClick={() => handleRowClick(line.issueId)}
                      onTypeChange={handleTypeChange}
                      onStatusChange={handleStatusChange}
                      onExecutionModeChange={handleExecutionModeChange}
                      onSelectFirstInstance={handleSelectFirstInstance}
                      openSpecState={openSpecStatesHook.openSpecStates?.[line.issueId] ?? null}
                      isMoveSource={moveSourceIssueId === line.issueId}
                      isMoveOperationActive={!!moveOperation}
                      aria-rowindex={index + 1}
                      data-testid="task-graph-issue-row"
                      data-issue-id={line.issueId}
                    />
                  )}

                  {/* Expanded details */}
                  {isExpanded && !isEditing && (
                    <TaskGraphExpandedDetails
                      line={line}
                      maxLanes={maxLanes}
                      onEdit={onEditIssue}
                      onRunAgent={onRunAgent}
                      onOpenSession={onOpenSession}
                      onClose={() => toggleExpanded(line.issueId)}
                      onHeightChange={handlePanelHeightChange}
                    />
                  )}
                </div>
              )
            }

            return null
          })}
        </div>

        {/* Deduped orphan changes across main + every branch. */}
        <OrphanedChangesList
          projectId={projectId}
          entries={aggregateOrphansFromInputs({
            orphanChanges: orphanChangesHook.orphanChanges,
            openSpecStates: openSpecStatesHook.openSpecStates,
            issues,
          })}
          issues={issueRenderLines}
          openSpecStates={openSpecStatesHook.openSpecStates}
        />

        {/* Cycle banner — degraded layout fallback. */}
        {layoutCycle && (
          <div
            role="alert"
            data-testid="task-graph-cycle-banner"
            className="border-destructive/40 bg-destructive/10 text-destructive mt-4 rounded border p-3 text-sm"
          >
            Issue graph contains a cycle ({layoutCycle.join(' → ')}). Showing a flat list until the
            cycle is resolved.
          </div>
        )}

        {/* Search match count indicator (hidden, for accessibility) */}
        {searchQuery && (
          <div className="sr-only" role="status" aria-live="polite">
            {searchMatchCount} issues match "{searchQuery}"
          </div>
        )}
      </div>
    )
  })
)

export { TaskGraphView as default }
