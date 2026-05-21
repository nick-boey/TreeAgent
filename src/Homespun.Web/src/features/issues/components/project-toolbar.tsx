import { useRef } from 'react'
import {
  ArrowUpFromLine,
  ArrowDownFromLine,
  ChevronUp,
  ChevronDown,
  CornerRightUp,
  CornerLeftDown,
  Unlink,
  Unlink2,
  Pencil,
  Play,
  Minus,
  Plus,
  Search,
  UserPlus,
  Filter,
  X,
  SquareUser,
  ListTree,
  Network,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ButtonGroup } from '@/components/ui/button-group'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { FilterHelpPopover } from './filter-help-popover'
import { cn } from '@/lib/utils'
import { useMobile } from '@/hooks'
import { ViewMode } from '../types'

export interface ProjectToolbarProps {
  selectedIssueId: string | null
  onCreateAbove: () => void
  onCreateBelow: () => void
  onMakeChild: () => void
  onMakeParent: () => void
  /** Whether the "Make Child Of" button is in active selection mode */
  childOfActive?: boolean
  /** Whether the "Make Parent Of" button is in active selection mode */
  parentOfActive?: boolean
  /** Callback when Remove Parent is clicked (enters selection mode) */
  onRemoveParent?: () => void
  /** Whether the "Remove Parent" button is in active selection mode */
  removeParentActive?: boolean
  /** Callback when Remove All Parents is clicked (immediate action) */
  onRemoveAllParents?: () => void
  /** Callback when Move Up is clicked */
  onMoveUp?: () => void
  /** Callback when Move Down is clicked */
  onMoveDown?: () => void
  /** Whether the selected issue can move up among its siblings */
  canMoveUp?: boolean
  /** Whether the selected issue can move down among its siblings */
  canMoveDown?: boolean
  onEditIssue: () => void
  onOpenAgentLauncher: () => void
  onAssignIssue: () => void
  depth: number
  onDepthChange: (depth: number) => void
  searchQuery: string
  onSearchChange: (query: string) => void
  searchMatchCount: number
  onNextMatch: () => void
  onPreviousMatch: () => void
  onEmbedSearch: () => void
  /** Whether the filter panel is active/visible */
  filterActive?: boolean
  /** Current filter query string */
  filterQuery?: string
  /** Toggle filter panel visibility */
  onToggleFilter?: () => void
  /** Called when filter input changes */
  onFilterChange?: (query: string) => void
  /** Called when filter should be applied (Enter key) */
  onApplyFilter?: () => void
  /** Number of issues matching the current filter */
  filterMatchCount?: number
  /** Ref to the filter input for focus management */
  filterInputRef?: React.RefObject<HTMLInputElement | null>
  /** Called when "My Tasks" button is clicked to apply default filters */
  onApplyDefaultFilter?: () => void
  /** Whether the default filter (My Tasks) is currently active */
  defaultFilterActive?: boolean
  /** Current view mode (next or tree) */
  viewMode?: ViewMode
  /** Called when view mode changes */
  onViewModeChange?: (mode: ViewMode) => void
}

export function ProjectToolbar({
  selectedIssueId,
  onCreateAbove,
  onCreateBelow,
  onMakeChild,
  onMakeParent,
  childOfActive = false,
  parentOfActive = false,
  onRemoveParent,
  removeParentActive = false,
  onRemoveAllParents,
  onMoveUp,
  onMoveDown,
  canMoveUp = false,
  canMoveDown = false,
  onEditIssue,
  onOpenAgentLauncher,
  onAssignIssue,
  depth,
  onDepthChange,
  searchQuery,
  onSearchChange,
  searchMatchCount,
  filterActive = false,
  filterQuery = '',
  onToggleFilter,
  onFilterChange,
  onApplyFilter,
  filterMatchCount,
  filterInputRef,
  onApplyDefaultFilter,
  defaultFilterActive = false,
  viewMode = ViewMode.Tree,
  onViewModeChange,
}: ProjectToolbarProps) {
  const isMobile = useMobile()
  const internalFilterInputRef = useRef<HTMLInputElement>(null)
  const actualFilterInputRef = filterInputRef ?? internalFilterInputRef

  const hasIssueSelected = selectedIssueId !== null

  // Use touch-friendly sizes on mobile
  const buttonSize = isMobile ? 'icon-touch' : 'icon-sm'

  const handleFilterKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault()
      onApplyFilter?.()
    } else if (e.key === 'Escape') {
      e.preventDefault()
      onToggleFilter?.()
    }
  }

  return (
    <div
      role="toolbar"
      aria-label="Issue management toolbar"
      className={cn(
        'sticky top-[4px] z-10 md:top-[4px]',
        'flex items-center gap-2 overflow-x-auto',
        'border-border bg-background/80 rounded-lg border px-2 backdrop-blur-sm',
        // More padding on mobile for touch
        isMobile ? 'py-2' : 'py-1.5',
        'scrollbar-thin scrollbar-thumb-muted scrollbar-track-transparent'
      )}
    >
      {/* Creation buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onCreateAbove}
          aria-label="Create above (Shift+O)"
          title="Create above (Shift+O)"
        >
          <ArrowUpFromLine className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onCreateBelow}
          aria-label="Create below (O)"
          title="Create below (O)"
        >
          <ArrowDownFromLine className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Hierarchy buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMakeChild}
          disabled={!hasIssueSelected}
          aria-label="Make child of another (click to select target)"
          title="Make child of another (click to select target)"
          className={cn(childOfActive && 'ring-ring ring-2')}
        >
          <CornerRightUp className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMakeParent}
          disabled={!hasIssueSelected}
          aria-label="Make parent of another (click to select target)"
          title="Make parent of another (click to select target)"
          className={cn(parentOfActive && 'ring-ring ring-2')}
        >
          <CornerLeftDown className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onRemoveParent}
          disabled={!hasIssueSelected}
          aria-label="Remove parent (click to select parent to remove)"
          title="Remove parent (click to select parent to remove)"
          data-testid="toolbar-remove-parent"
          className={cn(removeParentActive && 'ring-ring ring-2')}
        >
          <Unlink className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onRemoveAllParents}
          disabled={!hasIssueSelected}
          aria-label="Remove all parents"
          title="Remove all parents"
          data-testid="toolbar-remove-all-parents"
        >
          <Unlink2 className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Move Up/Down buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMoveUp}
          disabled={!canMoveUp}
          aria-label="Move up (Ctrl+Shift+Up)"
          title="Move up (Ctrl+Shift+Up)"
          data-testid="toolbar-move-up"
        >
          <ChevronUp className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMoveDown}
          disabled={!canMoveDown}
          aria-label="Move down (Ctrl+Shift+Down)"
          title="Move down (Ctrl+Shift+Down)"
          data-testid="toolbar-move-down"
        >
          <ChevronDown className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Edit and Assign buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onEditIssue}
          disabled={!hasIssueSelected}
          aria-label="Edit issue"
          title="Edit issue"
        >
          <Pencil className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onAssignIssue}
          disabled={!hasIssueSelected}
          aria-label="Assign issue"
          title="Assign issue"
          data-testid="toolbar-assign-issue"
        >
          <UserPlus className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Agent button */}
      <Button
        variant="outline"
        size={buttonSize}
        onClick={onOpenAgentLauncher}
        aria-label="Run agent (e)"
        title="Run agent (e)"
        data-testid="toolbar-run-agent"
      >
        <Play className="h-4 w-4" />
      </Button>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Filter button */}
      <div className="relative">
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onToggleFilter}
          aria-label="Filter issues (f)"
          title="Filter issues (f)"
          aria-pressed={filterActive}
          data-testid="toolbar-filter-button"
          className={cn(filterActive && 'ring-ring ring-2')}
        >
          <Filter className="h-4 w-4" />
        </Button>
        {filterActive && filterMatchCount !== undefined && filterQuery && (
          <span
            data-testid="filter-match-count"
            className="bg-primary text-primary-foreground absolute -top-2 -right-2 flex h-5 min-w-5 items-center justify-center rounded-full px-1 text-[10px] font-medium"
          >
            {filterMatchCount}
          </span>
        )}
      </div>

      {/* My Tasks button */}
      <Button
        variant="outline"
        size={buttonSize}
        onClick={onApplyDefaultFilter}
        aria-label="My Tasks"
        title="My Tasks - Apply default filters"
        aria-pressed={defaultFilterActive}
        data-testid="toolbar-my-tasks-button"
        className={cn(defaultFilterActive && 'ring-ring ring-2')}
      >
        <SquareUser className="h-4 w-4" />
      </Button>

      {/* Inline filter input (when active) */}
      {filterActive && (
        <>
          <Input
            ref={actualFilterInputRef}
            type="text"
            placeholder={isMobile ? 'Filter...' : 'Filter issues (e.g., status:open type:bug)...'}
            value={filterQuery}
            onChange={(e) => onFilterChange?.(e.target.value)}
            onKeyDown={handleFilterKeyDown}
            className={cn('min-w-[200px] flex-1', isMobile ? 'h-11' : 'h-8')}
            aria-label="Filter query"
            data-testid="filter-input"
          />
          <FilterHelpPopover />
          <Button
            variant="ghost"
            size={buttonSize}
            onClick={onToggleFilter}
            aria-label="Close filter"
            title="Close filter (Escape)"
            data-testid="filter-close-button"
          >
            <X className="h-4 w-4" />
          </Button>
        </>
      )}

      {/* Spacer */}
      <div className="flex-1" />

      {/* View mode toggle */}
      <Button
        variant="outline"
        size={buttonSize}
        onClick={() =>
          onViewModeChange?.(viewMode === ViewMode.Next ? ViewMode.Tree : ViewMode.Next)
        }
        aria-label={
          viewMode === ViewMode.Tree
            ? 'Tree view (click to switch to Next view)'
            : 'Next view (click to switch to Tree view)'
        }
        title={
          viewMode === ViewMode.Tree
            ? 'Tree view (click to switch to Next view)'
            : 'Next view (click to switch to Tree view)'
        }
        data-testid="toolbar-view-mode-toggle"
      >
        {viewMode === ViewMode.Tree ? (
          <ListTree className="h-4 w-4" />
        ) : (
          <Network className="h-4 w-4" />
        )}
      </Button>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Depth controls (right side) */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => onDepthChange(depth - 1)}
          disabled={depth <= 1}
          aria-label="Decrease depth ([)"
          title="Decrease depth ([)"
        >
          <Minus className="h-4 w-4" />
        </Button>
        <span className="border-border text-muted-foreground bg-background flex h-8 min-w-6 items-center justify-center border-y text-center text-sm">
          {depth}
        </span>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => onDepthChange(depth + 1)}
          aria-label="Increase depth (])"
          title="Increase depth (])"
        >
          <Plus className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Search input (right side) */}
      <div className="relative flex items-center">
        <Search className="text-muted-foreground absolute left-2 h-4 w-4" aria-hidden="true" />
        <Input
          type="search"
          role="searchbox"
          placeholder={isMobile ? 'Search...' : 'Search issues (/)...'}
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className={cn(
            'pr-8 pl-8',
            // Touch-friendly height on mobile
            isMobile ? 'h-11 w-[140px]' : 'h-8 w-[180px]'
          )}
          aria-label="Search issues"
        />
        {searchQuery && searchMatchCount > 0 && (
          <span
            data-testid="search-match-count"
            className="bg-muted text-muted-foreground absolute right-2 rounded px-1.5 py-0.5 text-xs"
          >
            {searchMatchCount}
          </span>
        )}
      </div>
    </div>
  )
}
