## 1. Format helper

- [x] 1.1 Add a pure helper `formatIssueContextBlock(issue: IssueResponse): string` at `src/Homespun.Web/src/features/agents/lib/format-issue-context-block.ts`. Returns the structured `Issue ID:` / `Title:` / `Description:` block, omitting absent Title/Description lines.
- [x] 1.2 Co-located unit test for: (a) full fields, (b) missing description, (c) missing title (defensive), (d) trims trailing whitespace.

## 2. Shared component — `IssueIdStrip`

- [x] 2.1 Create `src/Homespun.Web/src/features/agents/components/issue-id-strip.tsx`. Props: `{ issueIds: string[] }`. Renders nothing when `issueIds` is empty. Otherwise renders a chip list — one chip per id — each chip containing the short id text plus a `Button` with a lucide `Copy` / `Check` icon swap (2s flash) wired via `useCopyToClipboard`. Use shadcn `Button` (size `sm`, variant `ghost`).
- [x] 2.2 Co-located unit test `issue-id-strip.test.tsx`: (a) renders nothing for empty input, (b) renders one chip per id, (c) clicking the copy button writes the id to the clipboard (mock `navigator.clipboard.writeText`), (d) the icon switches to Check after click then back to Copy after ~2s.

## 3. Hook reuse — `useIssue` integration

- [x] 3.1 In `run-agent-dialog.tsx`, compute `effectiveIssueId = issueId ?? selectedIssueId ?? null`. Call `useIssue(effectiveIssueId ?? '', projectId)` (the hook's `enabled` guard skips the fetch when the id is empty).
- [x] 3.2 Pass the resolved `issue` (or undefined) down to `IssuesAgentTabContent` and `OpenSpecTabContent` via new props so each tab can prefill without re-fetching.

## 4. Dialog wiring — shared strip

- [x] 4.1 Render `<IssueIdStrip issueIds={effectiveIssueId ? [effectiveIssueId] : []} />` in the `DialogHeader` of `RunAgentDialog`, between the existing `DialogTitle` / `DialogDescription` block and the `<Tabs>`.
- [x] 4.2 Style the strip as a single horizontal row consistent with the rest of the dialog header. No "Issue:" label — the chip is self-evident.

## 5. Issues Agent tab — prefill

- [x] 5.1 Add a new optional `issue?: IssueResponse` prop to `IssuesAgentTabContent`. Thread it through from `RunAgentDialog`.
- [x] 5.2 Add a `useEffect` that fires when `issue` becomes defined AND `userInstructions === ''`. When both conditions hold, set `userInstructions` to `formatIssueContextBlock(issue)`.
- [x] 5.3 Update the helper text below the textarea to reflect the prefilled-baseline reality. E.g. when an issue is linked, show "The agent will start with this context. Edit to customize."; when no issue is linked, keep the existing "Leave empty to start an interactive session."

## 6. OpenSpec tab — prefill

- [x] 6.1 Add a new optional `issue?: IssueResponse` prop to `OpenSpecTabContent`. Thread it through from `RunAgentDialog`.
- [x] 6.2 Add the same `useEffect` prefill behaviour as Issues Agent. No change to the existing `handleStart` composition — the prefill becomes part of `userInstructions`, and the existing `parts.filter(...).join('\n\n')` lays `schemaOverride` in front of it for free.

## 7. Task Agent tab — strip-only

- [x] 7.1 No textarea behaviour change. Verify the shared strip renders above the tab content (covered by §4 in the dialog header). No new code in `TaskAgentTabContent`.

## 8. Tests — update existing

- [x] 8.1 `src/features/agents/components/run-agent-dialog.test.tsx`: update the "includes user instructions in the dispatch" assertion (≈L300–328) to expect the prefilled block in the payload when an issue is linked, and to expect the literal user-typed content when no issue is linked. Update the Issues Agent empty-vs-populated branches (≈L414–469) to reflect that prefilled content is non-empty.
- [x] 8.2 `src/features/agents/components/openspec-tab.test.tsx`: update the "prepends schema override to userInstructions" assertion (≈L289–310) to expect `schemaOverride` + prefilled block in order. Update the "dispatches with skill name and change name as arg" assertion (≈L263–287) to expect `userInstructions` to be defined (the prefill) when an issue is linked.

## 9. Tests — new

- [x] 9.1 `run-agent-dialog.test.tsx`: assert the navigation gate stays literal — issue-bound Issues Agent dispatch DOES NOT navigate to the session page (the dialog just closes).
- [x] 9.2 `run-agent-dialog.test.tsx`: assert the strip is rendered in the header when `issueId` or `selectedIssueId` is set, and is NOT rendered when both are absent.
- [x] 9.3 `run-agent-dialog.test.tsx`: assert the Task Agent textarea stays empty (no prefill) when an issue is linked.
- [x] 9.4 `run-agent-dialog.test.tsx`: assert the Issues Agent and OpenSpec textareas are prefilled with the formatted block when an issue is linked.
- [x] 9.5 `run-agent-dialog.test.tsx`: assert first-write-wins — typing into the textarea before `useIssue` resolves preserves the user's input.

## 10. Stories

- [x] 10.1 No existing Storybook story for `run-agent-dialog` or `openspec-tab` exists. If one is added during implementation, include a story showing the prefilled state. Otherwise N/A.

## 11. Pre-PR checks

- [x] 11.1 `dotnet test` (no backend changes expected, but the project's pre-PR checklist requires it).
- [x] 11.2 In `src/Homespun.Web`: `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run build-storybook`.
- [ ] 11.3 Optional manual smoke test in `dev-mock`: verify (a) the strip appears in the header, (b) clicking copy flashes the icon, (c) opening from an issue context prefills the Issues Agent / OpenSpec textareas, (d) opening the Issues Agent button without a node selected hides the strip and leaves the textarea blank.

## 12. Linking and review

- [x] 12.1 Tag fleece issue `rkh0cc` with `openspec=show-issue-description-in-agent-panel` (`fleece edit rkh0cc --tags "openspec=show-issue-description-in-agent-panel"`).
- [x] 12.2 Update issue status to `progress` when starting (`fleece edit rkh0cc -s progress`) and to `review` with `--linked-pr <number>` when opening the PR.
- [x] 12.3 Commit `.fleece/` changes alongside code changes in the same PR.
