/**
 * Golden-fixture cross-stack diff: runs the TS port against each `*.input.json`
 * under `tests/Homespun.Web.LayoutFixtures/fixtures/` and asserts the emitted
 * envelope matches the reference `*.expected.json` produced by Fleece.Core's C#
 * implementation.
 *
 * If this test fails after a Fleece.Core upgrade or a deliberate change to the
 * algorithm, regenerate the expected fixtures with:
 *   UPDATE_FIXTURES=1 dotnet test tests/Homespun.Web.LayoutFixtures
 * and update this port to match.
 */

import { describe, it, expect } from 'vitest'
import * as fs from 'node:fs'
import * as path from 'node:path'
import { fileURLToPath } from 'node:url'
import { IssueLayoutService, InvalidGraphError } from './issue-layout-service'
import type {
  ExecutionMode,
  IssueStatus,
  LayoutIssue,
  ParentIssueRef,
} from './issue-layout-service'
import type { LayoutNode } from './nodes'
import type { Edge, GraphLayout, GraphLayoutResult, InactiveVisibility } from './types'

// __dirname is .../src/Homespun.Web/src/features/issues/services/layout
// Repo root is 7 levels up.
const FIXTURES_DIR = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../../../../../../../tests/Homespun.Web.LayoutFixtures/fixtures'
)

interface FixtureInput {
  mode?: 'Tree' | 'Next'
  visibility?: InactiveVisibility | 'Hide' | 'IfHasActiveDescendants' | 'Always'
  assignedTo?: string | null
  matchedIds?: string[]
  issues: LayoutIssueRaw[]
}

interface LayoutIssueRaw {
  id: string
  title?: string | null
  description?: string | null
  status: string
  type?: string
  executionMode?: string
  parentIssues?: ParentIssueRefRaw[]
  priority?: number | null
  assignedTo?: string | null
  createdAt?: string
}

interface ParentIssueRefRaw {
  parentIssue: string
  /** Fleece 3.1 wire-format name. */
  lexOrder?: string
  /** Legacy 3.0 wire-format name; still read for transitional compatibility. */
  sortOrder?: string
  active?: boolean
}

interface FixtureNode {
  id: string
  row: number
  lane: number
  appearanceIndex: number
  totalAppearances: number
}

interface FixtureEdge {
  fromId: string
  toId: string
  kind: string
  startRow: number
  startLane: number
  endRow: number
  endLane: number
  pivotLane?: number
  sourceAttach: string
  targetAttach: string
}

interface FixtureSuccessOutput {
  ok: true
  totalRows: number
  totalLanes: number
  nodes: FixtureNode[]
  edges: FixtureEdge[]
}

interface FixtureCycleOutput {
  ok: false
  cycle: string[]
}

type FixtureOutput = FixtureSuccessOutput | FixtureCycleOutput

const lc = (s: string): string => s.toLowerCase()

const normalizeIssue = (raw: LayoutIssueRaw): LayoutIssue => ({
  id: raw.id,
  title: raw.title ?? null,
  description: raw.description ?? null,
  status: lc(raw.status) as IssueStatus,
  executionMode: raw.executionMode ? (lc(raw.executionMode) as ExecutionMode) : 'series',
  parentIssues:
    raw.parentIssues?.map<ParentIssueRef>((p) => ({
      parentIssue: p.parentIssue,
      sortOrder: p.lexOrder ?? p.sortOrder ?? null,
      active: p.active ?? true,
    })) ?? [],
  priority: raw.priority ?? null,
  assignedTo: raw.assignedTo ?? null,
  createdAt: raw.createdAt ?? null,
})

const normalizeVisibility = (v: FixtureInput['visibility']): InactiveVisibility => {
  if (!v) return 'hide'
  const lower = lc(v)
  if (lower === 'hide') return 'hide'
  if (lower === 'always') return 'always'
  if (lower === 'ifhasactivedescendants') return 'ifHasActiveDescendants'
  return v as InactiveVisibility
}

const toFixtureOutput = (
  result: GraphLayoutResult<LayoutNode> | InvalidGraphError
): FixtureOutput => {
  if (result instanceof InvalidGraphError) {
    return { ok: false, cycle: [...result.cycle] }
  }
  if (!result.ok) {
    return { ok: false, cycle: [...result.cycle] }
  }
  const layout: GraphLayout<LayoutNode> = result.layout
  return {
    ok: true,
    totalRows: layout.totalRows,
    totalLanes: layout.totalLanes,
    nodes: layout.nodes.map((n) => ({
      id: n.node.id,
      row: n.row,
      lane: n.lane,
      appearanceIndex: n.appearanceIndex,
      totalAppearances: n.totalAppearances,
    })),
    edges: layout.edges.map((e: Edge<LayoutNode>) => {
      const out: FixtureEdge = {
        fromId: e.from.id,
        toId: e.to.id,
        kind: e.kind,
        startRow: e.startRow,
        startLane: e.startLane,
        endRow: e.endRow,
        endLane: e.endLane,
        sourceAttach: e.sourceAttach,
        targetAttach: e.targetAttach,
      }
      // Match C# JsonIgnoreCondition.WhenWritingNull: omit the field when null.
      if (e.pivotLane !== null) out.pivotLane = e.pivotLane
      return out
    }),
  }
}

const runFixture = (input: FixtureInput): FixtureOutput => {
  const service = new IssueLayoutService()
  const issues = input.issues.map(normalizeIssue)
  const visibility = normalizeVisibility(input.visibility)
  const assignedTo = input.assignedTo ?? null
  try {
    if (input.mode === 'Next') {
      const matched =
        input.matchedIds && input.matchedIds.length > 0 ? new Set(input.matchedIds) : null
      return toFixtureOutput(service.layoutForNext(issues, matched, visibility, assignedTo, null))
    }
    return toFixtureOutput(service.layoutForTree(issues, visibility, assignedTo, null))
  } catch (err) {
    if (err instanceof InvalidGraphError) {
      return toFixtureOutput(err)
    }
    throw err
  }
}

const fixtureNames = (): string[] => {
  if (!fs.existsSync(FIXTURES_DIR)) return []
  return fs
    .readdirSync(FIXTURES_DIR)
    .filter((f) => f.endsWith('.input.json'))
    .map((f) => f.replace('.input.json', ''))
    .sort()
}

describe('Golden fixtures (TS port matches Fleece.Core reference)', () => {
  const names = fixtureNames()
  it('discovers at least 10 fixtures', () => {
    expect(names.length).toBeGreaterThanOrEqual(10)
  })

  for (const name of names) {
    it(`fixture ${name} matches reference`, () => {
      const inputPath = path.join(FIXTURES_DIR, `${name}.input.json`)
      const expectedPath = path.join(FIXTURES_DIR, `${name}.expected.json`)
      const input = JSON.parse(fs.readFileSync(inputPath, 'utf8')) as FixtureInput
      const expected = JSON.parse(fs.readFileSync(expectedPath, 'utf8')) as FixtureOutput
      const actual = runFixture(input)
      expect(actual).toEqual(expected)
    })
  }
})
