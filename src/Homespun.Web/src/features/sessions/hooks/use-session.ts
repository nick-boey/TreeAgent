import { useState, useEffect, useCallback, useRef } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { useSessionSettingsStore } from '@/stores/session-settings-store'
import { normalizeSessionMode } from '@/lib/utils/session-mode'
import type { ClaudeSession } from '@/types/signalr'

// NOTE: the custom client-side hop-log batcher was retired with the
// custom-telemetry stack. `traceInvoke` now produces a client span for every
// `joinSession` invocation and the server-side TraceparentHubFilter attaches
// the `homespun.session.id` tag — so the same "did the join succeed?"
// question is answerable from Seq traces instead of a bespoke log channel.

export interface UseSessionResult {
  session: ClaudeSession | null | undefined
  isLoading: boolean
  isNotFound: boolean
  error: string | undefined
  isJoined: boolean
  refetch: () => Promise<void>
}

export function useSession(sessionId: string): UseSessionResult {
  const { connection, methods, isConnected, isReconnecting } = useClaudeCodeHub()
  const [session, setSession] = useState<ClaudeSession | null | undefined>(undefined)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | undefined>()
  const [isJoined, setIsJoined] = useState(false)
  const hasJoinedRef = useRef(false)
  const currentSessionIdRef = useRef(sessionId)
  const wasReconnectingRef = useRef(false)
  const isMountedRef = useRef(true)

  const fetchSession = useCallback(async () => {
    if (!methods || !isConnected) {
      return
    }

    setIsLoading(true)
    setError(undefined)

    try {
      const result = await methods.getSession(sessionId)
      setSession(result)
      // Sync session settings to the per-session cache
      if (result) {
        useSessionSettingsStore.getState().updateSession(sessionId, result.mode, result.model)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch session')
    } finally {
      setIsLoading(false)
    }
  }, [methods, isConnected, sessionId])

  // Track mounted state for cleanup
  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  // Handle session ID changes
  useEffect(() => {
    const previousSessionId = currentSessionIdRef.current
    currentSessionIdRef.current = sessionId

    // If session ID changed, leave the old session
    if (previousSessionId !== sessionId && hasJoinedRef.current && methods) {
      methods.leaveSession(previousSessionId).catch(() => {})
      hasJoinedRef.current = false
      setIsJoined(false)
    }
  }, [sessionId, methods])

  // Fetch session and join group
  useEffect(() => {
    if (!isConnected || !methods) {
      return
    }

    fetchSession()

    // Join session group — traceInvoke wraps this and emits a client span
    // named `signalr.invoke.JoinSession`; errors are recorded on that span.
    methods
      .joinSession(sessionId)
      .then(() => {
        if (isMountedRef.current) {
          hasJoinedRef.current = true
          setIsJoined(true)
        }
      })
      .catch(() => {
        if (isMountedRef.current) {
          hasJoinedRef.current = false
          setIsJoined(false)
        }
      })

    return () => {
      if (hasJoinedRef.current && methods) {
        methods.leaveSession(sessionId).catch(() => {})
        hasJoinedRef.current = false
        setIsJoined(false)
      }
    }
  }, [isConnected, methods, sessionId, fetchSession])

  // Handle reconnection - set isJoined to false during reconnection
  // and re-join when connection recovers
  useEffect(() => {
    // When reconnecting starts, mark as not joined
    if (isReconnecting) {
      wasReconnectingRef.current = true
      hasJoinedRef.current = false
      setIsJoined(false)
      return
    }

    // When transitioning from reconnecting to connected, re-join
    if (wasReconnectingRef.current && isConnected && methods) {
      wasReconnectingRef.current = false

      // Re-join the current session; traceInvoke handles span + error recording.
      const currentSessionId = currentSessionIdRef.current
      methods
        .joinSession(currentSessionId)
        .then(() => {
          if (isMountedRef.current) {
            hasJoinedRef.current = true
            setIsJoined(true)
          }
        })
        .catch(() => {
          if (isMountedRef.current) {
            hasJoinedRef.current = false
            setIsJoined(false)
          }
        })
    }
  }, [isReconnecting, isConnected, methods])

  // Register event handlers
  useEffect(() => {
    if (!connection) {
      return
    }

    const handleSessionState = (updatedSession: ClaudeSession) => {
      if (updatedSession.id === sessionId) {
        setSession(updatedSession)
      }
    }

    const handleSessionStatusChanged = (
      updatedSessionId: string,
      status: ClaudeSession['status'],
      hasPendingPlanApproval: boolean
    ) => {
      if (updatedSessionId === sessionId) {
        setSession((prev) => {
          if (!prev || prev.id !== sessionId) return prev
          return { ...prev, status, hasPendingPlanApproval }
        })
      }
    }

    const handleSessionModeModelChanged = (
      updatedSessionId: string,
      mode: ClaudeSession['mode'],
      model: string
    ) => {
      if (updatedSessionId === sessionId) {
        // Normalize mode to handle numeric values from SignalR (C# enum serialization)
        const normalizedMode = normalizeSessionMode(mode)
        setSession((prevSession) => {
          if (!prevSession || prevSession.id !== sessionId) return prevSession
          return { ...prevSession, mode: normalizedMode, model }
        })
        // Sync to per-session settings cache
        useSessionSettingsStore.getState().updateSession(sessionId, normalizedMode, model)
      }
    }

    connection.on('SessionState', handleSessionState)
    connection.on('SessionStatusChanged', handleSessionStatusChanged)
    connection.on('SessionModeModelChanged', handleSessionModeModelChanged)

    return () => {
      connection.off('SessionState', handleSessionState)
      connection.off('SessionStatusChanged', handleSessionStatusChanged)
      connection.off('SessionModeModelChanged', handleSessionModeModelChanged)
    }
  }, [connection, sessionId])

  return {
    session,
    isLoading,
    isNotFound: session === null,
    error,
    isJoined,
    refetch: fetchSession,
  }
}
