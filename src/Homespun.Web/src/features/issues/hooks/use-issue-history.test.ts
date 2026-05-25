import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useIssueHistory, issueHistoryStateQueryKey } from './use-issue-history'
import { Issues } from '@/api'

vi.mock('@/api', () => ({
  Issues: {
    getApiProjectsByProjectIdIssuesHistoryState: vi.fn(),
    postApiProjectsByProjectIdIssuesHistoryUndo: vi.fn(),
    postApiProjectsByProjectIdIssuesHistoryRedo: vi.fn(),
  },
}))

describe('useIssueHistory', () => {
  let queryClient: QueryClient

  const wrapper = () =>
    function Wrapper({ children }: { children: ReactNode }) {
      return createElement(QueryClientProvider, { client: queryClient }, children)
    }

  beforeEach(() => {
    vi.clearAllMocks()
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
  })

  it('reports an empty stack until the query resolves', () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockReturnValue(
      new Promise(() => {}) as never // never resolves — keep query in pending state
    )

    const { result } = renderHook(() => useIssueHistory('proj-1'), {
      wrapper: wrapper(),
    })

    expect(result.current.canUndo).toBe(false)
    expect(result.current.canRedo).toBe(false)
    expect(result.current.undoCount).toBe(0)
    expect(result.current.redoCount).toBe(0)
  })

  it('derives canUndo/canRedo from the server state response', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: { canUndo: true, canRedo: false, undoCount: 3, redoCount: 0 },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueHistory('proj-1'), {
      wrapper: wrapper(),
    })

    await waitFor(() => expect(result.current.canUndo).toBe(true))
    expect(result.current.undoCount).toBe(3)
    expect(result.current.canRedo).toBe(false)
  })

  it('does not fetch state when projectId is undefined', () => {
    renderHook(() => useIssueHistory(undefined), { wrapper: wrapper() })
    expect(vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState)).not.toHaveBeenCalled()
  })

  it('calls the undo endpoint and invalidates the history-state + issues caches on success', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: { canUndo: true, canRedo: false, undoCount: 1, redoCount: 0 },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })
    vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryUndo).mockResolvedValue({
      data: {
        success: true,
        state: { canUndo: false, canRedo: true, undoCount: 0, redoCount: 1 },
      },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    const { result } = renderHook(() => useIssueHistory('proj-1'), {
      wrapper: wrapper(),
    })

    await waitFor(() => expect(result.current.canUndo).toBe(true))

    await result.current.undo()

    expect(vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryUndo)).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })

    // History state query and issues queries should be invalidated.
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: issueHistoryStateQueryKey('proj-1') })
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['issues', 'proj-1'] })
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['issues'] })
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['taskGraph'] })
  })

  it('calls the redo endpoint when redo() is invoked', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: { canUndo: false, canRedo: true, undoCount: 0, redoCount: 1 },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })
    vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryRedo).mockResolvedValue({
      data: {
        success: true,
        state: { canUndo: true, canRedo: false, undoCount: 1, redoCount: 0 },
      },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueHistory('proj-1'), {
      wrapper: wrapper(),
    })

    await waitFor(() => expect(result.current.canRedo).toBe(true))

    await result.current.redo()

    expect(vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryRedo)).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })
  })

  it('surfaces server errors as a thrown mutation error', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: { canUndo: true, canRedo: false, undoCount: 1, redoCount: 0 },
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })
    vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryUndo).mockResolvedValue({
      error: { detail: 'boom' } as never,
      request: {} as Request,
      response: {} as Response,
    } as never)

    const { result } = renderHook(() => useIssueHistory('proj-1'), {
      wrapper: wrapper(),
    })

    await waitFor(() => expect(result.current.canUndo).toBe(true))
    await expect(result.current.undo()).rejects.toThrow('Failed to undo')
  })
})
