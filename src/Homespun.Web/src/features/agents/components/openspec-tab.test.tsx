import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import {
  OpenSpecTabContent,
  autoSelectOpenSpecSkill,
  isSkillReady,
  buildSchemaOverride,
} from './openspec-tab'
import {
  BranchPresence,
  ChangePhase,
  Graph,
  Issues,
  Projects,
  Skills,
  SkillCategory,
  SessionMode,
} from '@/api'
import type {
  DiscoveredSkills,
  IssueOpenSpecState,
  IssueResponse,
  RunAgentAcceptedResponse,
  TaskGraphResponse,
  Project,
} from '@/api/generated/types.gen'

const OPENSPEC_SKILL_DESCRIPTORS: DiscoveredSkills = {
  homespun: [],
  general: [],
  openSpec: [
    { name: 'openspec-explore', description: 'Explore', category: SkillCategory.OPEN_SPEC },
    { name: 'openspec-propose', description: 'Propose', category: SkillCategory.OPEN_SPEC },
    { name: 'openspec-new-change', description: 'New change', category: SkillCategory.OPEN_SPEC },
    {
      name: 'openspec-continue-change',
      description: 'Continue',
      category: SkillCategory.OPEN_SPEC,
    },
    { name: 'openspec-apply-change', description: 'Apply', category: SkillCategory.OPEN_SPEC },
    {
      name: 'openspec-verify-change',
      description: 'Verify',
      category: SkillCategory.OPEN_SPEC,
    },
    { name: 'openspec-sync-specs', description: 'Sync', category: SkillCategory.OPEN_SPEC },
    {
      name: 'openspec-archive-change',
      description: 'Archive',
      category: SkillCategory.OPEN_SPEC,
    },
  ],
}

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Skills: { getApiSkillsProjectByProjectId: vi.fn() },
    Issues: { postApiIssuesByIssueIdRun: vi.fn() },
    Graph: { getApiGraphByProjectIdTaskgraphData: vi.fn() },
    Projects: { getApiProjectsById: vi.fn() },
  }
})

const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

const mockGetSkills = vi.mocked(Skills.getApiSkillsProjectByProjectId)
const mockRunAgent = vi.mocked(Issues.postApiIssuesByIssueIdRun)
const mockGetGraph = vi.mocked(Graph.getApiGraphByProjectIdTaskgraphData)
const mockGetProject = vi.mocked(Projects.getApiProjectsById)

function createMockResponse<T>(data: T) {
  return {
    data,
    error: undefined,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createRunResponse(
  overrides: Partial<RunAgentAcceptedResponse> = {}
): RunAgentAcceptedResponse {
  return { issueId: 'issue-1', branchName: 'feat/test+abc123', message: 'starting', ...overrides }
}

function createProject(overrides: Partial<Project> = {}): Project {
  return {
    id: 'project-1',
    name: 'Project',
    localPath: '/tmp/project',
    defaultBranch: 'main',
    gitRemoteUrl: 'https://example.com/x.git',
    ...overrides,
  } as Project
}

function createGraph(state: IssueOpenSpecState | null, issueId = 'issue-1'): TaskGraphResponse {
  return {
    nodes: [],
    totalLanes: 0,
    mergedPrs: [],
    hasMorePastPrs: false,
    totalPastPrsShown: 0,
    agentStatuses: {},
    linkedPrs: {},
    openSpecStates: state ? { [issueId]: state } : {},
    mainOrphanChanges: [],
  } as TaskGraphResponse
}

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

describe('openspec tab pure helpers', () => {
  it('autoSelectOpenSpecSkill: none/incomplete -> explore', () => {
    expect(autoSelectOpenSpecSkill(null)).toBe('openspec-explore')
    expect(
      autoSelectOpenSpecSkill({
        branchState: BranchPresence.EXISTS,
        changeState: ChangePhase.INCOMPLETE,
      })
    ).toBe('openspec-explore')
  })

  it('autoSelectOpenSpecSkill: ready-to-apply -> apply', () => {
    expect(
      autoSelectOpenSpecSkill({
        branchState: BranchPresence.WITH_CHANGE,
        changeState: ChangePhase.READY_TO_APPLY,
      })
    ).toBe('openspec-apply-change')
  })

  it('autoSelectOpenSpecSkill: ready-to-archive/archived -> archive', () => {
    expect(
      autoSelectOpenSpecSkill({
        branchState: BranchPresence.WITH_CHANGE,
        changeState: ChangePhase.READY_TO_ARCHIVE,
      })
    ).toBe('openspec-archive-change')
    expect(
      autoSelectOpenSpecSkill({
        branchState: BranchPresence.WITH_CHANGE,
        changeState: ChangePhase.ARCHIVED,
      })
    ).toBe('openspec-archive-change')
  })

  it('isSkillReady: ungated skills always ready', () => {
    const none = { branchState: BranchPresence.NONE, changeState: ChangePhase.NONE }
    expect(isSkillReady('openspec-explore', none)).toBe(true)
    expect(isSkillReady('openspec-propose', none)).toBe(true)
    expect(isSkillReady('openspec-new-change', none)).toBe(true)
    expect(isSkillReady('openspec-continue-change', none)).toBe(true)
  })

  it('isSkillReady: apply gated on artifacts done', () => {
    const incomplete = {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.INCOMPLETE,
    }
    const readyApply = {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_APPLY,
    }
    expect(isSkillReady('openspec-apply-change', incomplete)).toBe(false)
    expect(isSkillReady('openspec-apply-change', readyApply)).toBe(true)
  })

  it('isSkillReady: verify/sync/archive gated on tasks done', () => {
    const readyApply = {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_APPLY,
    }
    const readyArchive = {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_ARCHIVE,
    }
    expect(isSkillReady('openspec-verify-change', readyApply)).toBe(false)
    expect(isSkillReady('openspec-verify-change', readyArchive)).toBe(true)
    expect(isSkillReady('openspec-sync-specs', readyArchive)).toBe(true)
    expect(isSkillReady('openspec-archive-change', readyArchive)).toBe(true)
  })

  it('buildSchemaOverride: default schema returns null', () => {
    expect(buildSchemaOverride(null)).toBeNull()
    expect(buildSchemaOverride('spec-driven')).toBeNull()
  })

  it('buildSchemaOverride: non-default schema returns phrase', () => {
    expect(buildSchemaOverride('custom')).toBe(
      "use openspec schema 'custom' for all openspec commands"
    )
  })
})

describe('OpenSpecTabContent', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mockGetSkills.mockResolvedValue(createMockResponse(OPENSPEC_SKILL_DESCRIPTORS))
    mockRunAgent.mockResolvedValue(createMockResponse(createRunResponse()))
    mockGetProject.mockResolvedValue(createMockResponse(createProject()))
  })

  const MOCK_ISSUE: IssueResponse = {
    id: 'issue-1',
    title: 'Test Issue',
    description: 'Test description',
  }

  const PREFILLED_BLOCK = 'Issue ID: issue-1\nTitle: Test Issue\nDescription: Test description'

  function renderTab(state: IssueOpenSpecState | null, issue?: IssueResponse) {
    mockGetGraph.mockResolvedValue(createMockResponse(createGraph(state)))
    return render(
      <OpenSpecTabContent
        projectId="project-1"
        issueId="issue-1"
        issue={issue}
        onOpenChange={vi.fn()}
      />,
      { wrapper: wrapper() }
    )
  }

  it('lists all 8 OpenSpec skills', async () => {
    renderTab({ branchState: BranchPresence.NONE, changeState: ChangePhase.NONE })

    const trigger = await screen.findByRole('combobox', { name: /select openspec skill/i })
    await userEvent.setup().click(trigger)
    const listbox = await screen.findByRole('listbox')
    const options = within(listbox).getAllByRole('option')
    expect(options).toHaveLength(8)
  })

  it('auto-selects explore when no change exists', async () => {
    renderTab({ branchState: BranchPresence.NONE, changeState: ChangePhase.NONE })
    const trigger = await screen.findByRole('combobox', { name: /select openspec skill/i })
    await waitFor(() => expect(trigger.textContent ?? '').toMatch(/openspec-explore/))
  })

  it('auto-selects apply when artifacts ready', async () => {
    renderTab({
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_APPLY,
      changeName: 'my-change',
    })
    const trigger = await screen.findByRole('combobox', { name: /select openspec skill/i })
    await waitFor(() => expect(trigger.textContent ?? '').toMatch(/openspec-apply-change/))
  })

  it('auto-selects archive when tasks done', async () => {
    renderTab({
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_ARCHIVE,
      changeName: 'my-change',
    })
    const trigger = await screen.findByRole('combobox', { name: /select openspec skill/i })
    await waitFor(() => expect(trigger.textContent ?? '').toMatch(/openspec-archive-change/))
  })

  it('dispatches with skill name and change name as arg', async () => {
    renderTab(
      {
        branchState: BranchPresence.WITH_CHANGE,
        changeState: ChangePhase.READY_TO_APPLY,
        changeName: 'my-change',
      },
      MOCK_ISSUE
    )

    // Wait for auto-selection to settle.
    const startBtn = await screen.findByTestId('openspec-start-agent')
    await waitFor(() => expect(startBtn).not.toBeDisabled())
    await userEvent.setup().click(startBtn)

    await waitFor(() => {
      expect(mockRunAgent).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { issueId: 'issue-1' },
          body: expect.objectContaining({
            skillName: 'openspec-apply-change',
            skillArgs: { change: 'my-change' },
            mode: SessionMode.BUILD,
            userInstructions: PREFILLED_BLOCK,
          }),
        })
      )
    })
  })

  it('prepends schema override to userInstructions when non-default schema', async () => {
    renderTab(
      {
        branchState: BranchPresence.WITH_CHANGE,
        changeState: ChangePhase.READY_TO_APPLY,
        changeName: 'my-change',
        schemaName: 'custom-schema',
      },
      MOCK_ISSUE
    )

    const startBtn = await screen.findByTestId('openspec-start-agent')
    await waitFor(() => expect(startBtn).not.toBeDisabled())
    await userEvent.setup().click(startBtn)

    await waitFor(() => {
      expect(mockRunAgent).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({
            userInstructions: `use openspec schema 'custom-schema' for all openspec commands\n\n${PREFILLED_BLOCK}`,
          }),
        })
      )
    })
  })

  it('start button disabled when selected skill is blocked', async () => {
    const user = userEvent.setup()
    renderTab({ branchState: BranchPresence.EXISTS, changeState: ChangePhase.INCOMPLETE })

    const trigger = await screen.findByRole('combobox', { name: /select openspec skill/i })
    await user.click(trigger)
    const listbox = await screen.findByRole('listbox')
    const archive = within(listbox).getByRole('option', { name: /openspec-archive-change/ })
    expect(archive).toHaveAttribute('data-disabled')
  })
})
