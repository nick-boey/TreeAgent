import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Issues, type IssueHistoryOperationResponse, type IssueHistoryState } from '@/api'

export const issueHistoryStateQueryKey = (projectId: string) =>
  ['issue-history-state', projectId] as const

const EMPTY_STATE: IssueHistoryState = {
  canUndo: false,
  canRedo: false,
  undoCount: 0,
  redoCount: 0,
}

/**
 * Subscribes to the per-project undo/redo stack pointers and exposes
 * mutations for the undo/redo buttons + keyboard shortcuts.
 *
 * The stack is server-side and in-memory (cleared on server restart). The
 * state query is `staleTime: 0` and refetches on window focus so multi-tab
 * clients converge after a remote mutation.
 *
 * On a successful undo/redo, every issue-related query is invalidated —
 * the server broadcasts a bulk `IssueChanged` over SignalR for the same
 * reason, so both paths invalidate symmetrically.
 */
export function useIssueHistory(projectId: string | undefined) {
  const queryClient = useQueryClient()

  const stateQuery = useQuery({
    queryKey: issueHistoryStateQueryKey(projectId ?? ''),
    queryFn: async (): Promise<IssueHistoryState> => {
      if (!projectId) return EMPTY_STATE
      const response = await Issues.getApiProjectsByProjectIdIssuesHistoryState({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to fetch issue history state')
      }
      return response.data as IssueHistoryState
    },
    enabled: !!projectId,
    staleTime: 0,
    refetchOnWindowFocus: true,
  })

  const invalidateAllIssueQueries = () => {
    if (!projectId) return
    queryClient.invalidateQueries({ queryKey: issueHistoryStateQueryKey(projectId) })
    queryClient.invalidateQueries({ queryKey: ['issues', projectId] })
    queryClient.invalidateQueries({ queryKey: ['issues'] })
    queryClient.invalidateQueries({ queryKey: ['taskGraph'] })
    queryClient.invalidateQueries({ queryKey: ['linked-prs'] })
    queryClient.invalidateQueries({ queryKey: ['agent-statuses'] })
    queryClient.invalidateQueries({ queryKey: ['openspec-states'] })
    queryClient.invalidateQueries({ queryKey: ['orphan-changes'] })
  }

  const undoMutation = useMutation({
    mutationFn: async (): Promise<IssueHistoryOperationResponse> => {
      if (!projectId) throw new Error('projectId required for undo')
      const response = await Issues.postApiProjectsByProjectIdIssuesHistoryUndo({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to undo')
      }
      return response.data as IssueHistoryOperationResponse
    },
    onSuccess: () => {
      invalidateAllIssueQueries()
    },
  })

  const redoMutation = useMutation({
    mutationFn: async (): Promise<IssueHistoryOperationResponse> => {
      if (!projectId) throw new Error('projectId required for redo')
      const response = await Issues.postApiProjectsByProjectIdIssuesHistoryRedo({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error('Failed to redo')
      }
      return response.data as IssueHistoryOperationResponse
    },
    onSuccess: () => {
      invalidateAllIssueQueries()
    },
  })

  const state = stateQuery.data ?? EMPTY_STATE

  return {
    canUndo: state.canUndo,
    canRedo: state.canRedo,
    undoCount: state.undoCount,
    redoCount: state.redoCount,
    isLoading: stateQuery.isLoading,
    undo: () => undoMutation.mutateAsync(),
    redo: () => redoMutation.mutateAsync(),
    isUndoing: undoMutation.isPending,
    isRedoing: redoMutation.isPending,
  }
}
