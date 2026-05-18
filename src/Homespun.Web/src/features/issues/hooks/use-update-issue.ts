import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Issues, type IssueResponse, type UpdateIssueRequest, type TaskGraphResponse } from '@/api'
import { issueQueryKey } from './use-issue'
import { taskGraphQueryKey } from './use-task-graph'

export interface UseUpdateIssueOptions {
  onSuccess?: (data: IssueResponse) => void
  onError?: (error: Error, variables: UpdateIssueParams, context: unknown) => void
  /** Enable optimistic updates (default: true) */
  optimistic?: boolean
}

export interface UpdateIssueParams {
  issueId: string
  data: UpdateIssueRequest
}

interface MutationContext {
  previousIssue?: IssueResponse
  previousTaskGraph?: TaskGraphResponse
  projectId?: string
}

/**
 * Hook for updating an issue with optimistic updates.
 *
 * Features:
 * - Instant UI feedback through optimistic updates
 * - Automatic rollback on error
 * - Cache invalidation on success
 *
 * @param options - Optional callbacks and configuration
 * @returns Mutation object for updating issues
 */
export function useUpdateIssue(options?: UseUpdateIssueOptions) {
  const { onSuccess, onError, optimistic = true } = options ?? {}
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ issueId, data }: UpdateIssueParams) => {
      const response = await Issues.putApiIssuesByIssueId({
        path: { issueId },
        body: data,
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to update issue')
      }

      return response.data
    },
    onMutate: async ({ issueId, data }): Promise<MutationContext> => {
      if (!optimistic) return {}

      const projectId = data.projectId
      if (!projectId) return {}

      // Cancel any outgoing refetches to avoid overwriting optimistic update
      await queryClient.cancelQueries({ queryKey: issueQueryKey(projectId, issueId) })
      await queryClient.cancelQueries({ queryKey: taskGraphQueryKey(projectId) })

      // Snapshot previous values
      const previousIssue = queryClient.getQueryData<IssueResponse>(
        issueQueryKey(projectId, issueId)
      )
      const previousTaskGraph = queryClient.getQueryData<TaskGraphResponse>(
        taskGraphQueryKey(projectId)
      )

      // Optimistically update the issue cache
      if (previousIssue) {
        queryClient.setQueryData<IssueResponse>(issueQueryKey(projectId, issueId), {
          ...previousIssue,
          ...data,
          lastUpdate: new Date().toISOString(),
        })
      }

      // Optimistically update the task graph cache
      if (previousTaskGraph?.nodes) {
        queryClient.setQueryData<TaskGraphResponse>(taskGraphQueryKey(projectId), {
          ...previousTaskGraph,
          nodes: previousTaskGraph.nodes.map((node) => {
            if (node.issue?.id === issueId) {
              return {
                ...node,
                issue: {
                  ...node.issue,
                  ...data,
                  lastUpdate: new Date().toISOString(),
                },
              }
            }
            return node
          }),
        })
      }

      // Return context with previous values for rollback
      return { previousIssue, previousTaskGraph, projectId }
    },
    onError: (error, variables, context) => {
      // Rollback on error
      const ctx = context as MutationContext | undefined
      if (ctx?.projectId) {
        if (ctx.previousIssue) {
          queryClient.setQueryData<IssueResponse>(
            issueQueryKey(ctx.projectId, variables.issueId),
            ctx.previousIssue
          )
        }
        if (ctx.previousTaskGraph) {
          queryClient.setQueryData<TaskGraphResponse>(
            taskGraphQueryKey(ctx.projectId),
            ctx.previousTaskGraph
          )
        }
      }
      onError?.(error as Error, variables, context)
    },
    onSuccess: (data) => {
      // Invalidate caches to sync with server state
      if (data.id) {
        const projectId = data.id // Get from response if needed
        queryClient.invalidateQueries({
          queryKey: issueQueryKey(projectId, data.id),
        })
      }
      // Invalidate every cached query the task graph view depends on:
      // legacy `taskGraph` plus the per-resource keys driving the new
      // client-side layout pipeline.
      queryClient.invalidateQueries({ queryKey: ['taskGraph'] })
      queryClient.invalidateQueries({ queryKey: ['issues'] })
      queryClient.invalidateQueries({ queryKey: ['linked-prs'] })
      queryClient.invalidateQueries({ queryKey: ['agent-statuses'] })
      queryClient.invalidateQueries({ queryKey: ['openspec-states'] })
      onSuccess?.(data as IssueResponse)
    },
    onSettled: () => {
      // Always refetch after mutation settles — both pipelines.
      queryClient.invalidateQueries({ queryKey: ['taskGraph'] })
      queryClient.invalidateQueries({ queryKey: ['issues'] })
    },
  })
}
