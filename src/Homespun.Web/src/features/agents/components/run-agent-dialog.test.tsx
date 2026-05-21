import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RunAgentDialog } from './run-agent-dialog'
import {
  Issues,
  IssuesAgent,
  Models,
  Skills,
  SessionMode,
  SkillCategory,
  SkillArgKind,
} from '@/api'
import type { ReactNode } from 'react'
import type {
  ClaudeModelInfo,
  DiscoveredSkills,
  IssueResponse,
  RunAgentAcceptedResponse,
} from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Skills: {
      getApiSkillsProjectByProjectId: vi.fn(),
    },
    Issues: {
      postApiIssuesByIssueIdRun: vi.fn(),
      getApiIssuesByIssueId: vi.fn(),
    },
    IssuesAgent: {
      postApiIssuesAgentSession: vi.fn(),
    },
    Models: {
      getApiModels: vi.fn(),
    },
  }
})

const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

const mockGetSkills = vi.mocked(Skills.getApiSkillsProjectByProjectId)
const mockRunAgent = vi.mocked(Issues.postApiIssuesByIssueIdRun)
const mockCreateIssuesAgentSession = vi.mocked(IssuesAgent.postApiIssuesAgentSession)
const mockGetModels = vi.mocked(Models.getApiModels)
const mockGetIssue = vi.mocked(Issues.getApiIssuesByIssueId)

const MOCK_MODELS: ClaudeModelInfo[] = [
  {
    id: 'claude-opus-4-7-20251101',
    displayName: 'Claude Opus 4.7',
    createdAt: '2025-11-01T00:00:00Z',
    isDefault: true,
  },
  {
    id: 'claude-sonnet-4-6-20250601',
    displayName: 'Claude Sonnet 4.6',
    createdAt: '2025-06-01T00:00:00Z',
  },
  {
    id: 'claude-haiku-4-5-20251001',
    displayName: 'Claude Haiku 4.5',
    createdAt: '2025-10-01T00:00:00Z',
  },
]

const MOCK_ISSUE: IssueResponse = {
  id: 'issue-456',
  title: 'Test Issue Title',
  description: 'Test issue description',
}

const PREFILLED_BLOCK =
  'Issue ID: issue-456\nTitle: Test Issue Title\nDescription: Test issue description'

function createMockResponse<T>(data: T) {
  return {
    data,
    error: undefined,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

const MOCK_SKILLS: DiscoveredSkills = {
  openSpec: [],
  general: [],
  homespun: [
    {
      name: 'fix-bug',
      description: 'Fix a bug',
      category: SkillCategory.HOMESPUN,
      mode: SessionMode.BUILD,
      args: [{ name: 'issue-id', kind: SkillArgKind.ISSUE, label: 'Issue ID' }],
    },
  ],
}

function createMockRunAgentResponse(
  overrides: Partial<RunAgentAcceptedResponse> = {}
): RunAgentAcceptedResponse {
  return {
    issueId: 'issue-456',
    branchName: 'feature/test-123',
    message: 'Agent is starting',
    ...overrides,
  }
}

function getTaskTab() {
  return screen.getByTestId('task-tab-content')
}

function getIssuesTab() {
  return screen.getByTestId('issues-tab-content')
}

describe('RunAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()

    mockGetSkills.mockResolvedValue(createMockResponse(MOCK_SKILLS))
    mockRunAgent.mockResolvedValue(createMockResponse(createMockRunAgentResponse()))
    mockGetModels.mockResolvedValue(createMockResponse(MOCK_MODELS))
    // Default: issue fetch fails gracefully so issue stays undefined (no prefill in most tests)
    mockGetIssue.mockResolvedValue(createMockResponse(undefined as unknown as IssueResponse))
  })

  it('renders nothing when closed', () => {
    const { container } = render(
      <RunAgentDialog
        open={false}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('renders dialog with both tabs when open', async () => {
    render(
      <RunAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument()
    })
    expect(screen.getByRole('tab', { name: /task agent/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /issues agent/i })).toBeInTheDocument()
  })

  describe('OpenSpec tab visibility', () => {
    it('shows the OpenSpec tab when issueId is set', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      await waitFor(() => {
        expect(screen.getByRole('tab', { name: /openspec/i })).toBeInTheDocument()
      })
    })

    it('shows the OpenSpec tab when only selectedIssueId is set', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          selectedIssueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      await waitFor(() => {
        expect(screen.getByRole('tab', { name: /openspec/i })).toBeInTheDocument()
      })
    })

    it('hides the OpenSpec tab when neither issueId nor selectedIssueId is set', async () => {
      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      await waitFor(() => {
        expect(screen.getByRole('dialog')).toBeInTheDocument()
      })
      expect(screen.queryByRole('tab', { name: /openspec/i })).not.toBeInTheDocument()
    })
  })

  it('defaults to task tab when issueId is provided', async () => {
    render(
      <RunAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /task agent/i })).toHaveAttribute(
        'data-state',
        'active'
      )
    })
  })

  it('defaults to issues tab when no issueId', async () => {
    render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /issues agent/i })).toHaveAttribute(
        'data-state',
        'active'
      )
    })
  })

  describe('Issue ID strip', () => {
    it('renders the strip when issueId is set', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      await waitFor(() => {
        expect(screen.getByText('issue-456')).toBeInTheDocument()
      })
    })

    it('renders the strip when selectedIssueId is set and issueId is absent', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          selectedIssueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      await waitFor(() => {
        expect(screen.getByText('issue-456')).toBeInTheDocument()
      })
    })

    it('does not render the strip when neither issueId nor selectedIssueId is set', async () => {
      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      await waitFor(() => {
        expect(screen.getByRole('dialog')).toBeInTheDocument()
      })
      expect(screen.queryByRole('button', { name: /copy/i })).not.toBeInTheDocument()
    })
  })

  describe('Task Agent Tab', () => {
    it('shows skill picker, mode, model, and start button', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('combobox', { name: /select skill/i })).toBeInTheDocument()
      })
      expect(within(taskTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
      expect(within(taskTab).getByRole('combobox', { name: 'Select model' })).toBeInTheDocument()
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('Task Agent textarea stays empty (no prefill) when an issue is linked', async () => {
      mockGetIssue.mockResolvedValue(createMockResponse(MOCK_ISSUE))

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      const textarea = within(taskTab).getByPlaceholderText(/additional instructions/i)
      expect(textarea).toHaveValue('')
    })

    it('sends skillName and skillArgs when a skill is selected', async () => {
      const user = userEvent.setup()
      const onAgentStart = vi.fn()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
          onAgentStart={onAgentStart}
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      // Wait for skills loaded
      const skillTrigger = await within(taskTab).findByRole('combobox', { name: /select skill/i })
      await user.click(skillTrigger)

      // Select fix-bug
      const listbox = await screen.findByRole('listbox')
      await user.click(within(listbox).getByText(/fix-bug/))

      // Fill in the arg
      const issueIdInput = await within(taskTab).findByLabelText('Issue ID')
      await user.type(issueIdInput, 'ABC123')

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            projectId: 'project-123',
            skillName: 'fix-bug',
            skillArgs: { 'issue-id': 'ABC123' },
          }),
        })
      })

      expect(onAgentStart).toHaveBeenCalledWith({
        issueId: 'issue-456',
        branchName: 'feature/test-123',
        message: 'Agent is starting',
      })
    })

    it('sends no skillName / skillArgs when no skill selected', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalled()
      })

      const body = mockRunAgent.mock.calls[0]?.[0]?.body
      expect(body?.skillName).toBeUndefined()
      expect(body?.skillArgs).toBeUndefined()
    })

    it('includes user instructions in the dispatch', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      const textarea = await within(taskTab).findByPlaceholderText(/additional instructions/i)
      await user.type(textarea, 'Do the thing')

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            userInstructions: 'Do the thing',
          }),
        })
      })
    })

    it('shows conflict state when agent already running', async () => {
      const user = userEvent.setup()

      mockRunAgent.mockResolvedValue({
        data: undefined,
        error: { sessionId: 'existing-session', status: 'Running', message: 'Already running' },
        request: new Request('http://localhost/api/test'),
        response: new Response(null, { status: 409 }),
      } as never)

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(within(taskTab).getByText(/agent already running/i)).toBeInTheDocument()
      })
    })

    it('shows advanced settings with base branch selector', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByText(/more settings/i)).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByText(/more settings/i))

      await waitFor(() => {
        expect(within(taskTab).getByText(/base branch/i)).toBeInTheDocument()
      })
    })
  })

  describe('Issues Agent Tab (skill-less)', () => {
    it('has no skill picker — only mode, model, textarea, start button', async () => {
      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      await waitFor(() => {
        expect(screen.getByRole('tab', { name: /issues agent/i })).toHaveAttribute(
          'data-state',
          'active'
        )
      })

      expect(
        within(issuesTab).queryByRole('combobox', { name: /select skill/i })
      ).not.toBeInTheDocument()
      expect(within(issuesTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
      expect(within(issuesTab).getByRole('combobox', { name: 'Select model' })).toBeInTheDocument()
      expect(within(issuesTab).getByPlaceholderText(/additional instructions/i)).toBeInTheDocument()
      expect(within(issuesTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('stays on page when user instructions are provided', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: {
          sessionId: 'session-123',
          branchName: 'issues-agent-123',
          clonePath: '/tmp/clone',
        },
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
      await user.type(textarea, 'Do something specific')

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })
      expect(mockNavigate).not.toHaveBeenCalled()
    })

    it('navigates to session when no instructions are provided', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: {
          sessionId: 'session-123',
          branchName: 'issues-agent-123',
          clonePath: '/tmp/clone',
        },
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith({
          to: '/sessions/$sessionId',
          params: { sessionId: 'session-123' },
        })
      })
    })

    it('Issues Agent textarea is prefilled with formatted block when issue is linked', async () => {
      mockGetIssue.mockResolvedValue(createMockResponse(MOCK_ISSUE))

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          selectedIssueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const issuesTab = getIssuesTab()

      await waitFor(() => {
        const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
        expect(textarea).toHaveValue(PREFILLED_BLOCK)
      })
    })

    it('issue-bound Issues Agent dispatch does NOT navigate to session page', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockGetIssue.mockResolvedValue(createMockResponse(MOCK_ISSUE))
      mockCreateIssuesAgentSession.mockResolvedValue({
        data: {
          sessionId: 'session-123',
          branchName: 'issues-agent-123',
          clonePath: '/tmp/clone',
        },
      } as never)

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={onOpenChange}
          projectId="project-123"
          selectedIssueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const issuesTab = getIssuesTab()

      // Wait for prefill to settle
      await waitFor(() => {
        const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
        expect(textarea).toHaveValue(PREFILLED_BLOCK)
      })

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })
      expect(mockNavigate).not.toHaveBeenCalled()
    })

    it('first-write-wins: user-typed content is preserved when useIssue resolves later', async () => {
      // Simulate slow issue fetch with a deferred resolution
      let resolveIssue!: (value: unknown) => void
      const issuePromise = new Promise((resolve) => {
        resolveIssue = resolve
      })
      mockGetIssue.mockReturnValue(issuePromise as ReturnType<typeof mockGetIssue>)

      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          selectedIssueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const issuesTab = getIssuesTab()

      // User types before issue resolves
      const textarea = await within(issuesTab).findByPlaceholderText(/additional instructions/i)
      await user.type(textarea, 'custom instructions')

      // Now issue resolves
      resolveIssue(createMockResponse(MOCK_ISSUE))

      // Wait a beat then check the textarea has not been overwritten
      await waitFor(() => {
        expect(textarea).toHaveValue('custom instructions')
      })
    })
  })

  describe('model dropdown sourced from useAvailableModels', () => {
    it('populates task-tab dropdown with models from the API', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()
      const modelCombo = await within(taskTab).findByRole('combobox', { name: 'Select model' })

      await waitFor(() => {
        expect(modelCombo).not.toBeDisabled()
      })

      await user.click(modelCombo)

      const listbox = await screen.findByRole('listbox')
      expect(within(listbox).getByText(/Claude Opus 4\.7/)).toBeInTheDocument()
      expect(within(listbox).getByText(/Claude Sonnet 4\.6/)).toBeInTheDocument()
      expect(within(listbox).getByText(/Claude Haiku 4\.5/)).toBeInTheDocument()
    })

    it('uses the isDefault model when localStorage is empty and sends its id to the API', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).not.toBeDisabled()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            model: 'claude-opus-4-7-20251101',
          }),
        })
      })
    })

    it('normalizes a stored tier-prefix alias (opus) to the newest opus in the catalog', async () => {
      const user = userEvent.setup()
      localStorage.setItem('agent-launcher-model', 'opus')

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).not.toBeDisabled()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            model: 'claude-opus-4-7-20251101',
          }),
        })
      })
    })

    it('falls to default when stored id is not in the current catalog', async () => {
      const user = userEvent.setup()
      localStorage.setItem('agent-launcher-model', 'claude-obsolete-id-20200101')

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).not.toBeDisabled()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            model: 'claude-opus-4-7-20251101',
          }),
        })
      })
    })

    it('disables the start button while the catalog is loading', () => {
      mockGetModels.mockImplementation(
        () =>
          new Promise(() => {}) as Promise<{
            data: ClaudeModelInfo[]
            request: Request
            response: Response
          }>
      )

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeDisabled()
    })
  })
})
