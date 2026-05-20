## Context

The Run Agent dialog (`RunAgentDialog` in `src/Homespun.Web/src/features/agents/components/run-agent-dialog.tsx`) is the single user-facing entry point for starting an agent session on an issue. It hosts three tabs:

- **Task Agent**: skill-driven dispatch via `POST /api/issues/{id}/run`. Always scoped to a single `issueId`. Textarea labelled "Additional instructions" — semantically additive on top of the skill's SKILL.md system prompt.
- **Issues Agent**: skill-less free-text dispatch via `POST /api/issues-agent/sessions`. Scoped via `selectedIssueId` (may be unset). Textarea is the *primary* signal to the model.
- **OpenSpec**: skill-driven dispatch with auto-selected OpenSpec stage skill. Always scoped to an `issueId` (the tab is only mounted when `issueId` is set). Textarea is layered after a schema-override system phrase at send time.

Two issue-id props flow in from parents:

- `issueId` — set when the dialog is opened from the issue-edit "Save & Run Agent" button or a row-level "Run Agent" action.
- `selectedIssueId` — set when the dialog is opened from the issues graph with a node selected.

Neither tab today fetches the issue itself. The Issues Agent and OpenSpec tabs dispatch with no information beyond what the server can resolve from the issue id, leaving the textarea blank.

`useIssue(issueId, projectId)` already exists at `src/features/issues/hooks/use-issue.ts` and returns `{ issue, isLoading, ... }`. `useCopyToClipboard` already exists at `src/components/tool-ui/shared/use-copy-to-clipboard.ts` and exposes a `copy(text, id?)` plus a `copiedId` flash state. Both are reused.

## Goals / Non-Goals

**Goals:**
- Make the dialog self-explanatory about which issue it's about to operate on.
- Reduce friction in launching the Issues Agent and OpenSpec tabs by prefilling the textarea with structured issue context.
- Provide a quick copy-to-clipboard for the issue id from any tab.
- Keep the markup shape ready for a future multi-issue selection in the Issues Agent tab.

**Non-Goals:**
- Multi-issue selection itself. (Markup is chip-list-shaped; behaviour is single-chip.)
- Any backend, dispatch-payload contract, or server-side prompt-composition change.
- Changes to the Task Agent tab's textarea content (it remains blank).
- Changes to the OpenSpec tab's skill list, auto-selection, gating, or schema-override semantics.
- A redesign of post-dispatch navigation behaviour. The existing rule is preserved; its observable behaviour changes are documented as accepted.

## Decisions

### D1. New `run-agent-panel` capability instead of modifying `agent-dispatch`

**Choice:** The dialog-chrome behaviour is captured in a NEW capability spec `run-agent-panel` rather than added to `agent-dispatch` or to the existing OpenSpec-tab requirement in `openspec-integration`.

**Rationale:** `agent-dispatch` is about server-side dispatch decisions plus the top-bar active-agents indicator; the dialog UX is a distinct concern. `openspec-integration` describes OpenSpec-specific semantics, which are orthogonal to the chrome the dialog wraps around them. A dedicated capability keeps each spec focused and makes future dialog-UX changes easy to scope.

### D2. Effective issue id resolution

**Choice:** `effectiveIssueId = issueId ?? selectedIssueId ?? null`. The strip renders ⇔ `effectiveIssueId !== null`. The prefill is driven by the same value.

**Rationale:** When the dialog opens from an issue context, `issueId` is set. When it opens from a graph selection (via the "Issues Agent" button), only `selectedIssueId` is set; falling through to it is the only way the Issues Agent tab gets context in that flow. We do not attempt to repair the pre-existing Task-Agent oddity (using only `selectedIssueId` results in dispatching with `issueId === ''`); that's a separate concern.

### D3. Shared header strip vs. duplicated per-tab strips

**Choice:** Single shared `IssueIdStrip` rendered once in the dialog header, above `<Tabs>`.

**Alternatives considered:**
- Render the strip inside each `TabsContent` so each tab "owns" its chrome → three copies of the same UI, harder to keep in sync.

**Rationale:** The spec says "shown in all three tabs"; a shared header strip satisfies that with one source of truth in the markup. It also visually communicates that the issue context is constant across tabs.

### D4. Chip-list-shaped markup, one chip today

**Choice:** The strip is rendered as a list of chips, with one chip populated. The chip displays the short id and a copy button.

**Rationale:** The Issues Agent tab already accepts an optional `selectedIssueId`; a plausible future iteration extends this to a set. By shaping the markup as a list now, that extension is purely additive — no restructure required.

### D5. Prefill format: structured key/value block

**Choice:** The textarea is prefilled with:

```
Issue ID: {id}
Title: {title}
Description: {description}
```

Lines for absent fields are omitted (e.g. an issue with no description renders only Issue ID + Title).

**Alternatives considered:**
- Markdown-style prose ("This session is for issue rkh0cc, titled …") → noisier, harder to parse, no advantage.
- Inject as a read-only context block above the textarea, leaving the textarea empty for additions → cleaner UX but requires the dispatch path to know how to layer two strings; bypasses the chosen "prefill is just text in the textarea" model.
- Prefill description only → loses the unambiguous ID anchor inside the model's context.

**Rationale:** Structured key/value reads cleanly, parses unambiguously for the model, and is easy for the human to scan and edit. The "first-write-wins" rule (D6) ensures the user's own edits are never overwritten.

### D6. First-write-wins: prefill only when textarea is empty

**Choice:** The prefill effect fires only when `userInstructions === ''` AND the issue has resolved. If the user types anything before the issue resolves, the prefill never overwrites it.

**Rationale:** Avoids the surprising case where a slow network connection clobbers the user's in-progress edits when the response finally arrives.

### D7. Loading state: chip appears immediately, prefill swaps in

**Choice:** The strip renders the id chip as soon as `effectiveIssueId` is known (we have it as a prop, no fetch required). The textarea prefill is deferred until `useIssue` resolves successfully.

**Rationale:** The id is available synchronously; rendering it immediately gives the user the ID + copy affordance with zero delay. The description block needs the fetched issue, so it gets a small natural delay.

### D8. Issue fetch failure: chip renders, no prefill

**Choice:** If `useIssue` resolves with an error, the strip still renders (the id is real and copyable), but the textarea stays empty. No error toast, no inline error chrome.

**Rationale:** Failure of an enrichment fetch should not block the primary flow. The user can still dispatch the agent with manually-typed instructions.

### D9. Task Agent tab is exempt from prefill

**Choice:** Task Agent's textarea remains a blank "Additional instructions" surface. Only Issues Agent and OpenSpec receive the prefill.

**Rationale:** The Task Agent tab is paired with a skill picker. Its textarea is semantically *additive* on top of the skill's SKILL.md system prompt; prefilling it with issue context would fight with the skill's own framing. Both Issues Agent and OpenSpec tabs treat the textarea as the primary free-form signal, where the prefill adds rather than conflicts.

### D10. Issues Agent navigation gate: preserved literally

**Choice:** The existing `IssuesAgentTabContent.handleStart` rule is kept exactly:

```ts
if (!userInstructions.trim()) {
  navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
}
onOpenChange(false)
```

With prefill in effect, the textarea is always populated when an issue is linked, so the navigation branch never fires in that case. The dialog closes without navigating; the user remains on the originating page.

**Alternatives considered:**
- Track a `userHasEdited` flag and treat unedited prefill as "empty" for the navigation gate → would preserve the existing UX feel but adds state and obscures the dispatch contract.
- Always navigate when an issue is linked → simpler, takes the user to the session, but removes the "background dispatch" affordance.

**Rationale:** The user has explicitly chosen the literal interpretation ("only navigate when there are no user instructions at all"). The observable consequence is that issue-bound Issues Agent dispatches now behave as "fire-and-forget" — the dialog closes and the user remains on the originating page. The agent still runs; the session is reachable from the sidebar session list (see the `move-sessions-to-sidebar` change). Documented here so future readers don't mistake this for an oversight.

### D11. OpenSpec schema-override layering with prefill

**Choice:** The OpenSpec tab's existing `handleStart` continues to prepend `schemaOverride` (when applicable) before the user's textarea content, joined by `\n\n`. With prefill, the final `userInstructions` payload becomes:

```
{schemaOverride}        ← only when the project's schema is non-default
\n\n
Issue ID: {id}
Title: {title}
Description: {description}
\n\n
{user's additions}      ← only if the user appended any
```

**Rationale:** No code change needed beyond the prefill itself. The existing `parts.filter(...).join('\n\n')` composition handles the layering for free.

### D12. Reset on each dialog open

**Choice:** The prefill applies on every open. `RunAgentDialog` already returns `null` when `open === false`, so its children unmount and remount on each open — internal state (including the prefilled textarea) is naturally reset.

**Rationale:** Each open is a fresh launch context. A user who closes the dialog and reopens it expects to see the current canonical state, not a half-edited draft from a previous attempt.

## Risks / Trade-offs

- **[Risk] Slow `useIssue` resolution leaves the textarea empty for a beat after the dialog opens.**
  → Mitigation: the id chip renders immediately so the user has something to anchor to. The textarea fills in within one round-trip (typically <100ms for a local server, sub-second on a cold cache). Acceptable.

- **[Trade-off] Issue-bound Issues Agent dispatch now defaults to background.**
  → Documented in D10. The sidebar session list (the `move-sessions-to-sidebar` change, in flight in parallel) provides the discoverability that compensates for not auto-navigating.

- **[Risk] `userHasEdited` semantics are not tracked, so a user who clears the textarea after prefill (perhaps wanting to chat interactively) DOES trigger navigation.**
  → Acceptable. "Clear the textarea" is a reasonable user action that signals "I don't want this context, just open the chat".

- **[Risk] Future multi-issue selection on the Issues Agent tab will need the prefill rule to compose multiple issues' contexts.**
  → The chip-list-shaped markup already supports this; the prefill rule would need a small extension (e.g. concatenate per-issue blocks). Not blocking this change.

- **[Risk] Drift between the `Copy` flash UX here and the established `inline-issue-detail-row.tsx` flash UX.**
  → Both use the same `useCopyToClipboard` hook + lucide `Copy`/`Check` icons + 2s flash. Drift would be a regression in either site, not a new inconsistency introduced here.

## Open Questions

None blocking. Implementation-level details are codified above.
