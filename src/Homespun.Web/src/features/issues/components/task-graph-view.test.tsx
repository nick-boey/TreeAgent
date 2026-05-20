import { describe, it, expect, vi } from 'vitest'
import React from 'react'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IssueStatus, IssueType, ExecutionMode } from '@/api'
import { TaskGraphView } from './task-graph-view'

// ── Service mock ──────────────────────────────────────────────────────────────
// Return a minimal non-empty layout so the component reaches the
// OrphanedChangesList section rather than bailing out at the empty-state guard.
vi.mock('../services', async (importOriginal) => {
  const original = await importOriginal<typeof import('../services')>()
  return {
    ...original,
    computeLayoutFromIssues: vi.fn(() => ({
      lines: [
        {
          type: 'issue' as const,
          issueId: 'issue-1',
          title: 'Test Issue',
          description: null,
          branchName: null,
          lane: 0,
          marker: 'open',
          parentLane: null,
          isFirstChild: true,
          isSeriesChild: false,
          drawTopLine: false,
          drawBottomLine: false,
          seriesConnectorFromLane: null,
          issueType: IssueType.TASK,
          status: IssueStatus.OPEN,
          hasDescription: false,
          linkedPr: null,
          agentStatus: null,
          assignedTo: null,
          drawLane0Connector: false,
          isLastLane0Connector: false,
          drawLane0PassThrough: false,
          lane0Color: null,
          hasHiddenParent: false,
          hiddenParentIsSeriesMode: false,
          executionMode: ExecutionMode.SERIES,
          parentIssues: null,
          multiParentIndex: null,
          multiParentTotal: null,
          isLastChild: true,
          hasParallelChildren: false,
          parentIssueId: null,
          parentLaneReservations: [],
        },
      ],
      edges: [],
    })),
  }
})

// ── Hook mocks ────────────────────────────────────────────────────────────────
vi.mock('../hooks', () => ({
  useIssues: () => ({
    issues: [
      {
        id: 'issue-1',
        title: 'Test Issue',
        status: IssueStatus.OPEN,
        type: IssueType.TASK,
        executionMode: ExecutionMode.SERIES,
        description: null,
        parentIssues: null,
        assignedTo: null,
        priority: null,
        workingBranchId: null,
      },
    ],
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useLinkedPrs: () => ({
    linkedPrs: {},
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }),
  useAgentStatuses: () => ({
    agentStatuses: {},
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }),
  useOpenSpecStates: () => ({
    openSpecStates: {},
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }),
  useCreateIssue: () => ({ createIssue: vi.fn(), isCreating: false }),
  useUpdateIssue: () => ({ mutateAsync: vi.fn() }),
}))

// ── Wrapper ───────────────────────────────────────────────────────────────────
function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return React.createElement(QueryClientProvider, { client: queryClient }, children)
}

// ── Tests ─────────────────────────────────────────────────────────────────────
describe('TaskGraphView', () => {
  it('never renders orphaned-changes-section even when orphan data is present', () => {
    render(React.createElement(TaskGraphView, { projectId: 'project-1' }), { wrapper })

    // The orphaned-changes-section must not appear; orphan-link UI is fully removed.
    expect(screen.queryByTestId('orphaned-changes-section')).not.toBeInTheDocument()
  })
})
