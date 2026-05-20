import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InlineIssueDetailRow } from './inline-issue-detail-row'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueStatus, IssueType, ExecutionMode } from '@/api'

// Helper to create a mock render line
function createRenderLine(
  overrides: Partial<TaskGraphIssueRenderLine> = {}
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: 'abc123',
    title: 'Test Issue',
    description: 'This is a test description',
    branchName: 'feature/test-branch+abc123',
    lane: 0,
    marker: TaskGraphMarkerType.Open,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    hasDescription: true,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    appearanceIndex: 1,
    totalAppearances: 1,
    parentIssueId: null,
    ...overrides,
  }
}

describe('InlineIssueDetailRow', () => {
  const defaultProps = {
    line: createRenderLine(),
    maxLanes: 3,
    onEdit: vi.fn(),
    onRunAgent: vi.fn(),
    onClose: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders issue ID', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('abc123')).toBeInTheDocument()
    })

    it('renders type badge for task', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('Task')).toBeInTheDocument()
    })

    it('renders type badge for bug', () => {
      const line = createRenderLine({ issueType: IssueType.BUG })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Bug')).toBeInTheDocument()
    })

    it('renders type badge for chore', () => {
      const line = createRenderLine({ issueType: IssueType.CHORE })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Chore')).toBeInTheDocument()
    })

    it('renders type badge for feature', () => {
      const line = createRenderLine({ issueType: IssueType.FEATURE })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Feature')).toBeInTheDocument()
    })

    it('renders type badge for idea', () => {
      const line = createRenderLine({ issueType: IssueType.IDEA })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Idea')).toBeInTheDocument()
    })

    it('renders status badge for open', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('Open')).toBeInTheDocument()
    })

    it('renders status badge for in progress', () => {
      const line = createRenderLine({ status: IssueStatus.PROGRESS })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('In Progress')).toBeInTheDocument()
    })

    it('renders status badge for in review', () => {
      const line = createRenderLine({ status: IssueStatus.REVIEW })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Review')).toBeInTheDocument()
    })

    it('renders status badge for complete', () => {
      const line = createRenderLine({ status: IssueStatus.COMPLETE })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Complete')).toBeInTheDocument()
    })
  })

  describe('branch name', () => {
    it('renders branch name when available', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('feature/test-branch+abc123')).toBeInTheDocument()
    })

    it('renders copy button for branch name', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /copy branch/i })).toBeInTheDocument()
    })

    it('does not render branch section when branch name is null', () => {
      const line = createRenderLine({ branchName: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByRole('button', { name: /copy branch/i })).not.toBeInTheDocument()
    })

    it('copies branch name to clipboard when copy button is clicked', async () => {
      const user = userEvent.setup()
      const writeTextMock = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText: writeTextMock },
        writable: true,
        configurable: true,
      })

      render(<InlineIssueDetailRow {...defaultProps} />)
      await user.click(screen.getByRole('button', { name: /copy branch/i }))

      expect(writeTextMock).toHaveBeenCalledWith('feature/test-branch+abc123')
    })
  })

  describe('description', () => {
    it('renders description text', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByText('This is a test description')).toBeInTheDocument()
    })

    it('shows no description message when description is null', () => {
      const line = createRenderLine({ description: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText(/no description/i)).toBeInTheDocument()
    })

    it('renders markdown in description', () => {
      const line = createRenderLine({ description: '**Bold text** and `code`' })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      // Check that markdown elements are rendered (bold text becomes <strong>)
      expect(screen.getByText('Bold text')).toBeInTheDocument()
    })
  })

  describe('linked PR', () => {
    it('renders linked PR badge when available', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('#123')).toBeInTheDocument()
    })

    it('shows open status badge for open PR', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('pr-status-badge')).toHaveTextContent(/open/i)
    })

    it('shows merged status badge for merged PR', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'merged' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('pr-status-badge')).toHaveTextContent(/merged/i)
    })

    it('links to PR URL when clicked', () => {
      const line = createRenderLine({
        linkedPr: { number: 123, url: 'https://github.com/test/pr/123', status: 'open' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      const link = screen.getByRole('link', { name: /#123/i })
      expect(link).toHaveAttribute('href', 'https://github.com/test/pr/123')
    })
  })

  describe('agent status', () => {
    it('shows active agent indicator when agent is running', () => {
      const line = createRenderLine({
        agentStatus: { isActive: true, status: 'running', sessionId: 'session-123' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByTestId('agent-status-indicator')).toBeInTheDocument()
      expect(screen.getByText(/running/i)).toBeInTheDocument()
    })

    it('shows link to session when agent is active', () => {
      const line = createRenderLine({
        agentStatus: { isActive: true, status: 'running', sessionId: 'session-123' },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByRole('link', { name: /view session/i })).toBeInTheDocument()
    })

    it('does not show agent section when agent is inactive', () => {
      const line = createRenderLine({
        agentStatus: { isActive: false, status: 'stopped', sessionId: null },
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByTestId('agent-status-indicator')).not.toBeInTheDocument()
    })
  })

  describe('execution mode', () => {
    it('displays series execution mode', () => {
      const line = createRenderLine({ executionMode: ExecutionMode.SERIES })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Series')).toBeInTheDocument()
    })

    it('displays parallel execution mode', () => {
      const line = createRenderLine({ executionMode: ExecutionMode.PARALLEL })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('Parallel')).toBeInTheDocument()
    })
  })

  describe('parent issues', () => {
    it('displays parent issues with sort order', () => {
      const line = createRenderLine({
        parentIssues: [{ parentIssue: 'par123', sortOrder: 'ab' }],
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('par123:ab')).toBeInTheDocument()
    })

    it('displays multiple parent issues', () => {
      const line = createRenderLine({
        parentIssues: [
          { parentIssue: 'par123', sortOrder: 'ab' },
          { parentIssue: 'par456', sortOrder: 'cd' },
        ],
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('par123:ab')).toBeInTheDocument()
      expect(screen.getByText('par456:cd')).toBeInTheDocument()
    })

    it('displays parent issue without sort order', () => {
      const line = createRenderLine({
        parentIssues: [{ parentIssue: 'par123', sortOrder: null }],
      })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('par123')).toBeInTheDocument()
    })

    it('does not show parent issues section when empty', () => {
      const line = createRenderLine({ parentIssues: [] })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByText('Parents:')).not.toBeInTheDocument()
    })

    it('does not show parent issues section when null', () => {
      const line = createRenderLine({ parentIssues: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByText('Parents:')).not.toBeInTheDocument()
    })
  })

  describe('assigned to', () => {
    it('displays assigned user', () => {
      const line = createRenderLine({ assignedTo: 'user@example.com' })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.getByText('user@example.com')).toBeInTheDocument()
    })

    it('does not show assigned section when null', () => {
      const line = createRenderLine({ assignedTo: null })
      render(<InlineIssueDetailRow {...defaultProps} line={line} />)
      expect(screen.queryByText('Assigned:')).not.toBeInTheDocument()
    })
  })

  describe('description scrolling', () => {
    it('has scrollable description container', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      const descriptionContainer = screen.getByTestId('issue-description')
      expect(descriptionContainer).toHaveClass('overflow-y-auto')
      expect(descriptionContainer).toHaveClass('max-h-[400px]')
    })
  })

  describe('actions', () => {
    it('renders edit button', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument()
    })

    it('renders run agent button', () => {
      render(<InlineIssueDetailRow {...defaultProps} />)
      expect(screen.getByRole('button', { name: /run agent/i })).toBeInTheDocument()
    })

    it('calls onEdit when edit button is clicked', async () => {
      const user = userEvent.setup()
      const onEdit = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onEdit={onEdit} />)

      await user.click(screen.getByRole('button', { name: /edit/i }))
      expect(onEdit).toHaveBeenCalledWith('abc123')
    })

    it('calls onRunAgent when run agent button is clicked', async () => {
      const user = userEvent.setup()
      const onRunAgent = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onRunAgent={onRunAgent} />)

      await user.click(screen.getByRole('button', { name: /run agent/i }))
      expect(onRunAgent).toHaveBeenCalledWith('abc123')
    })
  })

  describe('close behavior', () => {
    it('calls onClose when close button is clicked', async () => {
      const user = userEvent.setup()
      const onClose = vi.fn()
      render(<InlineIssueDetailRow {...defaultProps} onClose={onClose} />)

      await user.click(screen.getByRole('button', { name: /close/i }))
      expect(onClose).toHaveBeenCalled()
    })
  })

  describe('styling', () => {
    it('applies correct left margin based on maxLanes', () => {
      const { container } = render(<InlineIssueDetailRow {...defaultProps} maxLanes={3} />)
      const detailRow = container.firstChild as HTMLElement
      // SVG width = 24 * max(maxLanes, 1) + 12 = 24 * 3 + 12 = 84
      expect(detailRow).toHaveStyle({ marginLeft: '84px' })
    })

    it('has expand animation class', () => {
      const { container } = render(<InlineIssueDetailRow {...defaultProps} />)
      const detailRow = container.firstChild as HTMLElement
      expect(detailRow).toHaveClass('animate-expand')
    })
  })

  // Height-reporting contract: drives the cumulative-offset math in
  // `TaskGraphEdges` (Decisions 2 & 3 of redraw-graph-edges-reactively).
  describe('onHeightChange', () => {
    it('reports panel offsetHeight synchronously on mount (before paint)', () => {
      const onHeightChange = vi.fn()
      const FAKE_HEIGHT = 240
      // jsdom returns 0 for offsetHeight; stub it for any HTMLElement.
      const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetHeight')
      Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
        configurable: true,
        get() {
          return FAKE_HEIGHT
        },
      })
      try {
        render(<InlineIssueDetailRow {...defaultProps} onHeightChange={onHeightChange} />)
        // useLayoutEffect runs synchronously during render in test environment.
        expect(onHeightChange).toHaveBeenCalledWith('abc123', FAKE_HEIGHT)
      } finally {
        if (original) {
          Object.defineProperty(HTMLElement.prototype, 'offsetHeight', original)
        } else {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          delete (HTMLElement.prototype as any).offsetHeight
        }
      }
    })

    it('reports height === 0 on unmount so parent can drop the entry', () => {
      const onHeightChange = vi.fn()
      const { unmount } = render(
        <InlineIssueDetailRow {...defaultProps} onHeightChange={onHeightChange} />
      )
      onHeightChange.mockClear()
      unmount()
      expect(onHeightChange).toHaveBeenCalledWith('abc123', 0)
    })
  })
})
