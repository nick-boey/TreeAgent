import { useState, useEffect, useMemo, useCallback } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { Play, ChevronDown, ChevronUp, AlertCircle, ExternalLink } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { Loader } from '@/components/ui/loader'
import { Label } from '@/components/ui/label'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { useAvailableModels, useRunAgent } from '../hooks'
import { isAgentConflictError } from '../hooks/use-run-agent'
import { normalizeStoredModel } from '../lib/normalize-stored-model'
import { formatIssueContextBlock } from '../lib/format-issue-context-block'
import { useProject } from '@/features/projects'
import { BaseBranchSelector } from './base-branch-selector'
import { OpenSpecTabContent } from './openspec-tab'
import { IssueIdStrip } from './issue-id-strip'
import { useCreateIssuesAgentSession } from '@/features/issues-agent/hooks/use-create-issues-agent-session'
import { SkillPicker } from '@/features/skills'
import { useProjectSkills } from '@/features/skills/hooks/use-project-skills'
import { useIssue } from '@/features/issues/hooks/use-issue'
import type { RunAgentResult } from '../hooks/use-run-agent'
import type { CreateIssuesAgentSessionResult } from '@/features/issues-agent/hooks/use-create-issues-agent-session'
import type { ClaudeModelInfo, IssueResponse } from '@/api/generated/types.gen'
import { SessionMode, SkillCategory } from '@/api'

// localStorage keys
const TASK_MODEL_STORAGE_KEY = 'agent-launcher-model'
const TASK_SKILL_STORAGE_KEY = 'agent-launcher-skill'
const TASK_BASE_BRANCH_STORAGE_KEY = 'agent-launcher-base-branch'
const TASK_MODE_STORAGE_KEY = 'agent-launcher-mode'
const ISSUES_MODEL_STORAGE_KEY = 'issues-agent-model'
const ISSUES_MODE_STORAGE_KEY = 'issues-agent-mode'

export interface RunAgentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
  issueId?: string
  selectedIssueId?: string | null
  defaultTab?: 'task' | 'issues' | 'openspec'
  onAgentStart?: (result: RunAgentResult) => void
  onSessionCreated?: (result: CreateIssuesAgentSessionResult) => void
  onError?: (error: Error) => void
}

/**
 * Unified dialog for launching both Task Agent and Issues Agent sessions.
 *
 * Task Agent dispatches via an optional Homespun skill (skills-catalogue).
 * Issues Agent is skill-less — it takes free-text instructions only.
 */
export function RunAgentDialog({
  open,
  onOpenChange,
  projectId,
  issueId,
  selectedIssueId,
  defaultTab,
  onAgentStart,
  onSessionCreated,
  onError,
}: RunAgentDialogProps) {
  const computedDefaultTab = defaultTab ?? (issueId ? 'task' : 'issues')
  const [activeTab, setActiveTab] = useState<string>(computedDefaultTab)

  const [lastDefault, setLastDefault] = useState(computedDefaultTab)
  if (computedDefaultTab !== lastDefault) {
    setLastDefault(computedDefaultTab)
    setActiveTab(computedDefaultTab)
  }

  const effectiveIssueId = issueId ?? selectedIssueId ?? null
  const { issue } = useIssue(effectiveIssueId ?? '', projectId)

  if (!open) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex h-[80vh] w-[80vw] max-w-none flex-col overflow-hidden sm:max-w-none">
        <DialogHeader>
          <DialogTitle>Run Agent</DialogTitle>
          <DialogDescription>Configure and start an agent session</DialogDescription>
          <IssueIdStrip issueIds={effectiveIssueId ? [effectiveIssueId] : []} />
        </DialogHeader>

        <Tabs
          value={activeTab}
          onValueChange={setActiveTab}
          className="flex flex-1 flex-col overflow-hidden"
        >
          <TabsList variant="line">
            <TabsTrigger value="task">Task Agent</TabsTrigger>
            <TabsTrigger value="issues">Issues Agent</TabsTrigger>
            {effectiveIssueId ? <TabsTrigger value="openspec">OpenSpec</TabsTrigger> : null}
          </TabsList>

          <TabsContent
            value="task"
            forceMount
            data-testid="task-tab-content"
            className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
          >
            <TaskAgentTabContent
              projectId={projectId}
              issueId={issueId ?? ''}
              onAgentStart={onAgentStart}
              onOpenChange={onOpenChange}
              onError={onError}
            />
          </TabsContent>

          <TabsContent
            value="issues"
            forceMount
            data-testid="issues-tab-content"
            className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
          >
            <IssuesAgentTabContent
              projectId={projectId}
              selectedIssueId={selectedIssueId}
              issue={issue}
              onSessionCreated={onSessionCreated}
              onOpenChange={onOpenChange}
              onError={onError}
            />
          </TabsContent>

          {effectiveIssueId ? (
            <TabsContent
              value="openspec"
              forceMount
              data-testid="openspec-tab-content"
              className="flex flex-1 flex-col overflow-hidden data-[state=inactive]:hidden"
            >
              <OpenSpecTabContent
                projectId={projectId}
                issueId={effectiveIssueId}
                issue={issue}
                onAgentStart={onAgentStart}
                onOpenChange={onOpenChange}
                onError={onError}
              />
            </TabsContent>
          ) : null}
        </Tabs>
      </DialogContent>
    </Dialog>
  )
}

// ============================================================================
// Task Agent Tab
// ============================================================================

interface TaskAgentTabContentProps {
  projectId: string
  issueId: string
  onAgentStart?: (result: RunAgentResult) => void
  onOpenChange: (open: boolean) => void
  onError?: (error: Error) => void
}

function TaskAgentTabContent({
  projectId,
  issueId,
  onAgentStart,
  onOpenChange,
  onError,
}: TaskAgentTabContentProps) {
  const navigate = useNavigate()

  const { project } = useProject(projectId)
  const { data: skillsData } = useProjectSkills(projectId)
  const { models: availableModels, defaultModel, isLoading: modelsLoading } = useAvailableModels()

  const [selectedModel, setSelectedModel] = useState<string | null>(null)

  // Adjusted during render (vs. in an effect) to avoid cascading renders.
  if (selectedModel === null && availableModels.length > 0) {
    const normalized = normalizeStoredModel(
      localStorage.getItem(TASK_MODEL_STORAGE_KEY),
      availableModels,
      defaultModel
    )
    if (normalized) setSelectedModel(normalized)
  }

  const [selectedSkillName, setSelectedSkillName] = useState<string | null>(
    () => localStorage.getItem(TASK_SKILL_STORAGE_KEY) || null
  )
  const [skillArgs, setSkillArgs] = useState<Record<string, string>>({})
  const [selectedBaseBranch, setSelectedBaseBranch] = useState<string>(
    () => localStorage.getItem(TASK_BASE_BRANCH_STORAGE_KEY) ?? ''
  )
  const [selectedMode, setSelectedMode] = useState<SessionMode>(() => {
    const stored = localStorage.getItem(TASK_MODE_STORAGE_KEY)
    return stored === SessionMode.BUILD ? SessionMode.BUILD : SessionMode.PLAN
  })
  const [showAdvancedSettings, setShowAdvancedSettings] = useState(false)
  const [userInstructions, setUserInstructions] = useState('')

  const [conflictSessionId, setConflictSessionId] = useState<string | null>(null)

  const effectiveBaseBranch = useMemo(
    () => selectedBaseBranch || project?.defaultBranch || '',
    [selectedBaseBranch, project?.defaultBranch]
  )

  const runAgent = useRunAgent()

  // Persist selections
  useEffect(() => {
    if (selectedModel) {
      localStorage.setItem(TASK_MODEL_STORAGE_KEY, selectedModel)
    }
  }, [selectedModel])

  useEffect(() => {
    if (selectedSkillName) {
      localStorage.setItem(TASK_SKILL_STORAGE_KEY, selectedSkillName)
    } else {
      localStorage.removeItem(TASK_SKILL_STORAGE_KEY)
    }
  }, [selectedSkillName])

  useEffect(() => {
    if (selectedBaseBranch) {
      localStorage.setItem(TASK_BASE_BRANCH_STORAGE_KEY, selectedBaseBranch)
    }
  }, [selectedBaseBranch])

  useEffect(() => {
    localStorage.setItem(TASK_MODE_STORAGE_KEY, selectedMode)
  }, [selectedMode])

  // Sync mode to the selected skill's declared mode. Adjusted during render (vs. in an
  // effect) to avoid cascading renders. Triggers once per selectedSkillName change — covers
  // user-driven skill picks and the initial-mount case where selectedSkillName is restored
  // from localStorage and skillsData loads asynchronously.
  const [lastSyncedSkillName, setLastSyncedSkillName] = useState<string | null>(null)
  if (selectedSkillName && selectedSkillName !== lastSyncedSkillName && skillsData?.homespun) {
    setLastSyncedSkillName(selectedSkillName)
    const skill = skillsData.homespun.find((s) => s.name === selectedSkillName)
    if (skill?.mode) {
      setSelectedMode(skill.mode)
    }
  }

  const handleStart = useCallback(async () => {
    if (!selectedModel) return
    setConflictSessionId(null)

    try {
      const nonEmptyArgs = Object.fromEntries(
        Object.entries(skillArgs).filter(([, v]) => v.trim().length > 0)
      )

      const result = await runAgent.mutateAsync({
        issueId,
        projectId,
        mode: selectedMode,
        model: selectedModel,
        baseBranch: effectiveBaseBranch || undefined,
        userInstructions: userInstructions.trim() || undefined,
        skillName: selectedSkillName ?? undefined,
        skillArgs: Object.keys(nonEmptyArgs).length > 0 ? nonEmptyArgs : undefined,
      })

      onAgentStart?.(result)
      onOpenChange(false)
    } catch (e) {
      if (isAgentConflictError(e)) {
        setConflictSessionId(e.sessionId)
        return
      }
      onError?.(e as Error)
    }
  }, [
    runAgent,
    issueId,
    projectId,
    selectedMode,
    selectedModel,
    effectiveBaseBranch,
    userInstructions,
    selectedSkillName,
    skillArgs,
    onAgentStart,
    onOpenChange,
    onError,
  ])

  const handleOpenExistingSession = useCallback(() => {
    if (conflictSessionId) {
      navigate({ to: '/sessions/$sessionId', params: { sessionId: conflictSessionId } })
      onOpenChange(false)
    }
  }, [conflictSessionId, navigate, onOpenChange])

  const isStarting = runAgent.isPending
  const isBusy = isStarting || modelsLoading || !selectedModel

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-y-auto py-4">
      {/* Conflict state */}
      {conflictSessionId && (
        <div className="flex flex-col gap-3 rounded-md border border-amber-500/50 bg-amber-500/10 p-4">
          <div className="flex items-center gap-2">
            <AlertCircle className="h-5 w-5 flex-shrink-0 text-amber-600 dark:text-amber-400" />
            <div>
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                Agent already running
              </p>
              <p className="text-xs text-amber-700 dark:text-amber-300">
                An agent session is already active on this issue.
              </p>
            </div>
          </div>
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5 self-start"
            onClick={handleOpenExistingSession}
          >
            <ExternalLink className="h-3.5 w-3.5" />
            Open Existing Session
          </Button>
        </div>
      )}

      {/* Skill picker with dynamic args */}
      <SkillPicker
        projectId={projectId}
        category={SkillCategory.HOMESPUN}
        selectedSkillName={selectedSkillName}
        onSkillChange={(name) => {
          setSelectedSkillName(name)
          setSkillArgs({})
        }}
        argValues={skillArgs}
        onArgValuesChange={setSkillArgs}
        disabled={isStarting}
      />

      {/* Main controls row */}
      <div className="flex flex-wrap items-center gap-2">
        <ModeSelector value={selectedMode} onValueChange={setSelectedMode} disabled={isStarting} />

        <ModelSelect
          value={selectedModel}
          onValueChange={setSelectedModel}
          models={availableModels}
          disabled={isStarting || modelsLoading}
          className="w-44"
        />

        <Button
          size="sm"
          onClick={handleStart}
          disabled={isBusy}
          className="w-full gap-1.5 sm:w-auto"
        >
          {runAgent.isPending ? (
            <Loader variant="circular" size="sm" />
          ) : (
            <Play className="h-3.5 w-3.5" />
          )}
          Start Agent
        </Button>
      </div>

      {/* Instructions textarea */}
      <Textarea
        placeholder="Additional instructions (optional)"
        value={userInstructions}
        onChange={(e) => setUserInstructions(e.target.value)}
        disabled={isStarting}
        className="min-h-32 flex-1 resize-none overflow-y-auto"
      />

      {/* Advanced settings */}
      <Collapsible open={showAdvancedSettings} onOpenChange={setShowAdvancedSettings}>
        <CollapsibleTrigger asChild>
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground hover:text-foreground gap-1 p-0 text-xs"
          >
            {showAdvancedSettings ? (
              <>
                <ChevronUp className="h-3 w-3" />
                Hide settings
              </>
            ) : (
              <>
                <ChevronDown className="h-3 w-3" />
                More settings
              </>
            )}
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent className="mt-3 space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="base-branch" className="text-sm">
              Base Branch
            </Label>
            <BaseBranchSelector
              repoPath={project?.localPath ?? undefined}
              defaultBranch={project?.defaultBranch}
              value={effectiveBaseBranch}
              onChange={setSelectedBaseBranch}
              disabled={isStarting}
              aria-label="Select base branch"
            />
            <p className="text-muted-foreground text-xs">
              The branch to create the working branch from
            </p>
          </div>
        </CollapsibleContent>
      </Collapsible>
    </div>
  )
}

// ============================================================================
// Issues Agent Tab (skill-less)
// ============================================================================

interface IssuesAgentTabContentProps {
  projectId: string
  selectedIssueId?: string | null
  issue?: IssueResponse
  onSessionCreated?: (result: CreateIssuesAgentSessionResult) => void
  onOpenChange: (open: boolean) => void
  onError?: (error: Error) => void
}

function IssuesAgentTabContent({
  projectId,
  selectedIssueId,
  issue,
  onSessionCreated,
  onOpenChange,
  onError,
}: IssuesAgentTabContentProps) {
  const navigate = useNavigate()
  const createSession = useCreateIssuesAgentSession()
  const { models: availableModels, defaultModel, isLoading: modelsLoading } = useAvailableModels()

  const [selectedModel, setSelectedModel] = useState<string | null>(null)

  if (selectedModel === null && availableModels.length > 0) {
    const normalized = normalizeStoredModel(
      localStorage.getItem(ISSUES_MODEL_STORAGE_KEY),
      availableModels,
      defaultModel
    )
    if (normalized) setSelectedModel(normalized)
  }

  const [selectedMode, setSelectedMode] = useState<SessionMode>(() => {
    const stored = localStorage.getItem(ISSUES_MODE_STORAGE_KEY)
    return stored === SessionMode.PLAN ? SessionMode.PLAN : SessionMode.BUILD
  })

  const [userInstructions, setUserInstructions] = useState('')

  useEffect(() => {
    if (selectedModel) {
      localStorage.setItem(ISSUES_MODEL_STORAGE_KEY, selectedModel)
    }
  }, [selectedModel])

  useEffect(() => {
    localStorage.setItem(ISSUES_MODE_STORAGE_KEY, selectedMode)
  }, [selectedMode])

  // Prefill textarea with issue context when the issue resolves, first-write-wins.
  useEffect(() => {
    if (!issue) return
    setUserInstructions((prev) => (prev === '' ? formatIssueContextBlock(issue) : prev))
  }, [issue])

  const handleStart = useCallback(async () => {
    if (!selectedModel) return
    try {
      const result = await createSession.mutateAsync({
        projectId,
        model: selectedModel,
        selectedIssueId: selectedIssueId ?? undefined,
        userInstructions: userInstructions.trim() || undefined,
        mode: selectedMode,
      })

      onSessionCreated?.(result)

      if (!userInstructions.trim()) {
        navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
      }

      onOpenChange(false)
    } catch (e) {
      onError?.(e as Error)
    }
  }, [
    createSession,
    projectId,
    selectedModel,
    selectedIssueId,
    userInstructions,
    selectedMode,
    onSessionCreated,
    navigate,
    onOpenChange,
    onError,
  ])

  const hasIssue = !!issue

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-hidden py-4">
      <div className="flex flex-wrap items-center gap-2">
        <ModeSelector
          value={selectedMode}
          onValueChange={setSelectedMode}
          disabled={createSession.isPending}
        />

        <ModelSelect
          value={selectedModel}
          onValueChange={setSelectedModel}
          models={availableModels}
          disabled={createSession.isPending || modelsLoading}
          className="w-44"
        />

        <Button
          size="sm"
          onClick={handleStart}
          disabled={createSession.isPending || modelsLoading || !selectedModel}
          className="w-full gap-1.5 sm:w-auto"
        >
          {createSession.isPending ? (
            <Loader variant="circular" size="sm" />
          ) : (
            <Play className="h-3.5 w-3.5" />
          )}
          Start Agent
        </Button>
      </div>

      <Textarea
        placeholder="Additional instructions (optional)"
        value={userInstructions}
        onChange={(e) => setUserInstructions(e.target.value)}
        disabled={createSession.isPending}
        className="flex-1 resize-none overflow-y-auto"
      />

      <p className="text-muted-foreground text-xs">
        {hasIssue
          ? 'The agent will start with this context. Edit to customize.'
          : userInstructions.trim()
            ? 'The agent will start with these instructions.'
            : 'Leave empty to start an interactive session.'}
      </p>
    </div>
  )
}

// ============================================================================
// Shared Components
// ============================================================================

interface ModeSelectorProps {
  value: SessionMode
  onValueChange: (value: SessionMode) => void
  disabled: boolean
}

function ModeSelector({ value, onValueChange, disabled }: ModeSelectorProps) {
  return (
    <Select
      value={value}
      onValueChange={(v) => onValueChange(v as SessionMode)}
      disabled={disabled}
    >
      <SelectTrigger className="w-24" aria-label="Select mode">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={SessionMode.PLAN}>Plan</SelectItem>
        <SelectItem value={SessionMode.BUILD}>Build</SelectItem>
      </SelectContent>
    </Select>
  )
}

interface ModelSelectProps {
  value: string | null
  onValueChange: (value: string) => void
  models: readonly ClaudeModelInfo[]
  disabled: boolean
  className?: string
}

function ModelSelect({ value, onValueChange, models, disabled, className }: ModelSelectProps) {
  return (
    <Select value={value ?? ''} onValueChange={onValueChange} disabled={disabled || !value}>
      <SelectTrigger className={className} aria-label="Select model">
        <SelectValue placeholder="Loading…" />
      </SelectTrigger>
      <SelectContent>
        {models.map((model) =>
          model.id ? (
            <SelectItem key={model.id} value={model.id}>
              {model.displayName ?? model.id}
            </SelectItem>
          ) : null
        )}
      </SelectContent>
    </Select>
  )
}
