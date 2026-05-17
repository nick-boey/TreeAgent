import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Sessions, SessionMode } from '@/api'
import { toast } from 'sonner'

// Import first to get the actual component
import './sessions.$sessionId'

// Mock all dependencies
vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn((path: string) => {
    return (config: { component: React.ComponentType }) => ({
      path,
      component: config.component,
    })
  }),
  useParams: () => ({ sessionId: 'test-session-id' }),
  useRouterState: () => '/sessions/test-session-id',
  useNavigate: () => vi.fn(),
  Outlet: () => null,
  Link: ({
    children,
    to,
    params,
    ...rest
  }: {
    children: React.ReactNode
    to: string
    params?: { sessionId: string }
    [key: string]: unknown
  }) => {
    // Handle parameterized routes
    const href = params?.sessionId ? to.replace('$sessionId', params.sessionId) : to
    // Pass through other props like aria-label, className, etc.
    return (
      <a href={href} {...rest}>
        {children}
      </a>
    )
  },
}))

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Sessions: {
      postApiSessionsByIdMessages: vi.fn(),
      deleteApiSessionsById: vi.fn(),
    },
  }
})

vi.mock('sonner', () => ({
  toast: {
    error: vi.fn(),
    info: vi.fn(),
  },
}))

vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(() => ({
    isConnected: true,
    methods: null,
    connectionStatus: 'connected',
    error: null,
  })),
}))

vi.mock('@/features/sessions/hooks/use-session-events', () => ({
  useSessionEvents: vi.fn(() => ({
    state: {
      lastSeenSeq: 0,
      messages: [],
      pendingQuestion: null,
      pendingPlan: null,
      toolCallIndex: {},
      systemInit: null,
      hookEvents: [],
      isRunning: false,
      lastError: null,
      unknownEvents: [],
    },
    isReplayingHistory: false,
    replayError: null,
  })),
}))

vi.mock('@/features/sessions/hooks/use-clear-context', () => ({
  useClearContext: vi.fn(() => ({
    clearContext: vi.fn(),
    isPending: false,
    error: null,
  })),
}))

vi.mock('@/features/sessions', () => ({
  useSession: vi.fn(),
  MessageList: vi.fn(() => <div data-testid="message-list" />),
  ChatInput: vi.fn(),
  useEntityInfo: vi.fn(),
  useStopSession: vi.fn(() => ({
    mutate: vi.fn(),
    isPending: false,
  })),
  useInterruptSession: vi.fn(() => ({
    mutate: vi.fn(),
    isPending: false,
  })),
  useSessionSettings: vi.fn(() => ({ mode: 'build', model: 'opus' })),
  useChangeSessionSettings: vi.fn(() => ({
    changeMode: vi.fn(),
    changeModel: vi.fn(),
    isChanging: false,
    error: null,
  })),
  SessionInfoPanel: vi.fn(() => null),
  useSessionNavigation: vi.fn(() => ({
    previousSessionId: null,
    nextSessionId: null,
    hasPrevious: false,
    hasNext: false,
    isLoading: false,
  })),
  useClearContext: vi.fn(() => ({
    clearContext: vi.fn(),
    isPending: false,
    error: null,
  })),
}))

vi.mock('@/hooks/use-breadcrumbs', () => ({
  useBreadcrumbSetter: vi.fn(),
}))

vi.mock('@/components/ui/scroll-to-bottom', () => ({
  ScrollToBottom: vi.fn(() => null),
}))

// Mock AlertDialog components
vi.mock('@/components/ui/alert-dialog', () => ({
  AlertDialog: ({ children, open }: { children: React.ReactNode; open: boolean }) =>
    open ? <div data-testid="alert-dialog">{children}</div> : null,
  AlertDialogTrigger: ({ children }: { children: React.ReactNode }) => children,
  AlertDialogContent: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="dialog-content">{children}</div>
  ),
  AlertDialogHeader: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="dialog-header">{children}</div>
  ),
  AlertDialogFooter: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="dialog-footer">{children}</div>
  ),
  AlertDialogTitle: ({ children }: { children: React.ReactNode }) => (
    <h2 data-testid="dialog-title">{children}</h2>
  ),
  AlertDialogDescription: ({ children }: { children: React.ReactNode }) => (
    <p data-testid="dialog-description">{children}</p>
  ),
  AlertDialogCancel: ({
    children,
    onClick,
  }: {
    children: React.ReactNode
    onClick?: () => void
  }) => (
    <button data-testid="dialog-cancel" onClick={onClick}>
      {children}
    </button>
  ),
  AlertDialogAction: ({
    children,
    onClick,
  }: {
    children: React.ReactNode
    onClick?: () => void
  }) => (
    <button data-testid="dialog-action" onClick={onClick}>
      {children}
    </button>
  ),
}))

// Import the component directly
import { Route } from './sessions.$sessionId'

// Extract the component from the route
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const SessionChat = (Route as any).component as React.ComponentType

describe('SessionChat - Message Sending', () => {
  const mockSessionsAPI = vi.mocked(Sessions)
  const mockToast = vi.mocked(toast)
  const mockAddUserMessage = vi.fn()
  const mockSession = {
    id: 'test-session-id',
    entityId: 'entity-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'build' as const,
    status: 'waitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()
    mockAddUserMessage.mockClear()

    // Set up connected state by default
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: true,
      methods: null,
      status: 'connected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    const { useSession, ChatInput, useEntityInfo } = vi.mocked(await import('@/features/sessions'))

    // Mock useEntityInfo to return a default value
    ;(useEntityInfo as Mock).mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    })

    // useSessionEvents is mocked at module level with an empty state. Tests that
    // cared about the addUserMessage optimistic path are obsolete — the server
    // echoes user messages as user.message custom envelopes now.
    void mockAddUserMessage

    // Mock useSession to return a valid session
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    // Mock ChatInput to capture onSend callback
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'build', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))
  })

  it('should send message successfully via HTTP API', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with correct parameters
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: SessionMode.BUILD },
      })
    })

    // Verify no error was shown
    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should show error toast when session not found (404)', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockRejectedValueOnce({
      status: 404,
      error: 'Not Found',
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Session not found')
    })
  })

  it('should show generic error toast for network errors', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi
      .fn()
      .mockRejectedValueOnce(new Error('Network error'))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Failed to send message')
    })
  })

  it('should disable send button when not connected', async () => {
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: false,
      methods: null,
      status: 'disconnected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    expect(sendButton).toBeDisabled()
  })

  it('should disable send button when session is processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'running', mode: 'build' as const },
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    expect(sendButton).toBeDisabled()
  })

  it('should disable send during message sending', async () => {
    const user = userEvent.setup()
    let resolvePromise: () => void
    const sendPromise = new Promise<void>((resolve) => {
      resolvePromise = resolve
    })

    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockReturnValueOnce(sendPromise)

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')

    // Initially enabled (connected)
    expect(sendButton).not.toBeDisabled()

    await user.click(sendButton)

    // Should show loading state
    await waitFor(() => {
      expect(screen.queryByTestId('loading')).toBeInTheDocument()
    })

    // Button should be disabled while sending
    expect(sendButton).toBeDisabled()

    // Resolve the promise
    resolvePromise!()

    await waitFor(() => {
      expect(screen.queryByTestId('loading')).not.toBeInTheDocument()
      expect(sendButton).not.toBeDisabled()
    })
  })

  it('should send Plan mode as SessionMode.PLAN', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    // Mock ChatInput to send Plan mode
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'plan', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with Plan mode
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: SessionMode.PLAN },
      })
    })

    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should send Build mode as SessionMode.BUILD', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    // Mock ChatInput to send Build mode
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'build', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with Build mode
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: SessionMode.BUILD },
      })
    })

    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should show appropriate placeholder text when not connected', async () => {
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: false,
      methods: null,
      status: 'connecting',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Connecting...')
  })

  it('should show appropriate placeholder text when processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))

    // Ensure we're connected
    useClaudeCodeHub.mockReturnValue({
      isConnected: true,
      methods: null,
      status: 'connected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    // Set session as processing
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'running', mode: 'build' as const },
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Processing...')
  })

  // Optimistic addUserMessage tests removed with the a2a-native-messaging migration.
  // User messages now arrive via server-echoed `user.message` AG-UI custom envelopes, so
  // the client-side optimistic append is gone. The API call still happens on send; that
  // path is covered by "should send message with current mode when Send is clicked"
  // and the failure path by a separate error-handling test.
  it('should send message to API when Send is clicked', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: SessionMode.BUILD },
      })
    })
  })

  it('should toast an error when the send API call fails', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi
      .fn()
      .mockRejectedValueOnce(new Error('Network error'))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Failed to send message')
    })
  })
})

describe('SessionChat - Entity Title Display', () => {
  const mockSession = {
    id: 'test-session-id',
    entityId: 'issue-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'build' as const,
    status: 'waitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()

    const { useSession, useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useEntityInfo as Mock).mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    })
  })

  it('should display entity title when successfully loaded', async () => {
    const { useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    ;(useEntityInfo as Mock).mockReturnValue({
      data: { type: 'issue', title: 'Fix authentication bug', id: 'issue-123' },
      isLoading: false,
      error: null,
    })

    render(<SessionChat />)

    await waitFor(() => {
      expect(screen.getByText('Fix authentication bug')).toBeInTheDocument()
    })
    expect(screen.queryByText(/Session test-session-id/)).not.toBeInTheDocument()
  })

  it('should show loading state while fetching entity title', async () => {
    const { useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    ;(useEntityInfo as Mock).mockReturnValue({
      data: null,
      isLoading: true,
      error: null,
    })

    render(<SessionChat />)

    // Should show session ID as fallback during loading
    expect(screen.getByText('Session test-ses...')).toBeInTheDocument()
  })

  it('should fallback to session ID when entity info fails to load', async () => {
    const { useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    ;(useEntityInfo as Mock).mockReturnValue({
      data: null,
      isLoading: false,
      error: new Error('Failed to fetch entity'),
    })

    render(<SessionChat />)

    // Should show session ID as fallback when error
    expect(screen.getByText('Session test-ses...')).toBeInTheDocument()
  })

  it('should display PR title correctly', async () => {
    const prSession = { ...mockSession, entityId: 'pr-456' }
    const { useSession, useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: prSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useEntityInfo as Mock).mockReturnValue({
      data: { type: 'pr', title: 'Add new feature', id: 'pr-456' },
      isLoading: false,
      error: null,
    })

    render(<SessionChat />)

    await waitFor(() => {
      expect(screen.getByText('Add new feature')).toBeInTheDocument()
    })
  })
})

describe('SessionChat - Stop Button Functionality', () => {
  const mockSession = {
    id: 'test-session-id',
    entityId: 'issue-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'build' as const,
    status: 'waitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()

    const { useSession, useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useEntityInfo as Mock).mockReturnValue({
      data: { type: 'issue', title: 'Test Issue', id: 'issue-123' },
      isLoading: false,
      error: null,
    })
  })

  it('should display stop button for active session', async () => {
    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    expect(stopButton).toBeInTheDocument()
  })

  it('should not display stop button for stopped session', async () => {
    const stoppedSession = { ...mockSession, status: 'stopped' as const }
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: stoppedSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
  })

  it('should not display stop button for error session', async () => {
    const errorSession = { ...mockSession, status: 'error' as const }
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: errorSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
  })

  it('should show confirmation dialog when stop button clicked', async () => {
    const user = userEvent.setup()
    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    await user.click(stopButton)

    expect(screen.getByTestId('alert-dialog')).toBeInTheDocument()
    expect(screen.getByTestId('dialog-title')).toHaveTextContent('Stop Session')
    expect(screen.getByTestId('dialog-description')).toHaveTextContent(
      /Are you sure you want to stop this session/
    )
  })

  it('should cancel stop when cancel button clicked in dialog', async () => {
    const user = userEvent.setup()
    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    await user.click(stopButton)

    const cancelButton = screen.getByTestId('dialog-cancel')
    await user.click(cancelButton)

    expect(screen.queryByTestId('alert-dialog')).not.toBeInTheDocument()
  })

  it('should call stop mutation when confirmed', async () => {
    const mockStopMutate = vi.fn()
    const { useStopSession } = vi.mocked(await import('@/features/sessions'))
    ;(useStopSession as Mock).mockReturnValue({
      mutate: mockStopMutate,
      isPending: false,
    })

    const user = userEvent.setup()
    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    await user.click(stopButton)

    const confirmButton = screen.getByTestId('dialog-action')
    await user.click(confirmButton)

    expect(mockStopMutate).toHaveBeenCalledWith(
      'test-session-id',
      expect.objectContaining({
        onSuccess: expect.any(Function),
      })
    )
  })

  it('should disable stop button while stop is pending', async () => {
    const { useStopSession } = vi.mocked(await import('@/features/sessions'))
    ;(useStopSession as Mock).mockReturnValue({
      mutate: vi.fn(),
      isPending: true,
    })

    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    expect(stopButton).toBeDisabled()
  })

  it('should show stop button on mobile layout', async () => {
    // Set viewport to mobile size
    window.innerWidth = 375
    window.innerHeight = 667

    render(<SessionChat />)

    const stopButton = await screen.findByRole('button', { name: /stop/i })
    expect(stopButton).toBeInTheDocument()
  })
})

describe('SessionChat - Interrupt Button Functionality', () => {
  const mockSession = {
    id: 'test-session-id',
    entityId: 'issue-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'build' as const,
    status: 'waitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()

    const { useSession, useEntityInfo } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useEntityInfo as Mock).mockReturnValue({
      data: { type: 'issue', title: 'Test Issue', id: 'issue-123' },
      isLoading: false,
      error: null,
    })
  })

  it('should not display interrupt button when session is in waitingForInput', async () => {
    render(<SessionChat />)

    expect(screen.queryByRole('button', { name: /interrupt/i })).not.toBeInTheDocument()
  })

  it('should display interrupt button when session is running', async () => {
    const runningSession = { ...mockSession, status: 'running' as const }
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: runningSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    expect(await screen.findByRole('button', { name: /interrupt/i })).toBeInTheDocument()
  })

  it('should display interrupt button when session is waitingForPlanExecution', async () => {
    const planSession = { ...mockSession, status: 'waitingForPlanExecution' as const }
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: planSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    expect(await screen.findByRole('button', { name: /interrupt/i })).toBeInTheDocument()
  })

  it('should not display interrupt button when session is stopped', async () => {
    const stoppedSession = { ...mockSession, status: 'stopped' as const }
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: stoppedSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    expect(screen.queryByRole('button', { name: /interrupt/i })).not.toBeInTheDocument()
  })

  it('should call interrupt mutation when interrupt button clicked', async () => {
    const mockInterruptMutate = vi.fn()
    const { useSession, useInterruptSession } = vi.mocked(await import('@/features/sessions'))
    const runningSession = { ...mockSession, status: 'running' as const }
    useSession.mockReturnValue({
      session: runningSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useInterruptSession as Mock).mockReturnValue({
      mutate: mockInterruptMutate,
      isPending: false,
    })

    const user = userEvent.setup()
    render(<SessionChat />)

    const interruptButton = await screen.findByRole('button', { name: /interrupt/i })
    await user.click(interruptButton)

    expect(mockInterruptMutate).toHaveBeenCalledWith('test-session-id')
  })

  it('should disable interrupt button while interrupt is pending', async () => {
    const { useSession, useInterruptSession } = vi.mocked(await import('@/features/sessions'))
    const runningSession = { ...mockSession, status: 'running' as const }
    useSession.mockReturnValue({
      session: runningSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useInterruptSession as Mock).mockReturnValue({
      mutate: vi.fn(),
      isPending: true,
    })

    render(<SessionChat />)

    const interruptButton = await screen.findByRole('button', { name: /interrupt/i })
    expect(interruptButton).toBeDisabled()
  })
})

describe('SessionChat - Session Navigation', () => {
  const mockSession = {
    id: 'test-session-id',
    entityId: 'issue-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'build' as const,
    status: 'waitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()

    const { useSession, useEntityInfo, useSessionNavigation } = vi.mocked(
      await import('@/features/sessions')
    )
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      isJoined: true,
      refetch: vi.fn(),
    })
    ;(useEntityInfo as Mock).mockReturnValue({
      data: { type: 'issue', title: 'Test Issue', id: 'issue-123' },
      isLoading: false,
      error: null,
    })
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: null,
      nextSessionId: null,
      hasPrevious: false,
      hasNext: false,
      isLoading: false,
    })
  })

  it('should render previous and next navigation buttons', async () => {
    render(<SessionChat />)

    const prevButton = await screen.findByRole('button', { name: /previous session/i })
    const nextButton = await screen.findByRole('button', { name: /next session/i })

    expect(prevButton).toBeInTheDocument()
    expect(nextButton).toBeInTheDocument()
  })

  it('should disable previous button when no previous session exists', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: null,
      nextSessionId: 'session-next',
      hasPrevious: false,
      hasNext: true,
      isLoading: false,
    })

    render(<SessionChat />)

    const prevButton = await screen.findByRole('button', { name: /no previous session/i })
    expect(prevButton).toBeDisabled()
  })

  it('should disable next button when no next session exists', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: 'session-prev',
      nextSessionId: null,
      hasPrevious: true,
      hasNext: false,
      isLoading: false,
    })

    render(<SessionChat />)

    const nextButton = await screen.findByRole('button', { name: /no next session/i })
    expect(nextButton).toBeDisabled()
  })

  it('should link to correct previous session when available', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: 'session-prev-123',
      nextSessionId: null,
      hasPrevious: true,
      hasNext: false,
      isLoading: false,
    })

    render(<SessionChat />)

    // When hasPrevious is true, it renders as a link using asChild
    const prevLink = await screen.findByLabelText(/go to previous session/i)
    expect(prevLink).toHaveAttribute('href', '/sessions/session-prev-123')
  })

  it('should link to correct next session when available', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: null,
      nextSessionId: 'session-next-456',
      hasPrevious: false,
      hasNext: true,
      isLoading: false,
    })

    render(<SessionChat />)

    // When hasNext is true, it renders as a link using asChild
    const nextLink = await screen.findByLabelText(/go to next session/i)
    expect(nextLink).toHaveAttribute('href', '/sessions/session-next-456')
  })

  it('should disable both buttons when no navigation targets exist', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: null,
      nextSessionId: null,
      hasPrevious: false,
      hasNext: false,
      isLoading: false,
    })

    render(<SessionChat />)

    const prevButton = await screen.findByRole('button', { name: /no previous session/i })
    const nextButton = await screen.findByRole('button', { name: /no next session/i })

    expect(prevButton).toBeDisabled()
    expect(nextButton).toBeDisabled()
  })

  it('should enable both buttons when both navigation targets exist', async () => {
    const { useSessionNavigation } = vi.mocked(await import('@/features/sessions'))
    ;(useSessionNavigation as Mock).mockReturnValue({
      previousSessionId: 'session-prev',
      nextSessionId: 'session-next',
      hasPrevious: true,
      hasNext: true,
      isLoading: false,
    })

    render(<SessionChat />)

    // When navigation targets exist, they render as links using asChild
    const prevLink = await screen.findByLabelText(/go to previous session/i)
    const nextLink = await screen.findByLabelText(/go to next session/i)

    expect(prevLink).toHaveAttribute('href', '/sessions/session-prev')
    expect(nextLink).toHaveAttribute('href', '/sessions/session-next')
  })
})

// SessionChat - Plan Rejection via Chat tests were retired with the
// questions-plans-as-tools change. Plan approval / rejection now flows
// through the propose_plan Toolkit renderer's addResult callback, not through
// composer interception. See ChatSurface.stories.tsx for the new interactions.
