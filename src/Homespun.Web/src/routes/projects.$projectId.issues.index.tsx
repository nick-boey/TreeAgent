import { useState, useCallback, useRef, useMemo } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  TaskGraphView,
  ProjectToolbar,
  useToolbarShortcuts,
  useIssues,
  taskGraphQueryKey,
  useDefaultFilter,
  type TaskGraphViewRef,
} from '@/features/issues'
import { MoveOperationType } from '@/features/issues/types'
import { MoveDirection } from '@/api/generated/types.gen'
import { useAppStore } from '@/stores/app-store'
import { parseFilterQuery, type ParsedFilter } from '@/features/issues/services'
import { RunAgentDialog } from '@/features/agents'
import { AssignIssueDialog } from '@/features/issues/components/assign-issue-popover'
import { Issues } from '@/api'

export const Route = createFileRoute('/projects/$projectId/issues/')({
  component: IssuesList,
})

function IssuesList() {
  const { projectId } = Route.useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // Get default filter configuration
  const { defaultFilterQuery, userEmail } = useDefaultFilter()

  // View mode from app store
  const { issuesViewMode, setIssuesViewMode } = useAppStore()

  // Ref to TaskGraphView for imperative actions
  const taskGraphRef = useRef<TaskGraphViewRef>(null)

  // Ref to filter input for focus management
  const filterInputRef = useRef<HTMLInputElement>(null)

  // State
  const [selectedIssueId, setSelectedIssueId] = useState<string | null>(null)
  const [depth, setDepth] = useState(3)
  const [searchQuery, setSearchQuery] = useState('')

  // Compute search match count from rendered issues
  const [searchMatchCount] = useState(0)

  // Filter state - default to no filter on page load
  const [filterActive, setFilterActive] = useState(false)
  const [filterQuery, setFilterQuery] = useState('')
  const [appliedFilterQuery, setAppliedFilterQuery] = useState('')

  // Parse the applied filter query and resolve "me" keyword
  const appliedFilter: ParsedFilter | null = useMemo(() => {
    if (!appliedFilterQuery.trim()) return null
    const filter = parseFilterQuery(appliedFilterQuery)
    // Resolve the "me" keyword with the current user's email
    if (filter.assigneeMe && userEmail) {
      filter.resolvedMeEmail = userEmail
    }
    return filter
  }, [appliedFilterQuery, userEmail])

  // Filter match count (will be updated by TaskGraphView)
  const [filterMatchCount, setFilterMatchCount] = useState(0)

  // Run agent dialog state (consolidates agent launcher + issues agent)
  const [runAgentOpen, setRunAgentOpen] = useState(false)
  const [runAgentIssueId, setRunAgentIssueId] = useState<string | null>(null)

  // Assign issue popover state
  const [assignPopoverOpen, setAssignPopoverOpen] = useState(false)

  // Move operation state
  const [moveOperation, setMoveOperation] = useState<MoveOperationType | null>(null)
  const [moveSourceIssueId, setMoveSourceIssueId] = useState<string | null>(null)

  // Visible-set issues — used to compute sibling-position metadata for the
  // "Move up / down" toolbar affordances.
  const { issues } = useIssues(projectId, { includeOpenPrLinked: true })

  // Compute canMoveUp/canMoveDown based on selected issue's position among siblings
  const { canMoveUp, canMoveDown } = useMemo(() => {
    if (!selectedIssueId || !issues) return { canMoveUp: false, canMoveDown: false }

    const selected = issues.find((i) => i.id === selectedIssueId)
    if (!selected?.parentIssues?.length) return { canMoveUp: false, canMoveDown: false }
    if (selected.parentIssues.length > 1) return { canMoveUp: false, canMoveDown: false }

    const parentId = selected.parentIssues[0].parentIssue
    if (!parentId) return { canMoveUp: false, canMoveDown: false }

    // Find all siblings under the same parent.
    const siblings = issues
      .filter((i) => i.parentIssues?.some((p) => p.parentIssue === parentId))
      .sort((a, b) => {
        const aOrder = a.parentIssues?.find((p) => p.parentIssue === parentId)?.sortOrder ?? ''
        const bOrder = b.parentIssues?.find((p) => p.parentIssue === parentId)?.sortOrder ?? ''
        return aOrder.localeCompare(bOrder)
      })

    const index = siblings.findIndex((i) => i.id === selectedIssueId)
    if (index < 0) return { canMoveUp: false, canMoveDown: false }

    return {
      canMoveUp: index > 0,
      canMoveDown: index < siblings.length - 1,
    }
  }, [selectedIssueId, issues])

  // Move sibling mutation
  const moveSiblingMutation = useMutation({
    mutationFn: ({ issueId, direction }: { issueId: string; direction: MoveDirection }) =>
      Issues.postApiIssuesByIssueIdMoveSibling({
        path: { issueId },
        body: { projectId, direction },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
    },
  })

  // Remove parent mutation
  const removeParentMutation = useMutation({
    mutationFn: ({ childId, parentIssueId }: { childId: string; parentIssueId: string }) =>
      Issues.postApiIssuesByChildIdRemoveParent({
        path: { childId },
        body: { projectId, parentIssueId },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
    },
  })

  // Remove all parents mutation
  const removeAllParentsMutation = useMutation({
    mutationFn: ({ issueId }: { issueId: string }) =>
      Issues.postApiIssuesByIssueIdRemoveAllParents({
        path: { issueId },
        body: { projectId },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
    },
  })

  // Set parent mutation for move operations
  const setParentMutation = useMutation({
    mutationFn: ({ childId, parentIssueId }: { childId: string; parentIssueId: string | null }) =>
      Issues.postApiIssuesByChildIdSetParent({
        path: { childId },
        body: { projectId, parentIssueId },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
    },
  })

  // Handlers
  const handleEditIssue = useCallback(
    (issueId?: string) => {
      const id = issueId ?? selectedIssueId
      if (id) {
        navigate({
          to: '/projects/$projectId/issues/$issueId/edit',
          params: { projectId, issueId: id },
        })
      }
    },
    [navigate, projectId, selectedIssueId]
  )

  const handleCreateAbove = useCallback(() => {
    taskGraphRef.current?.createAbove()
  }, [])

  const handleCreateBelow = useCallback(() => {
    taskGraphRef.current?.createBelow()
  }, [])

  const handleMoveUp = useCallback(() => {
    if (selectedIssueId && canMoveUp) {
      moveSiblingMutation.mutate({ issueId: selectedIssueId, direction: MoveDirection.UP })
    }
  }, [selectedIssueId, canMoveUp, moveSiblingMutation])

  const handleMoveDown = useCallback(() => {
    if (selectedIssueId && canMoveDown) {
      moveSiblingMutation.mutate({ issueId: selectedIssueId, direction: MoveDirection.DOWN })
    }
  }, [selectedIssueId, canMoveDown, moveSiblingMutation])

  const handleMakeChild = useCallback(() => {
    if (!selectedIssueId) return
    // Toggle: if already in child mode, cancel; otherwise start child mode
    if (moveOperation === MoveOperationType.AsChildOf) {
      setMoveOperation(null)
      setMoveSourceIssueId(null)
    } else {
      setMoveOperation(MoveOperationType.AsChildOf)
      setMoveSourceIssueId(selectedIssueId)
    }
  }, [selectedIssueId, moveOperation])

  const handleMakeParent = useCallback(() => {
    if (!selectedIssueId) return
    // Toggle: if already in parent mode, cancel; otherwise start parent mode
    if (moveOperation === MoveOperationType.AsParentOf) {
      setMoveOperation(null)
      setMoveSourceIssueId(null)
    } else {
      setMoveOperation(MoveOperationType.AsParentOf)
      setMoveSourceIssueId(selectedIssueId)
    }
  }, [selectedIssueId, moveOperation])

  const handleRemoveParent = useCallback(() => {
    if (!selectedIssueId) return
    if (moveOperation === MoveOperationType.RemoveParent) {
      setMoveOperation(null)
      setMoveSourceIssueId(null)
    } else {
      setMoveOperation(MoveOperationType.RemoveParent)
      setMoveSourceIssueId(selectedIssueId)
    }
  }, [selectedIssueId, moveOperation])

  const handleRemoveAllParents = useCallback(() => {
    if (!selectedIssueId) return
    removeAllParentsMutation.mutate({ issueId: selectedIssueId })
  }, [selectedIssueId, removeAllParentsMutation])

  const handleMoveComplete = useCallback(
    async (targetIssueId: string) => {
      if (!moveSourceIssueId || !moveOperation) return

      try {
        if (moveOperation === MoveOperationType.AsChildOf) {
          // Make source a child of target
          await setParentMutation.mutateAsync({
            childId: moveSourceIssueId,
            parentIssueId: targetIssueId,
          })
        } else if (moveOperation === MoveOperationType.AsParentOf) {
          // Make target a child of source
          await setParentMutation.mutateAsync({
            childId: targetIssueId,
            parentIssueId: moveSourceIssueId,
          })
        } else if (moveOperation === MoveOperationType.RemoveParent) {
          // Remove target as parent of source (target must be a parent of source)
          await removeParentMutation.mutateAsync({
            childId: moveSourceIssueId,
            parentIssueId: targetIssueId,
          })
        }
      } finally {
        // Reset move operation state
        setMoveOperation(null)
        setMoveSourceIssueId(null)
      }
    },
    [moveSourceIssueId, moveOperation, setParentMutation, removeParentMutation]
  )

  const handleMoveCancel = useCallback(() => {
    setMoveOperation(null)
    setMoveSourceIssueId(null)
  }, [])

  const handleOpenAgentLauncher = useCallback(() => {
    if (selectedIssueId) {
      setRunAgentIssueId(selectedIssueId)
      setRunAgentOpen(true)
    }
  }, [selectedIssueId])

  const handleAssignIssue = useCallback(() => {
    if (selectedIssueId) {
      setAssignPopoverOpen(true)
    }
  }, [selectedIssueId])

  // Handler for running agent on a specific issue (from row actions)
  const handleRunAgent = useCallback((issueId: string) => {
    setRunAgentIssueId(issueId)
    setRunAgentOpen(true)
  }, [])

  // Handler for opening an existing session
  const handleOpenSession = useCallback(
    (sessionId: string) => {
      navigate({ to: '/sessions/$sessionId', params: { sessionId } })
    },
    [navigate]
  )

  const handleFocusSearch = useCallback(() => {
    // Focus the search input in toolbar
    const searchInput = document.querySelector<HTMLInputElement>('input[type="search"]')
    searchInput?.focus()
  }, [])

  const handleNextMatch = useCallback(() => {
    // TODO: Implement next match
    console.log('Next match')
  }, [])

  const handlePreviousMatch = useCallback(() => {
    // TODO: Implement previous match
    console.log('Previous match')
  }, [])

  const handleEmbedSearch = useCallback(() => {
    // TODO: Implement embed search
    console.log('Embed search')
  }, [])

  // Filter handlers
  const handleToggleFilter = useCallback(() => {
    setFilterActive((prev) => {
      const newState = !prev
      if (newState) {
        // Opening filter panel - focus the input after render
        setTimeout(() => {
          filterInputRef.current?.focus()
        }, 0)
      } else {
        // Closing filter panel - clear the filter
        setFilterQuery('')
        setAppliedFilterQuery('')
      }
      return newState
    })
  }, [])

  const handleFilterChange = useCallback((query: string) => {
    setFilterQuery(query)
  }, [])

  const handleApplyFilter = useCallback(() => {
    setAppliedFilterQuery(filterQuery)
  }, [filterQuery])

  const handleApplyDefaultFilter = useCallback(() => {
    // Toggle: if the default filter is already applied, clear it
    if (filterActive && appliedFilterQuery === defaultFilterQuery) {
      setFilterActive(false)
      setFilterQuery('')
      setAppliedFilterQuery('')
    } else {
      setFilterActive(true)
      setFilterQuery(defaultFilterQuery)
      setAppliedFilterQuery(defaultFilterQuery)
      setTimeout(() => filterInputRef.current?.focus(), 0)
    }
  }, [defaultFilterQuery, filterActive, appliedFilterQuery])

  // Focus filter input with cursor at end (for 'f' key when filter is already active)
  const handleFocusFilterAtEnd = useCallback(() => {
    if (filterInputRef.current) {
      filterInputRef.current.focus()
      const len = filterInputRef.current.value.length
      filterInputRef.current.setSelectionRange(len, len)
    }
  }, [])

  // Register keyboard shortcuts
  useToolbarShortcuts({
    onCreateAbove: handleCreateAbove,
    onCreateBelow: handleCreateBelow,
    onOpenAgentLauncher: handleOpenAgentLauncher,
    onDecreaseDepth: () => setDepth((d) => Math.max(1, d - 1)),
    onIncreaseDepth: () => setDepth((d) => d + 1),
    onFocusSearch: handleFocusSearch,
    onNextMatch: handleNextMatch,
    onPreviousMatch: handlePreviousMatch,
    onEmbedSearch: handleEmbedSearch,
    onMoveUp: handleMoveUp,
    onMoveDown: handleMoveDown,
    canMoveUp,
    canMoveDown,
    onToggleFilter: handleToggleFilter,
    isFilterActive: filterActive,
    onFocusFilterAtEnd: handleFocusFilterAtEnd,
  })

  const isDefaultFilterActive =
    filterActive && appliedFilterQuery === defaultFilterQuery && defaultFilterQuery !== ''

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {/* Toolbar */}
      <ProjectToolbar
        selectedIssueId={selectedIssueId}
        onCreateAbove={handleCreateAbove}
        onCreateBelow={handleCreateBelow}
        onMakeChild={handleMakeChild}
        onMakeParent={handleMakeParent}
        childOfActive={moveOperation === MoveOperationType.AsChildOf}
        parentOfActive={moveOperation === MoveOperationType.AsParentOf}
        onRemoveParent={handleRemoveParent}
        removeParentActive={moveOperation === MoveOperationType.RemoveParent}
        onRemoveAllParents={handleRemoveAllParents}
        onMoveUp={handleMoveUp}
        onMoveDown={handleMoveDown}
        canMoveUp={canMoveUp}
        canMoveDown={canMoveDown}
        onEditIssue={() => handleEditIssue()}
        onOpenAgentLauncher={handleOpenAgentLauncher}
        onAssignIssue={handleAssignIssue}
        depth={depth}
        onDepthChange={setDepth}
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        searchMatchCount={searchMatchCount}
        onNextMatch={handleNextMatch}
        onPreviousMatch={handlePreviousMatch}
        onEmbedSearch={handleEmbedSearch}
        filterActive={filterActive}
        filterQuery={filterQuery}
        onToggleFilter={handleToggleFilter}
        onFilterChange={handleFilterChange}
        onApplyFilter={handleApplyFilter}
        filterMatchCount={filterMatchCount}
        filterInputRef={filterInputRef}
        onApplyDefaultFilter={handleApplyDefaultFilter}
        defaultFilterActive={isDefaultFilterActive}
        viewMode={issuesViewMode}
        onViewModeChange={setIssuesViewMode}
      />

      {/* Task Graph View */}
      <div className="min-h-0 flex-1 overflow-auto p-4">
        <TaskGraphView
          ref={taskGraphRef}
          projectId={projectId}
          depth={depth}
          searchQuery={searchQuery}
          selectedIssueId={selectedIssueId}
          onSelectIssue={setSelectedIssueId}
          onEditIssue={handleEditIssue}
          onRunAgent={handleRunAgent}
          onOpenSession={handleOpenSession}
          moveOperation={moveOperation}
          moveSourceIssueId={moveSourceIssueId}
          onMoveComplete={handleMoveComplete}
          onMoveCancel={handleMoveCancel}
          appliedFilter={appliedFilter}
          onFilterMatchCountChange={setFilterMatchCount}
          viewMode={issuesViewMode}
        />
      </div>

      {/* Run Agent Dialog (Task Agent + Issues Agent) */}
      <RunAgentDialog
        open={runAgentOpen}
        onOpenChange={setRunAgentOpen}
        projectId={projectId}
        issueId={runAgentIssueId ?? undefined}
        selectedIssueId={selectedIssueId}
      />

      {/* Assign Issue Dialog */}
      {selectedIssueId && (
        <AssignIssueDialog
          open={assignPopoverOpen}
          onOpenChange={setAssignPopoverOpen}
          projectId={projectId}
          issueId={selectedIssueId}
        />
      )}
    </div>
  )
}
