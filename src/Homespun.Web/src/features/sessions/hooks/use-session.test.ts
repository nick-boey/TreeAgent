import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { useSession } from './use-session'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type { ClaudeSession } from '@/types/signalr'

// Mock the SignalR provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

// Helper to flush pending promises
const flushPromises = () => new Promise((resolve) => setTimeout(resolve, 0))

const mockSession: ClaudeSession = {
  id: 'session-123',
  entityId: 'entity-123',
  projectId: 'project-456',
  workingDirectory: '/path/to/project',
  model: 'opus',
  mode: 'build',
  status: 'running',
  createdAt: '2024-01-01T00:00:00Z',
  lastActivityAt: '2024-01-01T01:00:00Z',
  messages: [],
  totalCostUsd: 0.05,
  totalDurationMs: 3600000,
  hasPendingPlanApproval: false,
  contextClearMarkers: [],
}

describe('useSession', () => {
  const mockGetSession = vi.fn()
  const mockJoinSession = vi.fn()
  const mockLeaveSession = vi.fn()
  const mockConnection = {
    on: vi.fn(),
    off: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
    // Set default resolved values for all methods
    mockGetSession.mockResolvedValue(mockSession)
    mockJoinSession.mockResolvedValue(undefined)
    mockLeaveSession.mockResolvedValue(undefined)
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      connection: mockConnection,
      methods: {
        getSession: mockGetSession,
        joinSession: mockJoinSession,
        leaveSession: mockLeaveSession,
      },
      isConnected: true,
      isReconnecting: false,
    })
  })

  it('returns loading state initially', () => {
    mockGetSession.mockReturnValue(new Promise(() => {})) // Never resolves

    const { result } = renderHook(() => useSession('session-123'))

    expect(result.current.isLoading).toBe(true)
    expect(result.current.session).toBeUndefined()
  })

  it('fetches session data when connected', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.session).toEqual(mockSession)
    expect(mockGetSession).toHaveBeenCalledWith('session-123')
  })

  it('joins session when connected', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(mockJoinSession).toHaveBeenCalledWith('session-123')
    })
  })

  it('leaves session on unmount', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)
    mockLeaveSession.mockResolvedValueOnce(undefined)

    const { unmount } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(mockJoinSession).toHaveBeenCalled()
    })

    unmount()

    expect(mockLeaveSession).toHaveBeenCalledWith('session-123')
  })

  it('handles session not found', async () => {
    mockGetSession.mockResolvedValueOnce(null)

    const { result } = renderHook(() => useSession('nonexistent'))

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.session).toBeNull()
    expect(result.current.isNotFound).toBe(true)
  })

  it('handles fetch error', async () => {
    mockGetSession.mockRejectedValueOnce(new Error('Connection failed'))

    const { result } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.error).toBe('Connection failed')
    expect(result.current.session).toBeUndefined()
  })

  it('does not fetch when not connected', () => {
    ;(useClaudeCodeHub as Mock).mockReturnValue({
      connection: null,
      methods: null,
      isConnected: false,
    })

    const { result } = renderHook(() => useSession('session-123'))

    expect(result.current.isLoading).toBe(true)
    expect(mockGetSession).not.toHaveBeenCalled()
  })

  it('registers event handlers for session updates', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(mockConnection.on).toHaveBeenCalled()
    })

    // Should register for SessionState event
    const registeredEvents = mockConnection.on.mock.calls.map((call) => call[0])
    expect(registeredEvents).toContain('SessionState')
  })

  it('registers event handlers for SessionStatusChanged', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(mockConnection.on).toHaveBeenCalled()
    })

    const registeredEvents = mockConnection.on.mock.calls.map((call) => call[0])
    expect(registeredEvents).toContain('SessionStatusChanged')
  })

  it('updates session status when SessionStatusChanged event is received', async () => {
    mockGetSession.mockResolvedValueOnce({
      ...mockSession,
      status: 'waitingForPlanExecution' as const,
    })
    mockJoinSession.mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(result.current.session?.status).toBe('waitingForPlanExecution')
    })

    const statusChangedCall = mockConnection.on.mock.calls.find(
      (call) => call[0] === 'SessionStatusChanged'
    )
    expect(statusChangedCall).toBeDefined()

    const handler = statusChangedCall![1]
    act(() => {
      handler('session-123', 'waitingForInput', false)
    })

    expect(result.current.session?.status).toBe('waitingForInput')
    expect(result.current.session?.hasPendingPlanApproval).toBe(false)
  })

  it('ignores SessionStatusChanged for a different session', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(result.current.session).toEqual(mockSession)
    })

    const statusChangedCall = mockConnection.on.mock.calls.find(
      (call) => call[0] === 'SessionStatusChanged'
    )
    const handler = statusChangedCall![1]
    act(() => {
      handler('other-session-id', 'stopped', false)
    })

    expect(result.current.session?.status).toBe('running')
  })

  it('updates session when SessionState event is received', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    const { result } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(result.current.session).toEqual(mockSession)
    })

    // Find the SessionState handler
    const sessionStateCall = mockConnection.on.mock.calls.find((call) => call[0] === 'SessionState')
    expect(sessionStateCall).toBeDefined()

    const handler = sessionStateCall![1]
    const updatedSession = { ...mockSession, status: 'stopped' as const }

    act(() => {
      handler(updatedSession)
    })

    expect(result.current.session?.status).toBe('stopped')
  })

  it('cleans up event handlers on unmount', async () => {
    mockGetSession.mockResolvedValueOnce(mockSession)
    mockJoinSession.mockResolvedValueOnce(undefined)

    const { unmount } = renderHook(() => useSession('session-123'))

    await waitFor(() => {
      expect(mockConnection.on).toHaveBeenCalled()
    })

    unmount()

    expect(mockConnection.off).toHaveBeenCalled()
  })

  it('refetches session when sessionId changes', async () => {
    mockGetSession.mockResolvedValue(mockSession)
    mockJoinSession.mockResolvedValue(undefined)
    mockLeaveSession.mockResolvedValue(undefined)

    const { result, rerender } = renderHook(({ sessionId }) => useSession(sessionId), {
      initialProps: { sessionId: 'session-123' },
    })

    await waitFor(() => {
      expect(result.current.session).toEqual(mockSession)
    })

    const newSession = { ...mockSession, id: 'session-456' }
    mockGetSession.mockResolvedValueOnce(newSession)

    rerender({ sessionId: 'session-456' })

    await waitFor(() => {
      expect(mockLeaveSession).toHaveBeenCalledWith('session-123')
      expect(mockJoinSession).toHaveBeenCalledWith('session-456')
    })
  })

  describe('isJoined state', () => {
    it('returns isJoined: true after successfully joining session', async () => {
      mockGetSession.mockResolvedValueOnce(mockSession)
      mockJoinSession.mockResolvedValueOnce(undefined)

      const { result } = renderHook(() => useSession('session-123'))

      // Initially not joined
      expect(result.current.isJoined).toBe(false)

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })
    })

    it('returns isJoined: false when not connected', () => {
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: null,
        methods: null,
        isConnected: false,
        isReconnecting: false,
      })

      const { result } = renderHook(() => useSession('session-123'))

      expect(result.current.isJoined).toBe(false)
    })

    it('returns isJoined: false when joinSession fails', async () => {
      mockGetSession.mockResolvedValueOnce(mockSession)
      mockJoinSession.mockRejectedValueOnce(new Error('Failed to join'))

      const { result } = renderHook(() => useSession('session-123'))

      // Wait for join to be attempted
      await flushPromises()

      // isJoined should remain false since join failed
      expect(result.current.isJoined).toBe(false)
    })
  })

  describe('reconnection handling', () => {
    it('sets isJoined to false when connection enters reconnecting state', async () => {
      mockGetSession.mockResolvedValue(mockSession)
      mockJoinSession.mockResolvedValue(undefined)

      const { result, rerender } = renderHook(() => useSession('session-123'))

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })

      // Simulate reconnecting state
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: false,
        isReconnecting: true,
      })

      rerender()

      // isJoined should be false during reconnection
      expect(result.current.isJoined).toBe(false)
    })

    it('re-joins session after connection recovers from reconnecting', async () => {
      mockGetSession.mockResolvedValue(mockSession)
      mockJoinSession.mockResolvedValue(undefined)

      const { result, rerender } = renderHook(() => useSession('session-123'))

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })

      // Clear mocks to track new calls
      mockJoinSession.mockClear()

      // Simulate reconnecting state
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: false,
        isReconnecting: true,
      })

      rerender()

      expect(result.current.isJoined).toBe(false)

      // Simulate reconnection complete - back to connected
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: true,
        isReconnecting: false,
      })

      rerender()

      // Should have called joinSession again after reconnection
      await waitFor(() => {
        expect(mockJoinSession).toHaveBeenCalledWith('session-123')
      })

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })
    })

    it('joins correct session after reconnect when sessionId changed during reconnect', async () => {
      mockGetSession.mockResolvedValue(mockSession)
      mockJoinSession.mockResolvedValue(undefined)
      mockLeaveSession.mockResolvedValue(undefined)

      const { result, rerender } = renderHook(({ sessionId }) => useSession(sessionId), {
        initialProps: { sessionId: 'session-123' },
      })

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })

      mockJoinSession.mockClear()

      // Simulate reconnecting state
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: false,
        isReconnecting: true,
      })

      rerender({ sessionId: 'session-123' })

      // Change session ID during reconnection
      const newSession = { ...mockSession, id: 'session-456' }
      mockGetSession.mockResolvedValue(newSession)
      rerender({ sessionId: 'session-456' })

      // Simulate reconnection complete
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: true,
        isReconnecting: false,
      })

      rerender({ sessionId: 'session-456' })

      // Should join the NEW session ID, not the old one
      await waitFor(() => {
        expect(mockJoinSession).toHaveBeenCalledWith('session-456')
      })

      // Should NOT have joined the old session
      const joinCalls = mockJoinSession.mock.calls.filter((call) => call[0] === 'session-123')
      expect(joinCalls.length).toBe(0)
    })

    it('does not attempt re-join if component unmounted during reconnection', async () => {
      mockGetSession.mockResolvedValue(mockSession)
      mockJoinSession.mockResolvedValue(undefined)

      const { result, rerender, unmount } = renderHook(() => useSession('session-123'))

      await waitFor(() => {
        expect(result.current.isJoined).toBe(true)
      })

      mockJoinSession.mockClear()

      // Simulate reconnecting state
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: false,
        isReconnecting: true,
      })

      rerender()

      // Unmount during reconnection
      unmount()

      // Simulate reconnection complete after unmount
      ;(useClaudeCodeHub as Mock).mockReturnValue({
        connection: mockConnection,
        methods: {
          getSession: mockGetSession,
          joinSession: mockJoinSession,
          leaveSession: mockLeaveSession,
        },
        isConnected: true,
        isReconnecting: false,
      })

      // Wait for any potential async operations
      await flushPromises()

      // Should NOT have called joinSession after reconnection since unmounted
      expect(mockJoinSession).not.toHaveBeenCalled()
    })
  })
})
