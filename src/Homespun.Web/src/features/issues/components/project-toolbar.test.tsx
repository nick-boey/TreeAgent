import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import React from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProjectToolbar } from './project-toolbar'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const defaultProps = {
  selectedIssueId: null as string | null,
  onCreateAbove: vi.fn(),
  onCreateBelow: vi.fn(),
  onMakeChild: vi.fn(),
  onMakeParent: vi.fn(),
  onEditIssue: vi.fn(),
  onOpenAgentLauncher: vi.fn(),
  onAssignIssue: vi.fn(),
  depth: 3,
  onDepthChange: vi.fn(),
  searchQuery: '',
  onSearchChange: vi.fn(),
  searchMatchCount: 0,
  onNextMatch: vi.fn(),
  onPreviousMatch: vi.fn(),
  onEmbedSearch: vi.fn(),
  onApplyDefaultFilter: vi.fn(),
  onRemoveParent: vi.fn(),
  onRemoveAllParents: vi.fn(),
  onMoveUp: vi.fn(),
  onMoveDown: vi.fn(),
  canMoveUp: false,
  canMoveDown: false,
}

function renderToolbar(props = {}) {
  return render(React.createElement(ProjectToolbar, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

describe('ProjectToolbar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('Creation buttons', () => {
    it('renders create above button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /create above/i })).toBeInTheDocument()
    })

    it('renders create below button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /create below/i })).toBeInTheDocument()
    })

    it('calls onCreateAbove when create above button is clicked', async () => {
      const user = userEvent.setup()
      const onCreateAbove = vi.fn()
      renderToolbar({ onCreateAbove })

      await user.click(screen.getByRole('button', { name: /create above/i }))
      expect(onCreateAbove).toHaveBeenCalled()
    })

    it('calls onCreateBelow when create below button is clicked', async () => {
      const user = userEvent.setup()
      const onCreateBelow = vi.fn()
      renderToolbar({ onCreateBelow })

      await user.click(screen.getByRole('button', { name: /create below/i }))
      expect(onCreateBelow).toHaveBeenCalled()
    })
  })

  describe('Hierarchy buttons', () => {
    it('renders make child button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /make child/i })).toBeInTheDocument()
    })

    it('renders make parent button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /make parent/i })).toBeInTheDocument()
    })

    it('calls onMakeChild when make child button is clicked', async () => {
      const user = userEvent.setup()
      const onMakeChild = vi.fn()
      renderToolbar({ onMakeChild, selectedIssueId: 'test-issue-1' })

      await user.click(screen.getByRole('button', { name: /make child/i }))
      expect(onMakeChild).toHaveBeenCalled()
    })

    it('calls onMakeParent when make parent button is clicked', async () => {
      const user = userEvent.setup()
      const onMakeParent = vi.fn()
      renderToolbar({ onMakeParent, selectedIssueId: 'test-issue-1' })

      await user.click(screen.getByRole('button', { name: /make parent/i }))
      expect(onMakeParent).toHaveBeenCalled()
    })
  })

  describe('Remove parent buttons', () => {
    it('renders remove parent button', () => {
      renderToolbar()
      expect(screen.getByTestId('toolbar-remove-parent')).toBeInTheDocument()
    })

    it('renders remove all parents button', () => {
      renderToolbar()
      expect(screen.getByTestId('toolbar-remove-all-parents')).toBeInTheDocument()
    })

    it('disables remove parent button when no issue selected', () => {
      renderToolbar({ selectedIssueId: null })
      expect(screen.getByTestId('toolbar-remove-parent')).toBeDisabled()
    })

    it('disables remove all parents button when no issue selected', () => {
      renderToolbar({ selectedIssueId: null })
      expect(screen.getByTestId('toolbar-remove-all-parents')).toBeDisabled()
    })

    it('enables remove parent button when issue is selected', () => {
      renderToolbar({ selectedIssueId: 'test-issue-1' })
      expect(screen.getByTestId('toolbar-remove-parent')).not.toBeDisabled()
    })

    it('enables remove all parents button when issue is selected', () => {
      renderToolbar({ selectedIssueId: 'test-issue-1' })
      expect(screen.getByTestId('toolbar-remove-all-parents')).not.toBeDisabled()
    })

    it('shows ring when remove parent is active', () => {
      renderToolbar({ selectedIssueId: 'test-issue-1', removeParentActive: true })
      expect(screen.getByTestId('toolbar-remove-parent')).toHaveClass('ring-2')
    })

    it('calls onRemoveParent when remove parent button is clicked', async () => {
      const user = userEvent.setup()
      const onRemoveParent = vi.fn()
      renderToolbar({ selectedIssueId: 'test-issue-1', onRemoveParent })

      await user.click(screen.getByTestId('toolbar-remove-parent'))
      expect(onRemoveParent).toHaveBeenCalled()
    })

    it('calls onRemoveAllParents when remove all parents button is clicked', async () => {
      const user = userEvent.setup()
      const onRemoveAllParents = vi.fn()
      renderToolbar({ selectedIssueId: 'test-issue-1', onRemoveAllParents })

      await user.click(screen.getByTestId('toolbar-remove-all-parents'))
      expect(onRemoveAllParents).toHaveBeenCalled()
    })
  })

  describe('Move Up/Down buttons', () => {
    it('renders move up button', () => {
      renderToolbar()
      expect(screen.getByTestId('toolbar-move-up')).toBeInTheDocument()
    })

    it('renders move down button', () => {
      renderToolbar()
      expect(screen.getByTestId('toolbar-move-down')).toBeInTheDocument()
    })

    it('disables move up button when canMoveUp is false', () => {
      renderToolbar({ canMoveUp: false })
      expect(screen.getByTestId('toolbar-move-up')).toBeDisabled()
    })

    it('disables move down button when canMoveDown is false', () => {
      renderToolbar({ canMoveDown: false })
      expect(screen.getByTestId('toolbar-move-down')).toBeDisabled()
    })

    it('enables move up button when canMoveUp is true', () => {
      renderToolbar({ canMoveUp: true })
      expect(screen.getByTestId('toolbar-move-up')).not.toBeDisabled()
    })

    it('enables move down button when canMoveDown is true', () => {
      renderToolbar({ canMoveDown: true })
      expect(screen.getByTestId('toolbar-move-down')).not.toBeDisabled()
    })

    it('calls onMoveUp when move up button is clicked', async () => {
      const user = userEvent.setup()
      const onMoveUp = vi.fn()
      renderToolbar({ onMoveUp, canMoveUp: true })

      await user.click(screen.getByTestId('toolbar-move-up'))
      expect(onMoveUp).toHaveBeenCalled()
    })

    it('calls onMoveDown when move down button is clicked', async () => {
      const user = userEvent.setup()
      const onMoveDown = vi.fn()
      renderToolbar({ onMoveDown, canMoveDown: true })

      await user.click(screen.getByTestId('toolbar-move-down'))
      expect(onMoveDown).toHaveBeenCalled()
    })
  })

  describe('Edit button', () => {
    it('renders edit button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /edit issue/i })).toBeInTheDocument()
    })

    it('disables edit button when no issue is selected', () => {
      renderToolbar({ selectedIssueId: null })
      expect(screen.getByRole('button', { name: /edit issue/i })).toBeDisabled()
    })

    it('enables edit button when an issue is selected', () => {
      renderToolbar({ selectedIssueId: 'issue-123' })
      expect(screen.getByRole('button', { name: /edit issue/i })).not.toBeDisabled()
    })

    it('calls onEditIssue when edit button is clicked', async () => {
      const user = userEvent.setup()
      const onEditIssue = vi.fn()
      renderToolbar({ selectedIssueId: 'issue-123', onEditIssue })

      await user.click(screen.getByRole('button', { name: /edit issue/i }))
      expect(onEditIssue).toHaveBeenCalled()
    })
  })

  describe('Assign button', () => {
    it('renders assign button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /assign issue/i })).toBeInTheDocument()
    })

    it('disables assign button when no issue is selected', () => {
      renderToolbar({ selectedIssueId: null })
      expect(screen.getByRole('button', { name: /assign issue/i })).toBeDisabled()
    })

    it('enables assign button when an issue is selected', () => {
      renderToolbar({ selectedIssueId: 'issue-123' })
      expect(screen.getByRole('button', { name: /assign issue/i })).not.toBeDisabled()
    })

    it('calls onAssignIssue when assign button is clicked', async () => {
      const user = userEvent.setup()
      const onAssignIssue = vi.fn()
      renderToolbar({ selectedIssueId: 'issue-123', onAssignIssue })

      await user.click(screen.getByRole('button', { name: /assign issue/i }))
      expect(onAssignIssue).toHaveBeenCalled()
    })
  })

  describe('Agent Run button', () => {
    it('renders agent run button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /run agent/i })).toBeInTheDocument()
    })

    it('calls onOpenAgentLauncher when agent run button is clicked', async () => {
      const user = userEvent.setup()
      const onOpenAgentLauncher = vi.fn()
      renderToolbar({ onOpenAgentLauncher })

      await user.click(screen.getByRole('button', { name: /run agent/i }))
      expect(onOpenAgentLauncher).toHaveBeenCalled()
    })
  })

  describe('Depth controls', () => {
    it('renders depth decrease button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /decrease depth/i })).toBeInTheDocument()
    })

    it('renders depth increase button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /increase depth/i })).toBeInTheDocument()
    })

    it('displays current depth value', () => {
      renderToolbar({ depth: 5 })
      expect(screen.getByText('5')).toBeInTheDocument()
    })

    it('calls onDepthChange with decreased value when decrease button is clicked', async () => {
      const user = userEvent.setup()
      const onDepthChange = vi.fn()
      renderToolbar({ depth: 3, onDepthChange })

      await user.click(screen.getByRole('button', { name: /decrease depth/i }))
      expect(onDepthChange).toHaveBeenCalledWith(2)
    })

    it('calls onDepthChange with increased value when increase button is clicked', async () => {
      const user = userEvent.setup()
      const onDepthChange = vi.fn()
      renderToolbar({ depth: 3, onDepthChange })

      await user.click(screen.getByRole('button', { name: /increase depth/i }))
      expect(onDepthChange).toHaveBeenCalledWith(4)
    })

    it('disables decrease button when depth is 1', () => {
      renderToolbar({ depth: 1 })
      expect(screen.getByRole('button', { name: /decrease depth/i })).toBeDisabled()
    })
  })

  describe('Search input', () => {
    it('renders search input', () => {
      renderToolbar()
      expect(screen.getByRole('searchbox')).toBeInTheDocument()
    })

    it('displays search query value', () => {
      renderToolbar({ searchQuery: 'test search' })
      expect(screen.getByRole('searchbox')).toHaveValue('test search')
    })

    it('calls onSearchChange when search input changes', async () => {
      const user = userEvent.setup()
      const onSearchChange = vi.fn()
      renderToolbar({ onSearchChange })

      await user.type(screen.getByRole('searchbox'), 'hello')
      expect(onSearchChange).toHaveBeenCalled()
    })

    it('displays match count when there are matches', () => {
      renderToolbar({ searchQuery: 'test', searchMatchCount: 5 })
      expect(screen.getByText('5')).toBeInTheDocument()
    })

    it('does not display match count when search is empty', () => {
      renderToolbar({ searchQuery: '', searchMatchCount: 0 })
      // Look for elements that might indicate a count badge
      const badges = screen.queryByTestId('search-match-count')
      expect(badges).toBeNull()
    })
  })

  describe('Filter button', () => {
    it('renders filter button', () => {
      renderToolbar()
      expect(screen.getByRole('button', { name: /filter issues/i })).toBeInTheDocument()
    })

    it('calls onToggleFilter when filter button is clicked', async () => {
      const user = userEvent.setup()
      const onToggleFilter = vi.fn()
      renderToolbar({ onToggleFilter })

      await user.click(screen.getByRole('button', { name: /filter issues/i }))
      expect(onToggleFilter).toHaveBeenCalled()
    })

    it('shows active ring when filterActive is true', () => {
      renderToolbar({ filterActive: true })
      const filterButton = screen.getByRole('button', { name: /filter issues/i })
      expect(filterButton).toHaveClass('ring-2')
    })

    it('shows match count badge when filter is active with query', () => {
      renderToolbar({ filterActive: true, filterQuery: 'status:open', filterMatchCount: 5 })
      expect(screen.getByTestId('filter-match-count')).toHaveTextContent('5')
    })

    it('does not show match count badge when filter query is empty', () => {
      renderToolbar({ filterActive: true, filterQuery: '', filterMatchCount: 5 })
      expect(screen.queryByTestId('filter-match-count')).not.toBeInTheDocument()
    })
  })

  describe('My Tasks button', () => {
    it('renders My Tasks button', () => {
      renderToolbar()
      expect(screen.getByTestId('toolbar-my-tasks-button')).toBeInTheDocument()
    })

    it('calls onApplyDefaultFilter when My Tasks button is clicked', async () => {
      const user = userEvent.setup()
      const onApplyDefaultFilter = vi.fn()
      renderToolbar({ onApplyDefaultFilter })

      await user.click(screen.getByTestId('toolbar-my-tasks-button'))
      expect(onApplyDefaultFilter).toHaveBeenCalled()
    })

    it('shows active ring and aria-pressed when defaultFilterActive is true', () => {
      renderToolbar({ defaultFilterActive: true })
      const button = screen.getByTestId('toolbar-my-tasks-button')
      expect(button).toHaveClass('ring-2')
      expect(button).toHaveAttribute('aria-pressed', 'true')
    })

    it('shows no ring and aria-pressed false when defaultFilterActive is false', () => {
      renderToolbar({ defaultFilterActive: false })
      const button = screen.getByTestId('toolbar-my-tasks-button')
      expect(button).not.toHaveClass('ring-2')
      expect(button).toHaveAttribute('aria-pressed', 'false')
    })
  })

  describe('Filter panel', () => {
    it('does not render filter panel when filterActive is false', () => {
      renderToolbar({ filterActive: false })
      expect(screen.queryByTestId('filter-input')).not.toBeInTheDocument()
    })

    it('renders filter input inline when filterActive is true', () => {
      renderToolbar({ filterActive: true })
      expect(screen.getByTestId('filter-input')).toBeInTheDocument()
    })

    it('renders filter input', () => {
      renderToolbar({ filterActive: true })
      expect(screen.getByTestId('filter-input')).toBeInTheDocument()
    })

    it('displays filter query value', () => {
      renderToolbar({ filterActive: true, filterQuery: 'status:open' })
      expect(screen.getByTestId('filter-input')).toHaveValue('status:open')
    })

    it('calls onFilterChange when filter input changes', async () => {
      const user = userEvent.setup()
      const onFilterChange = vi.fn()
      renderToolbar({ filterActive: true, onFilterChange })

      await user.type(screen.getByTestId('filter-input'), 'test')
      expect(onFilterChange).toHaveBeenCalled()
    })

    it('calls onApplyFilter when Enter is pressed', async () => {
      const user = userEvent.setup()
      const onApplyFilter = vi.fn()
      renderToolbar({ filterActive: true, onApplyFilter })

      const filterInput = screen.getByTestId('filter-input')
      await user.click(filterInput)
      await user.keyboard('{Enter}')
      expect(onApplyFilter).toHaveBeenCalled()
    })

    it('calls onToggleFilter when Escape is pressed', async () => {
      const user = userEvent.setup()
      const onToggleFilter = vi.fn()
      renderToolbar({ filterActive: true, onToggleFilter })

      const filterInput = screen.getByTestId('filter-input')
      await user.click(filterInput)
      await user.keyboard('{Escape}')
      expect(onToggleFilter).toHaveBeenCalled()
    })

    it('renders close button in filter panel', () => {
      renderToolbar({ filterActive: true })
      expect(screen.getByTestId('filter-close-button')).toBeInTheDocument()
    })

    it('calls onToggleFilter when close button is clicked', async () => {
      const user = userEvent.setup()
      const onToggleFilter = vi.fn()
      renderToolbar({ filterActive: true, onToggleFilter })

      await user.click(screen.getByTestId('filter-close-button'))
      expect(onToggleFilter).toHaveBeenCalled()
    })

    it('renders filter help button', () => {
      renderToolbar({ filterActive: true })
      expect(screen.getByTestId('filter-help-button')).toBeInTheDocument()
    })
  })

  describe('Toolbar layout', () => {
    it('has sticky positioning class', () => {
      renderToolbar()
      const toolbar = screen.getByRole('toolbar')
      expect(toolbar).toHaveClass('sticky')
    })

    it('has correct top offset classes for sticky positioning', () => {
      renderToolbar()
      const toolbar = screen.getByRole('toolbar')
      // Should have mobile offset
      expect(toolbar).toHaveClass('top-[4px]')
      // Should have desktop offset
      expect(toolbar).toHaveClass('md:top-[4px]')
      // Should not have the old incorrect offset
      expect(toolbar).not.toHaveClass('top-[68px]')
    })

    it('has horizontal scroll class for mobile', () => {
      renderToolbar()
      const toolbar = screen.getByRole('toolbar')
      expect(toolbar).toHaveClass('overflow-x-auto')
    })
  })
})
