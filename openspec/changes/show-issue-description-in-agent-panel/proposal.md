## Why

When the Run Agent dialog is opened from an issue context, the user today sees no indication of *which* issue they're about to operate on and no information about what the issue is. The Issues Agent and OpenSpec tabs in particular show a blank "Additional instructions" textarea that requires the user to either remember the issue, switch tabs in the browser to look it up, or trust the agent to figure it out server-side. This adds friction for the common case (launching an agent on the issue you were just looking at) and produces ambiguous prompts in the rarer case (launching the Issues Agent against a graph-selected issue without manually re-stating context).

Surfacing the issue ID (with a copy affordance) consistently across all three tabs, and prefilling the description into the two free-form tabs, makes the dialog self-explanatory and reduces re-typing.

## What Changes

- **Shared "Issue Context" strip** rendered in the Run Agent dialog header, above the tabs. Visible on Task Agent, Issues Agent, and OpenSpec tabs. Hidden when no issue is linked.
- The strip renders the effective issue id (resolution rule: `issueId ?? selectedIssueId ?? null`) as a single chip with a copy-to-clipboard button. Markup is shaped as a chip list so a future multi-issue selection drops into it without restructure, but renders one chip today.
- The copy button reuses the existing `useCopyToClipboard` hook and the established lucide `Copy` → `Check` icon flash (2s) pattern.
- **Issues Agent tab** prefills its textarea with a structured Issue ID / Title / Description block on first render after the issue resolves, only when the textarea is empty. Lines for absent fields (e.g. no description) are omitted. The block is fully editable.
- **OpenSpec tab** applies the same prefill behaviour. The existing schema-override prepending continues to apply at send time, layered before the textarea content.
- **Task Agent tab** textarea is unchanged — it remains a blank "Additional instructions" surface alongside the skill picker. The shared ID strip still appears above it.
- **Issues Agent navigation gate** is preserved literally: post-dispatch navigation fires only when the textarea is empty at send time. With prefill in effect, issue-bound Issues Agent dispatches now behave as "fire-and-forget" — the dialog closes without navigating. The session remains reachable from the sidebar's session list. This is an explicit, documented acceptance of the existing rule applied to the new content baseline.

## Capabilities

### New Capabilities
- `run-agent-panel`: the Run Agent dialog's per-tab and shared chrome behaviour — issue-context strip, textarea prefill rules, navigation gate.

### Modified Capabilities
None. The existing `openspec-integration` requirement "OpenSpec tab in run-agent panel" continues to describe OpenSpec dispatch semantics (skill list, auto-selection, gating, schema override); the new dialog-chrome requirements are defined in the new `run-agent-panel` capability and are orthogonal.

## Impact

**Affected code (frontend only — no backend changes):**
- `src/Homespun.Web/src/features/agents/components/run-agent-dialog.tsx` — resolve `effectiveIssueId`, mount the new strip in the dialog header, wire `useIssue` once and pass through to the Issues Agent tab content, add prefill to the Issues Agent tab.
- `src/Homespun.Web/src/features/agents/components/openspec-tab.tsx` — accept an `issue` prop and apply the same prefill.
- New: `src/Homespun.Web/src/features/agents/components/issue-id-strip.tsx` — chip-list-shaped strip component with copy affordance. Co-located unit test.
- New (optional, lightweight): `src/Homespun.Web/src/features/agents/lib/format-issue-context-block.ts` — pure helper for the structured block. Co-located unit test.
- Tests: update `run-agent-dialog.test.tsx` (assertions on `userInstructions` payload shape and Issues Agent empty-vs-populated branches) and `openspec-tab.test.tsx` (schema-override composition and the empty-instructions branch).

**No impact on:**
- Backend (`Homespun.Server`, `Homespun.Worker`, `Homespun.Shared`).
- The `POST /api/issues/{id}/run` or `POST /api/issues-agent/sessions` payload contracts (both still receive `userInstructions`; only its content changes).
- The OpenSpec tab's existing skill semantics, auto-selection, gating, or schema-override behaviour.
- The Task Agent tab's behaviour beyond the appearance of the shared strip.
- Existing Playwright e2e specs — none assert textarea default content today.

**Linked work:**
- Fleece issue `rkh0cc` ("Show issue description in input of agent run panel"), tagged `openspec=show-issue-description-in-agent-panel`.
