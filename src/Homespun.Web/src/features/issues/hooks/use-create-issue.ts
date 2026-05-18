/**
 * useCreateIssue - Mutation hook for creating new issues.
 *
 * Supports creating issues as siblings, children (via parentIssueId),
 * or parents (via childIssueId) of existing issues.
 *
 * Features optimistic updates for instant UI feedback.
 */

import { useState, useCallback, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Issues, IssueStatus, IssueType, ExecutionMode } from '@/api'
import type { IssueResponse, TaskGraphResponse, TaskGraphNodeResponse } from '@/api'
import { taskGraphQueryKey } from './use-task-graph'

export interface UseCreateIssueOptions {
  /** The project ID to create issues in */
  projectId: string
  /** Callback fired when an issue is successfully created */
  onSuccess?: (issue: IssueResponse) => void
  /** Callback fired when an error occurs */
  onError?: (error: Error) => void
  /** Enable optimistic updates (default: true) */
  optimistic?: boolean
}

export interface CreateIssueParams {
  /** The title for the new issue */
  title: string
  /** Issue type (0=Task, 1=Bug, 2=Feature, 3=Chore) */
  type?: IssueType
  /** Set this to make the new issue a child of the specified parent */
  parentIssueId?: string
  /** Set this to make the new issue a parent of the specified child */
  childIssueId?: string
  /** Sibling issue ID for positioning within parent's children */
  siblingIssueId?: string
  /** If true, insert before the sibling; if false, insert after */
  insertBefore?: boolean
}

export interface UseCreateIssueReturn {
  /** Creates a new issue with the given parameters */
  createIssue: (params: CreateIssueParams) => Promise<IssueResponse>
  /** Whether a creation is in progress */
  isCreating: boolean
  /** The error that occurred during the last mutation, if any */
  error: Error | null
}

/**
 * Generate a temporary ID for optimistic updates.
 */
function generateTempId(): string {
  return `temp-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

/**
 * Hook for creating new issues with parent/child relationships.
 * Supports optimistic updates for instant UI feedback.
 */
export function useCreateIssue(options: UseCreateIssueOptions): UseCreateIssueReturn {
  const { projectId, onSuccess, onError, optimistic = true } = options
  const queryClient = useQueryClient()
  const [isCreating, setIsCreating] = useState(false)
  const [error, setError] = useState<Error | null>(null)
  const rollbackRef = useRef<(() => void) | null>(null)

  const createIssue = useCallback(
    async (params: CreateIssueParams): Promise<IssueResponse> => {
      const {
        title,
        type = IssueType.TASK,
        parentIssueId,
        childIssueId,
        siblingIssueId,
        insertBefore,
      } = params

      setIsCreating(true)
      setError(null)

      // Generate temp ID for optimistic update
      const tempId = generateTempId()

      // Optimistic update - add placeholder issue to cache
      if (optimistic) {
        const queryKey = taskGraphQueryKey(projectId)
        const previousData = queryClient.getQueryData<TaskGraphResponse>(queryKey)

        if (previousData) {
          // Create optimistic issue
          const optimisticIssue: IssueResponse = {
            id: tempId,
            title,
            description: null,
            status: IssueStatus.OPEN,
            type,
            priority: null,
            linkedPRs: [],
            linkedIssues: null,
            parentIssues: parentIssueId ? [{ parentIssue: parentIssueId, sortOrder: 'nnn' }] : null,
            tags: null,
            workingBranchId: null,
            executionMode: ExecutionMode.SERIES,
            createdBy: null,
            assignedTo: null,
            lastUpdate: new Date().toISOString(),
            createdAt: new Date().toISOString(),
          }

          // Create optimistic node
          const optimisticNode: TaskGraphNodeResponse = {
            issue: optimisticIssue,
            lane: 0, // Default lane, will be corrected on server response
            row: previousData.nodes?.length ?? 0,
            isActionable: true,
          }

          // Update cache optimistically
          queryClient.setQueryData<TaskGraphResponse>(queryKey, {
            ...previousData,
            nodes: [...(previousData.nodes ?? []), optimisticNode],
          })

          // Store rollback function
          rollbackRef.current = () => {
            queryClient.setQueryData<TaskGraphResponse>(queryKey, previousData)
          }
        }
      }

      try {
        const response = await Issues.postApiIssues({
          body: {
            projectId,
            title,
            type,
            parentIssueId,
            childIssueId,
            siblingIssueId,
            insertBefore,
          },
        })

        if (response.error || !response.data) {
          throw new Error(response.error?.detail ?? 'Failed to create issue')
        }

        const issue = response.data as IssueResponse

        // Clear rollback ref on success
        rollbackRef.current = null

        // The POST fetch is auto-instrumented; a dedicated trackEvent would
        // duplicate the span without adding context the server doesn't
        // already have.

        // Invalidate every cached query that backs the task graph view —
        // both the legacy server-laid-out task graph (for the static diff
        // view) and every per-resource key fed by the new client-side
        // pipeline (`useIssues`, `useLinkedPrs`, …). Prefix-invalidation
        // catches all variants of the issues query.
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) }),
          queryClient.invalidateQueries({ queryKey: ['issues', projectId] }),
          queryClient.invalidateQueries({ queryKey: ['linked-prs', projectId] }),
          queryClient.invalidateQueries({ queryKey: ['agent-statuses', projectId] }),
          queryClient.invalidateQueries({ queryKey: ['openspec-states', projectId] }),
        ])

        onSuccess?.(issue)
        return issue
      } catch (err) {
        const error = err instanceof Error ? err : new Error('Failed to create issue')
        setError(error)

        // Rollback optimistic update on error
        if (rollbackRef.current) {
          rollbackRef.current()
          rollbackRef.current = null
        }

        onError?.(error)
        throw error
      } finally {
        setIsCreating(false)
      }
    },
    [projectId, queryClient, onSuccess, onError, optimistic]
  )

  return {
    createIssue,
    isCreating,
    error,
  }
}

export default useCreateIssue
