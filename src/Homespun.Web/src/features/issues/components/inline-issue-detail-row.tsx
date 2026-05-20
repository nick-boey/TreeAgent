/**
 * InlineIssueDetailRow - Expanded inline details for an issue in the task graph.
 */

import { memo, useCallback, useState, useMemo, useEffect, useLayoutEffect, useRef } from 'react'
import { Copy, Pencil, Play, X, ExternalLink } from 'lucide-react'
import { cn } from '@/lib/utils'
import { IssueStatus, ExecutionMode } from '@/api'
import { ISSUE_STATUS_LABELS, ISSUE_STATUS_COLORS, ISSUE_TYPE_LABELS } from '@/lib/issue-constants'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import type { TaskGraphIssueRenderLine } from '../services'
import { getTypeColor } from './task-graph-svg'
import { useMobile } from '@/hooks'

/** PR status color variants */
const PR_STATUS_COLORS: Record<string, string> = {
  open: 'bg-green-500/20 text-green-700 dark:text-green-400',
  merged: 'bg-purple-500/20 text-purple-700 dark:text-purple-400',
  closed: 'bg-red-500/20 text-red-700 dark:text-red-400',
}

export interface InlineIssueDetailRowProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  /** Called when clicking the Open Session button to navigate to an existing session */
  onOpenSession?: (sessionId: string) => void
  onClose?: () => void
  /**
   * Called with this panel's measured `offsetHeight` synchronously after mount
   * (via `useLayoutEffect`) and on every subsequent resize (via `ResizeObserver`).
   * Called with `height === 0` on unmount so the parent can drop the entry.
   */
  onHeightChange?: (issueId: string, height: number) => void
}

/**
 * Inline expanded details panel for an issue.
 * Shows full issue metadata, description, branch info, and action buttons.
 */
export const InlineIssueDetailRow = memo(function InlineIssueDetailRow({
  line,
  maxLanes,
  onEdit,
  onRunAgent,
  onOpenSession,
  onClose,
  onHeightChange,
}: InlineIssueDetailRowProps) {
  const [copied, setCopied] = useState(false)
  const isMobile = useMobile()
  const containerRef = useRef<HTMLDivElement>(null)
  const issueId = line.issueId

  // Seed the height synchronously before first paint so edges crossing past
  // this newly-mounted panel use the correct cumulative offset on the same
  // paint that mounts it. The ResizeObserver below picks up subsequent
  // changes asynchronously.
  useLayoutEffect(() => {
    if (!onHeightChange) return
    const el = containerRef.current
    if (el) {
      onHeightChange(issueId, el.offsetHeight)
    }
    return () => {
      onHeightChange(issueId, 0)
    }
  }, [issueId, onHeightChange])

  useEffect(() => {
    if (!onHeightChange) return
    const el = containerRef.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const height = (entry.target as HTMLElement).offsetHeight
        onHeightChange(issueId, height)
      }
    })
    observer.observe(el)
    return () => {
      observer.disconnect()
    }
  }, [issueId, onHeightChange])

  // Calculate left padding to align with content (after SVG)
  // On mobile, use minimal padding for full-width display
  const svgWidth = isMobile ? 12 : 24 * Math.max(maxLanes, 1) + 12

  const typeColor = getTypeColor(line.issueType)

  const handleCopyBranch = useCallback(async () => {
    if (line.branchName) {
      await navigator.clipboard.writeText(line.branchName)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }, [line.branchName])

  const handleEdit = useCallback(() => {
    onEdit?.(line.issueId)
  }, [onEdit, line.issueId])

  const handleRunAgent = useCallback(() => {
    onRunAgent?.(line.issueId)
  }, [onRunAgent, line.issueId])

  const handleOpenSession = useCallback(() => {
    if (line.agentStatus?.sessionId) {
      onOpenSession?.(line.agentStatus.sessionId)
    }
  }, [onOpenSession, line.agentStatus])

  // Determine if an agent session is active on this issue
  const hasActiveSession = useMemo(
    () => line.agentStatus?.isActive && line.agentStatus.sessionId,
    [line.agentStatus?.isActive, line.agentStatus?.sessionId]
  )

  return (
    <div
      ref={containerRef}
      className={cn(
        'animate-expand bg-muted/30 border-muted overflow-hidden border-t px-3 py-4',
        'transition-all duration-200 ease-out'
      )}
      style={{ marginLeft: svgWidth }}
    >
      {/* Header with badges and close button */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        {/* Issue ID */}
        <span className="font-mono text-sm font-medium">{line.issueId}</span>

        {/* Type badge */}
        <span
          className="shrink-0 rounded px-1.5 py-0.5 text-xs font-medium"
          style={{
            backgroundColor: `${typeColor}20`,
            color: typeColor,
          }}
        >
          {ISSUE_TYPE_LABELS[line.issueType] ?? 'Task'}
        </span>

        {/* Status badge */}
        <span
          className={cn(
            'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
            ISSUE_STATUS_COLORS[line.status as IssueStatus] ?? ISSUE_STATUS_COLORS[IssueStatus.OPEN]
          )}
        >
          {ISSUE_STATUS_LABELS[line.status as IssueStatus] ?? 'Open'}
        </span>

        {/* Spacer */}
        <div className="flex-1" />

        {/* Close button - touch-friendly */}
        <Button
          variant="ghost"
          size="sm"
          className="h-10 w-10 p-0"
          onClick={onClose}
          aria-label="Close"
        >
          <X className="h-5 w-5" />
        </Button>
      </div>

      {/* Branch name with copy */}
      {line.branchName && (
        <div className="mb-3 flex items-center gap-2">
          <span className="text-muted-foreground text-xs">Branch:</span>
          <code className="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">
            {line.branchName}
          </code>
          <Button
            variant="ghost"
            size="sm"
            className="h-6 w-6 p-0"
            onClick={handleCopyBranch}
            aria-label="Copy branch name"
          >
            <Copy className="h-3 w-3" />
          </Button>
          {copied && <span className="text-muted-foreground text-xs">Copied!</span>}
        </div>
      )}

      {/* Linked PR */}
      {line.linkedPr && (
        <div className="mb-3 flex items-center gap-2">
          <span className="text-muted-foreground text-xs">Pull Request:</span>
          <a
            href={line.linkedPr.url ?? '#'}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary flex items-center gap-1 text-xs hover:underline"
          >
            #{line.linkedPr.number}
            <ExternalLink className="h-3 w-3" />
          </a>
          {line.linkedPr.status && (
            <span
              data-testid="pr-status-badge"
              className={cn(
                'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
                PR_STATUS_COLORS[line.linkedPr.status] ?? PR_STATUS_COLORS.open
              )}
            >
              {line.linkedPr.status}
            </span>
          )}
        </div>
      )}

      {/* Agent status */}
      {line.agentStatus?.isActive && (
        <div className="mb-3 flex items-center gap-2" data-testid="agent-status-indicator">
          <span className="text-muted-foreground text-xs">Agent:</span>
          <span className="relative flex h-2 w-2 shrink-0">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
          </span>
          <span className="text-xs text-green-600 dark:text-green-400">
            {line.agentStatus.status ?? 'running'}
          </span>
          {line.agentStatus.sessionId && (
            <a
              href={`/sessions/${line.agentStatus.sessionId}`}
              className="text-primary text-xs hover:underline"
              aria-label="View session"
            >
              View session
            </a>
          )}
        </div>
      )}

      {/* Execution mode */}
      <div className="mb-3 flex items-center gap-2">
        <span className="text-muted-foreground text-xs">Execution Mode:</span>
        <span className="text-xs font-medium" data-testid="execution-mode">
          {line.executionMode === ExecutionMode.PARALLEL ? 'Parallel' : 'Series'}
        </span>
      </div>

      {/* Parent issues */}
      {line.parentIssues && line.parentIssues.length > 0 && (
        <div className="mb-3 flex items-center gap-2" data-testid="parent-issues">
          <span className="text-muted-foreground text-xs">Parents:</span>
          <div className="flex flex-wrap gap-1">
            {line.parentIssues.map((p, i) => (
              <code key={i} className="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">
                {p.sortOrder ? `${p.parentIssue}:${p.sortOrder}` : p.parentIssue}
              </code>
            ))}
          </div>
        </div>
      )}

      {/* Assigned to */}
      {line.assignedTo && (
        <div className="mb-3 flex items-center gap-2" data-testid="assigned-to">
          <span className="text-muted-foreground text-xs">Assigned:</span>
          <span className="text-xs">{line.assignedTo}</span>
        </div>
      )}

      {/* Description */}
      <div className="mb-4 max-h-[400px] overflow-y-auto" data-testid="issue-description">
        {line.description ? (
          <Markdown className="text-foreground prose-sm max-w-none">{line.description}</Markdown>
        ) : (
          <p className="text-muted-foreground text-sm italic">No description</p>
        )}
      </div>

      {/* Actions - touch-friendly on mobile */}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={handleEdit}
          aria-label="Edit"
          className="min-h-[44px] px-4"
        >
          <Pencil className="mr-1.5 h-4 w-4" />
          Edit
        </Button>
        {hasActiveSession ? (
          <Button
            variant="outline"
            size="sm"
            onClick={handleOpenSession}
            aria-label="Open Session"
            className="min-h-[44px] px-4"
          >
            <ExternalLink className="mr-1.5 h-4 w-4" />
            Open Session
          </Button>
        ) : (
          <Button
            variant="outline"
            size="sm"
            onClick={handleRunAgent}
            aria-label="Run Agent"
            className="min-h-[44px] px-4"
          >
            <Play className="mr-1.5 h-4 w-4" />
            Run Agent
          </Button>
        )}
      </div>
    </div>
  )
})
