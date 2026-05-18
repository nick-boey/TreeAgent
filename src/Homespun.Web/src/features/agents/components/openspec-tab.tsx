import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { AlertCircle, ExternalLink, Lock, Play, Sparkles } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Loader } from '@/components/ui/loader'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'
import { useProjectSkills } from '@/features/skills/hooks/use-project-skills'
import { useTaskGraph } from '@/features/issues/hooks/use-task-graph'
import { useProject } from '@/features/projects'
import { isAgentConflictError, useRunAgent } from '../hooks/use-run-agent'
import { formatIssueContextBlock } from '../lib/format-issue-context-block'
import type { RunAgentResult } from '../hooks/use-run-agent'
import { BaseBranchSelector } from './base-branch-selector'
import { ChangePhase, SessionMode } from '@/api'
import type { IssueOpenSpecState, IssueResponse, SkillDescriptor } from '@/api/generated/types.gen'

const MODELS = [
  { value: 'opus', label: 'Opus' },
  { value: 'sonnet', label: 'Sonnet' },
  { value: 'haiku', label: 'Haiku' },
] as const

const MODEL_STORAGE_KEY = 'openspec-tab-model'
const BASE_BRANCH_STORAGE_KEY = 'openspec-tab-base-branch'
const DEFAULT_SCHEMA = 'spec-driven'

/**
 * Ordered list of OpenSpec skills surfaced in the tab. Order drives display and
 * readiness gating; see `GATED_SKILLS`.
 */
const OPENSPEC_SKILL_ORDER = [
  'openspec-explore',
  'openspec-propose',
  'openspec-new-change',
  'openspec-continue-change',
  'openspec-apply-change',
  'openspec-verify-change',
  'openspec-sync-specs',
  'openspec-archive-change',
] as const

type OpenSpecSkillName = (typeof OPENSPEC_SKILL_ORDER)[number]

/** Skills that are blocked until prerequisite OpenSpec state is reached. */
const GATED_SKILLS: ReadonlySet<OpenSpecSkillName> = new Set([
  'openspec-apply-change',
  'openspec-verify-change',
  'openspec-sync-specs',
  'openspec-archive-change',
])

export interface OpenSpecTabContentProps {
  projectId: string
  issueId: string
  issue?: IssueResponse
  onAgentStart?: (result: RunAgentResult) => void
  onOpenChange: (open: boolean) => void
  onError?: (error: Error) => void
}

/**
 * Determines which skill is the default suggestion for a given change state.
 * - No change or incomplete artifacts -> explore
 * - Artifacts complete -> apply
 * - Tasks complete (or archived) -> archive
 */
export function autoSelectOpenSpecSkill(state: IssueOpenSpecState | null | undefined): string {
  const phase = state?.changeState ?? ChangePhase.NONE
  switch (phase) {
    case ChangePhase.READY_TO_APPLY:
      return 'openspec-apply-change'
    case ChangePhase.READY_TO_ARCHIVE:
    case ChangePhase.ARCHIVED:
      return 'openspec-archive-change'
    default:
      return 'openspec-explore'
  }
}

/**
 * Whether a gated skill's prerequisites are met for the current change state.
 * Non-gated skills (`explore`, `propose`, `new-change`, `continue-change`) are
 * always considered ready.
 */
export function isSkillReady(
  skillName: string,
  state: IssueOpenSpecState | null | undefined
): boolean {
  if (!GATED_SKILLS.has(skillName as OpenSpecSkillName)) return true
  const phase = state?.changeState ?? ChangePhase.NONE
  switch (skillName) {
    case 'openspec-apply-change':
      return phase === ChangePhase.READY_TO_APPLY || phase === ChangePhase.READY_TO_ARCHIVE
    case 'openspec-verify-change':
    case 'openspec-sync-specs':
    case 'openspec-archive-change':
      return phase === ChangePhase.READY_TO_ARCHIVE || phase === ChangePhase.ARCHIVED
    default:
      return true
  }
}

/**
 * Computes the schema override system-prompt phrase for non-default schemas.
 * Returns null when the schema is the default ("spec-driven") or unknown.
 */
export function buildSchemaOverride(schemaName: string | null | undefined): string | null {
  if (!schemaName || schemaName === DEFAULT_SCHEMA) return null
  return `use openspec schema '${schemaName}' for all openspec commands`
}

/**
 * OpenSpec tab for the Run Agent dialog. Lists the fixed set of OpenSpec
 * skills, auto-selects the next ready stage based on the issue's change state,
 * gates skills whose prerequisites aren't met, and dispatches via
 * {@link useRunAgent} so the server composes the SKILL.md body + change name.
 */
export function OpenSpecTabContent({
  projectId,
  issueId,
  issue,
  onAgentStart,
  onOpenChange,
  onError,
}: OpenSpecTabContentProps) {
  const navigate = useNavigate()
  const { project } = useProject(projectId)
  const { data: skillsData } = useProjectSkills(projectId)
  const { taskGraph } = useTaskGraph(projectId)

  const openSpecState = useMemo<IssueOpenSpecState | null>(() => {
    return taskGraph?.openSpecStates?.[issueId] ?? null
  }, [taskGraph, issueId])

  const orderedSkills = useMemo<SkillDescriptor[]>(() => {
    const discovered = skillsData?.openSpec ?? []
    const byName = new Map(discovered.map((s) => [s.name ?? '', s]))
    const ordered: SkillDescriptor[] = []
    for (const name of OPENSPEC_SKILL_ORDER) {
      const skill = byName.get(name)
      if (skill) ordered.push(skill)
    }
    return ordered
  }, [skillsData])

  const suggestedSkill = useMemo(() => autoSelectOpenSpecSkill(openSpecState), [openSpecState])

  const [selectedSkill, setSelectedSkill] = useState<string>(suggestedSkill)

  // Re-auto-select when the change state changes (e.g. artifacts completed).
  const [lastSuggestion, setLastSuggestion] = useState(suggestedSkill)
  if (suggestedSkill !== lastSuggestion) {
    setLastSuggestion(suggestedSkill)
    setSelectedSkill(suggestedSkill)
  }

  const [selectedModel, setSelectedModel] = useState<string>(
    () => localStorage.getItem(MODEL_STORAGE_KEY) ?? MODELS[0].value
  )
  const [selectedBaseBranch, setSelectedBaseBranch] = useState<string>(
    () => localStorage.getItem(BASE_BRANCH_STORAGE_KEY) ?? ''
  )
  const [userInstructions, setUserInstructions] = useState('')
  const [conflictSessionId, setConflictSessionId] = useState<string | null>(null)

  useEffect(() => {
    localStorage.setItem(MODEL_STORAGE_KEY, selectedModel)
  }, [selectedModel])

  useEffect(() => {
    if (selectedBaseBranch) {
      localStorage.setItem(BASE_BRANCH_STORAGE_KEY, selectedBaseBranch)
    }
  }, [selectedBaseBranch])

  // Prefill textarea with issue context when the issue resolves, first-write-wins.
  useEffect(() => {
    if (!issue) return
    setUserInstructions((prev) => (prev === '' ? formatIssueContextBlock(issue) : prev))
  }, [issue])

  const effectiveBaseBranch = useMemo(
    () => selectedBaseBranch || project?.defaultBranch || '',
    [selectedBaseBranch, project?.defaultBranch]
  )

  const runAgent = useRunAgent()
  const isStarting = runAgent.isPending

  const selectedReady = isSkillReady(selectedSkill, openSpecState)
  const changeName = openSpecState?.changeName ?? null

  const schemaOverride = useMemo(
    () => buildSchemaOverride(openSpecState?.schemaName),
    [openSpecState?.schemaName]
  )

  const handleStart = useCallback(async () => {
    if (!selectedReady) return
    setConflictSessionId(null)

    // Compose user instructions: schema override (if any) + user input.
    const parts: string[] = []
    if (schemaOverride) parts.push(schemaOverride)
    const trimmed = userInstructions.trim()
    if (trimmed) parts.push(trimmed)
    const finalInstructions = parts.join('\n\n') || undefined

    const skillArgs: Record<string, string> = {}
    if (changeName) skillArgs.change = changeName

    try {
      const result = await runAgent.mutateAsync({
        issueId,
        projectId,
        // OpenSpec dispatches use full build access so skills can read/write
        // change artifacts. Plan mode would block artifact creation.
        mode: SessionMode.BUILD,
        model: selectedModel,
        baseBranch: effectiveBaseBranch || undefined,
        userInstructions: finalInstructions,
        skillName: selectedSkill,
        skillArgs: Object.keys(skillArgs).length > 0 ? skillArgs : undefined,
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
    selectedReady,
    schemaOverride,
    userInstructions,
    changeName,
    runAgent,
    issueId,
    projectId,
    selectedModel,
    effectiveBaseBranch,
    selectedSkill,
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

  const selectedDescription = orderedSkills.find((s) => s.name === selectedSkill)?.description

  return (
    <div className="flex flex-1 flex-col gap-4 overflow-y-auto py-4">
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

      <div className="flex flex-col gap-2" data-testid="openspec-change-state">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">Change:</span>
          <span className="font-mono">{changeName ?? '— no change linked —'}</span>
          {openSpecState?.schemaName ? (
            <span className="text-muted-foreground text-xs">
              schema: {openSpecState.schemaName}
            </span>
          ) : null}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <Select
          value={selectedSkill}
          onValueChange={setSelectedSkill}
          disabled={isStarting || orderedSkills.length === 0}
        >
          <SelectTrigger className="w-full" aria-label="Select OpenSpec skill">
            <SelectValue placeholder="Select OpenSpec skill" />
          </SelectTrigger>
          <SelectContent>
            {orderedSkills.map((skill) => {
              const name = skill.name ?? ''
              const ready = isSkillReady(name, openSpecState)
              const isSuggested = name === suggestedSkill
              return (
                <SelectItem key={name} value={name} disabled={!ready}>
                  <span className={cn('flex items-center gap-2', !ready && 'opacity-60')}>
                    {!ready ? <Lock className="h-3 w-3" aria-label="Blocked" /> : null}
                    {isSuggested ? (
                      <Sparkles className="h-3 w-3 text-amber-500" aria-label="Recommended" />
                    ) : null}
                    <span>{name}</span>
                  </span>
                </SelectItem>
              )
            })}
          </SelectContent>
        </Select>
        {selectedDescription ? (
          <p className="text-muted-foreground text-xs">{selectedDescription}</p>
        ) : null}
        {!selectedReady ? (
          <p
            className="text-xs text-amber-600 dark:text-amber-400"
            data-testid="openspec-blocked-reason"
          >
            Prerequisites not met for this skill. Pick another or advance the change first.
          </p>
        ) : null}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Select value={selectedModel} onValueChange={setSelectedModel} disabled={isStarting}>
          <SelectTrigger className="w-24" aria-label="Select model">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MODELS.map((model) => (
              <SelectItem key={model.value} value={model.value}>
                {model.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button
          size="sm"
          onClick={handleStart}
          disabled={isStarting || !selectedReady}
          className="w-full gap-1.5 sm:w-auto"
          data-testid="openspec-start-agent"
        >
          {isStarting ? <Loader variant="circular" size="sm" /> : <Play className="h-3.5 w-3.5" />}
          Start Agent
        </Button>
      </div>

      <Textarea
        placeholder="Additional instructions (optional)"
        value={userInstructions}
        onChange={(e) => setUserInstructions(e.target.value)}
        disabled={isStarting}
        className="min-h-32 flex-1 resize-none overflow-y-auto"
      />

      <div className="space-y-1.5">
        <span className="text-sm font-medium">Base Branch</span>
        <BaseBranchSelector
          repoPath={project?.localPath ?? undefined}
          defaultBranch={project?.defaultBranch}
          value={effectiveBaseBranch}
          onChange={setSelectedBaseBranch}
          disabled={isStarting}
          aria-label="Select base branch"
        />
      </div>
    </div>
  )
}
