import {
  createFileRoute,
  useParams,
  Link,
  Outlet,
  useRouterState,
  useNavigate,
} from '@tanstack/react-router'
import { useEffect, useRef, useCallback, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { toApiSessionMode, normalizeSessionMode } from '@/lib/utils/session-mode'
import {
  useSession,
  ChatInput,
  useEntityInfo,
  useStopSession,
  useInterruptSession,
  useSessionSettings,
  useChangeSessionSettings,
  SessionInfoPanel,
  useSessionNavigation,
} from '@/features/sessions'
import { useSessionEvents } from '@/features/sessions/hooks/use-session-events'
import { useClearContext } from '@/features/sessions/hooks/use-clear-context'
import { ChatSurface } from '@/features/sessions/components/assistant-chat/ChatSurface'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import {
  ArrowLeft,
  AlertCircle,
  RefreshCw,
  StopCircle,
  PanelRight,
  ChevronLeft,
  ChevronRight,
  FileCheck,
  History,
  Plus,
  Pause,
} from 'lucide-react'
import { useMobile } from '@/hooks/use-mobile'
import { cn } from '@/lib/utils'
import { Sessions, SessionType } from '@/api'
import { toast } from 'sonner'
import type { SessionMode } from '@/types/signalr'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'

export const Route = createFileRoute('/sessions/$sessionId')({
  component: SessionLayout,
})

/**
 * Layout component that either renders the session chat or child routes (like issue-diff).
 * This wrapper ensures we don't violate React's rules of hooks.
 */
function SessionLayout() {
  const { sessionId } = useParams({ from: '/sessions/$sessionId' })
  const currentPath = useRouterState({ select: (s) => s.location.pathname })

  // Check if we're at a child route (like issue-diff)
  const isChildRoute = currentPath !== `/sessions/${sessionId}`

  // If we're at a child route, render the Outlet for child content
  if (isChildRoute) {
    return <Outlet />
  }

  // Render the session chat content
  return <SessionChat sessionId={sessionId} />
}

function SessionChat({ sessionId }: { sessionId: string }) {
  const navigate = useNavigate()
  const { session, isLoading, isNotFound, error, refetch, isJoined } = useSession(sessionId)
  const { isConnected } = useClaudeCodeHub()
  const scrollContainerRef = useRef<HTMLDivElement>(null)
  const [isSending, setIsSending] = useState(false)
  const [showStopDialog, setShowStopDialog] = useState(false)
  const [infoPanelOpen, setInfoPanelOpen] = useState(false)
  const [viewingHistoricalSessionId, setViewingHistoricalSessionId] = useState<string | null>(null)
  const isMobile = useMobile()

  // Determine if we're viewing a historical session
  const isViewingHistorical = viewingHistoricalSessionId !== null

  // Resolve the event stream for whichever session is being viewed. The new unified
  // hook fetches `/api/sessions/{id}/events` replay on mount and subscribes to live
  // `ReceiveSessionEvent` envelopes; passing a historical sessionId simply yields a
  // replay-only view (no live envelopes will arrive for a terminated session).
  const viewedSessionId = viewingHistoricalSessionId ?? sessionId
  const { state: aguiState, isReplayingHistory } = useSessionEvents(viewedSessionId)
  const messages = aguiState.messages

  // Get session settings (mode/model) from server or cache
  const { mode, model } = useSessionSettings(sessionId, session)
  // Hook to change mode/model
  const { changeMode, changeModel } = useChangeSessionSettings(sessionId)

  // Fetch entity info
  const { data: entityInfo } = useEntityInfo(session?.entityId, session?.projectId)

  // Stop session mutation
  const stopSession = useStopSession()

  // Interrupt session mutation (sends SIGINT to the agent, returns to WaitingForInput)
  const interruptSession = useInterruptSession()

  // Clear context and start new session
  const { clearContext, isPending: isClearingContext } = useClearContext()

  // Interactive tool calls (ask_user_question, propose_plan) render inline via the
  // Toolkit's frontend tools in `features/sessions/runtime/toolkit.tsx`. The legacy
  // `usePlanApproval` / `useApprovePlan` / `useAnswerQuestion` wiring + footerSlot
  // panels were retired with the questions-plans-as-tools change.

  // Determine if the session is processing (not accepting input)
  const isProcessing =
    session?.status === 'running' ||
    session?.status === 'runningHooks' ||
    session?.status === 'starting'

  // Handle sending messages. The server echoes each accepted user message as a
  // `user.message` custom AG-UI event, so the reducer picks it up from the envelope
  // stream — no local optimistic append is needed.
  const handleSend = useCallback(
    async (message: string, sessionMode: SessionMode, _model: string) => {
      if (!isConnected || !isJoined) return

      // Normal message sending flow — plan approval and question answering are now
      // handled inline by the propose_plan / ask_user_question Toolkit renderers via
      // addResult, not by intercepting the composer.
      setIsSending(true)

      try {
        // Map our string SessionMode to API's numeric enum
        const apiMode = toApiSessionMode(sessionMode)

        await Sessions.postApiSessionsByIdMessages({
          path: { id: sessionId },
          body: { message, mode: apiMode },
        })
      } catch (error) {
        const errorMessage =
          error && typeof error === 'object' && 'status' in error && error.status === 404
            ? 'Session not found'
            : 'Failed to send message'
        toast.error(errorMessage)
      } finally {
        setIsSending(false)
      }
    },
    [isConnected, isJoined, sessionId]
  )

  // Handle stop session
  const handleStop = useCallback(() => {
    setShowStopDialog(true)
  }, [])

  const confirmStop = useCallback(() => {
    stopSession.mutate(sessionId, {
      onSuccess: () => navigate({ to: '/sessions' }),
    })
    setShowStopDialog(false)
  }, [stopSession, sessionId, navigate])

  const cancelStop = useCallback(() => {
    setShowStopDialog(false)
  }, [])

  // Toggle info panel
  const handleToggleInfoPanel = useCallback(() => {
    setInfoPanelOpen((prev) => !prev)
  }, [])

  // Handle selecting a historical session to view
  const handleSelectHistoricalSession = useCallback(
    (selectedSessionId: string) => {
      // If selecting the active session, clear historical view
      if (selectedSessionId === sessionId) {
        setViewingHistoricalSessionId(null)
      } else {
        setViewingHistoricalSessionId(selectedSessionId)
      }
    },
    [sessionId]
  )

  // Return to active session
  const handleReturnToActive = useCallback(() => {
    setViewingHistoricalSessionId(null)
  }, [])

  // Handle starting a new session (clear context)
  const handleNewSession = useCallback(() => {
    clearContext(sessionId)
  }, [clearContext, sessionId])

  // Handle interrupt session
  const handleInterrupt = useCallback(() => {
    interruptSession.mutate(sessionId)
  }, [interruptSession, sessionId])

  // Auto-scroll to bottom when new messages arrive.
  useEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current.scrollTop = scrollContainerRef.current.scrollHeight
    }
  }, [messages])

  useBreadcrumbSetter(
    [
      { title: 'Sessions', url: '/sessions' },
      {
        title:
          entityInfo?.title || (session ? `Session ${sessionId.slice(0, 8)}...` : 'Loading...'),
      },
    ],
    [sessionId, session, entityInfo]
  )

  // Loading state
  if (isLoading) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} session={null} />
        <div className="flex flex-1 flex-col gap-4 overflow-hidden rounded-lg border p-4">
          <div className="flex justify-end">
            <Skeleton className="h-12 w-48 rounded-lg" />
          </div>
          <div className="flex justify-start">
            <Skeleton className="h-24 w-64 rounded-lg" />
          </div>
          <div className="flex justify-end">
            <Skeleton className="h-12 w-56 rounded-lg" />
          </div>
        </div>
      </div>
    )
  }

  // Session not found
  if (isNotFound) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} session={null} />
        <div className="border-border flex flex-1 flex-col items-center justify-center rounded-lg border p-8">
          <AlertCircle className="text-muted-foreground mb-4 h-12 w-12" />
          <h2 className="mb-2 text-lg font-semibold">Session not found</h2>
          <p className="text-muted-foreground mb-4">
            The session you are looking for does not exist or has been deleted.
          </p>
          <Button asChild>
            <Link to="/sessions">View all sessions</Link>
          </Button>
        </div>
      </div>
    )
  }

  // Error state
  if (error) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} session={null} />
        <div className="border-border flex flex-1 flex-col items-center justify-center rounded-lg border p-8">
          <AlertCircle className="text-destructive mb-4 h-12 w-12" />
          <h2 className="mb-2 text-lg font-semibold">Error loading session</h2>
          <p className="text-muted-foreground mb-4">{error}</p>
          <Button onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Retry
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div
      className={cn(
        'flex h-full flex-col',
        !isMobile && infoPanelOpen && 'md:mr-80' // 320px right margin for desktop panel
      )}
    >
      <SessionHeader
        sessionId={sessionId}
        session={session}
        entityTitle={entityInfo?.title}
        onStop={handleStop}
        isStopPending={stopSession.isPending}
        onToggleInfoPanel={handleToggleInfoPanel}
        infoPanelOpen={infoPanelOpen}
        onNewSession={handleNewSession}
        isNewSessionPending={isClearingContext}
        onInterrupt={handleInterrupt}
        isInterruptPending={interruptSession.isPending}
      />
      {/* Historical session banner */}
      {isViewingHistorical && (
        <div className="mt-4 flex items-center justify-between rounded-lg border border-yellow-500/50 bg-yellow-500/10 px-4 py-2">
          <div className="flex items-center gap-2">
            <History className="h-4 w-4 text-yellow-600" />
            <span className="text-sm text-yellow-700">Viewing historical session (read-only)</span>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={handleReturnToActive}
            className="h-7 text-xs"
          >
            Return to active
          </Button>
        </div>
      )}
      {/* Messages area - flex-1 takes remaining space */}
      <div
        ref={scrollContainerRef}
        className="border-border relative mt-4 flex min-h-0 flex-1 flex-col overflow-y-auto rounded-lg border"
      >
        <ChatSurface
          state={aguiState}
          sessionId={viewedSessionId}
          sendMessage={async (text) => {
            await handleSend(text, mode, model)
          }}
          isLoading={isLoading || isReplayingHistory}
          className="flex min-h-0 flex-1 flex-col"
        />
      </div>
      {/* Chat input - sticky at bottom with safe area inset for mobile keyboards */}
      {!isViewingHistorical && (
        <div className="bg-background sticky bottom-0 mt-3 pb-[env(safe-area-inset-bottom)] md:mt-4">
          <ChatInput
            onSend={handleSend}
            sessionMode={mode}
            sessionModel={model}
            onModeChange={changeMode}
            onModelChange={changeModel}
            projectId={session?.projectId}
            disabled={isProcessing || !isConnected || !isJoined}
            isLoading={isSending}
            placeholder={
              !isConnected
                ? 'Connecting...'
                : !isJoined
                  ? 'Joining session...'
                  : isProcessing
                    ? 'Processing...'
                    : 'Type a message...'
            }
          />
        </div>
      )}

      {/* Stop confirmation dialog */}
      <AlertDialog open={showStopDialog} onOpenChange={setShowStopDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Stop Session</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to stop this session? This will terminate the running agent.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={cancelStop}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={confirmStop}>Stop</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Session info panel */}
      {session && (
        <SessionInfoPanel
          session={session}
          messages={messages}
          isOpen={infoPanelOpen}
          onOpenChange={setInfoPanelOpen}
          viewingHistoricalSessionId={viewingHistoricalSessionId}
          onSelectHistoricalSession={handleSelectHistoricalSession}
        />
      )}
    </div>
  )
}

interface SessionHeaderProps {
  sessionId: string
  session?: {
    id: string
    entityId: string
    projectId: string
    mode: string | number
    status: string
    model: string
    sessionType?: string
  } | null
  entityTitle?: string
  onStop?: () => void
  isStopPending?: boolean
  onToggleInfoPanel?: () => void
  infoPanelOpen?: boolean
  onNewSession?: () => void
  isNewSessionPending?: boolean
  onInterrupt?: () => void
  isInterruptPending?: boolean
}

/**
 * Convert session mode (which may be numeric from API or string from SignalR types)
 * to a display-friendly string.
 */
function getModeDisplayString(mode: string | number): string {
  return normalizeSessionMode(mode)
}

function SessionHeader({
  sessionId,
  session,
  entityTitle,
  onStop,
  isStopPending,
  onToggleInfoPanel,
  infoPanelOpen,
  onNewSession,
  isNewSessionPending,
  onInterrupt,
  isInterruptPending,
}: SessionHeaderProps) {
  // Determine if stop button should be shown
  const showStopButton =
    session && session.status !== 'stopped' && session.status !== 'error' && onStop

  // Interrupt is available while the session is actively processing
  const showInterruptButton =
    onInterrupt &&
    session &&
    (session.status === 'running' ||
      session.status === 'runningHooks' ||
      session.status === 'waitingForPlanExecution' ||
      session.status === 'waitingForQuestionAnswer')

  // Session navigation
  const { previousSessionId, nextSessionId, hasPrevious, hasNext } = useSessionNavigation(sessionId)

  return (
    <div className="flex items-center justify-between gap-2">
      <div className="flex min-w-0 items-center gap-2 md:gap-4">
        {/* Touch-friendly back button */}
        <Button variant="ghost" size="icon" asChild className="h-10 w-10 shrink-0">
          <Link to="/sessions">
            <ArrowLeft className="h-5 w-5" />
          </Link>
        </Button>
        {/* Session navigation buttons */}
        <div className="flex shrink-0 items-center">
          {hasPrevious ? (
            <Button
              variant="ghost"
              size="icon"
              asChild
              className="h-10 w-10"
              aria-label="Go to previous session"
            >
              <Link to="/sessions/$sessionId" params={{ sessionId: previousSessionId! }}>
                <ChevronLeft className="h-5 w-5" />
              </Link>
            </Button>
          ) : (
            <Button
              variant="ghost"
              size="icon"
              className="h-10 w-10"
              disabled
              aria-label="No previous session"
            >
              <ChevronLeft className="h-5 w-5" />
            </Button>
          )}
          {hasNext ? (
            <Button
              variant="ghost"
              size="icon"
              asChild
              className="h-10 w-10"
              aria-label="Go to next session"
            >
              <Link to="/sessions/$sessionId" params={{ sessionId: nextSessionId! }}>
                <ChevronRight className="h-5 w-5" />
              </Link>
            </Button>
          ) : (
            <Button
              variant="ghost"
              size="icon"
              className="h-10 w-10"
              disabled
              aria-label="No next session"
            >
              <ChevronRight className="h-5 w-5" />
            </Button>
          )}
        </div>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-semibold md:text-2xl">
            {entityTitle || `Session ${sessionId.slice(0, 8)}...`}
          </h1>
          {session && (
            <div className="text-muted-foreground flex flex-wrap items-center gap-1 text-xs md:gap-2 md:text-sm">
              <span className="capitalize">{getModeDisplayString(session.mode)}</span>
              <span className="hidden sm:inline">•</span>
              <span className="hidden sm:inline">{session.model}</span>
              <span>•</span>
              <SessionStatusBadge status={session.status} />
            </div>
          )}
        </div>
      </div>
      <div className="flex items-center gap-2">
        {session?.sessionType === SessionType.ISSUE_AGENT_MODIFICATION && (
          <Button variant="outline" size="sm" asChild className="h-8">
            <Link to="/sessions/$sessionId/issue-diff" params={{ sessionId }}>
              <FileCheck className="mr-2 h-4 w-4" />
              Review Changes
            </Link>
          </Button>
        )}
        {onNewSession && session && (
          <Button
            variant="outline"
            size="sm"
            onClick={onNewSession}
            disabled={isNewSessionPending}
            className="h-8"
            data-testid="session-new"
          >
            <Plus className="mr-2 h-4 w-4" />
            New Session
          </Button>
        )}
        {showInterruptButton && (
          <Button
            variant="outline"
            size="sm"
            onClick={onInterrupt}
            disabled={isInterruptPending}
            className="h-8"
            data-testid="session-interrupt"
          >
            <Pause className="mr-2 h-4 w-4" />
            Interrupt
          </Button>
        )}
        {showStopButton && (
          <Button
            variant="destructive"
            size="sm"
            onClick={onStop}
            disabled={isStopPending}
            className="h-8"
            data-testid="session-stop"
          >
            <StopCircle className="mr-2 h-4 w-4" />
            Stop
          </Button>
        )}
        {onToggleInfoPanel && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onToggleInfoPanel}
            className={cn('h-10 w-10', infoPanelOpen && 'bg-accent')}
            aria-label={infoPanelOpen ? 'Close info panel' : 'Open info panel'}
          >
            <PanelRight className="h-5 w-5" />
          </Button>
        )}
      </div>
    </div>
  )
}

function SessionStatusBadge({ status }: { status: string }) {
  const getStatusColor = (status: string) => {
    switch (status) {
      case 'running':
      case 'runningHooks':
        return 'bg-green-500/20 text-green-700'
      case 'waitingForInput':
      case 'waitingForQuestionAnswer':
      case 'waitingForPlanExecution':
        return 'bg-yellow-500/20 text-yellow-700'
      case 'stopped':
        return 'bg-gray-500/20 text-gray-700'
      case 'error':
        return 'bg-red-500/20 text-red-700'
      default:
        return 'bg-blue-500/20 text-blue-700'
    }
  }

  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${getStatusColor(status)}`}>
      {status}
    </span>
  )
}
