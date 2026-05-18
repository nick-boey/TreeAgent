## ADDED Requirements

### Requirement: Run Agent dialog shows an issue context strip when an issue is linked

The Run Agent dialog SHALL render an "issue context strip" in its header, above the tab list, whenever at least one issue is linked to the open dialog.

The set of linked issues SHALL be derived from the dialog's props as: `effectiveIssueIds = compact([issueId ?? selectedIssueId])` — at most one issue today, but the strip's markup SHALL be shaped to accommodate a list.

The strip SHALL render one chip per linked issue. Each chip SHALL show the short issue id text and a button that copies the id to the clipboard.

The copy button SHALL flash visual confirmation (Copy → Check icon swap) for approximately two seconds on success, using the `useCopyToClipboard` hook and the lucide icon pattern established elsewhere in the application.

The strip SHALL be visible on all three tabs (Task Agent, Issues Agent, OpenSpec) because it lives in the shared dialog header above the `<Tabs>`.

#### Scenario: Dialog opened with `issueId` shows the strip
- **WHEN** the dialog is opened with `issueId = "rkh0cc"`
- **THEN** the strip SHALL render one chip showing "rkh0cc" with a copy button
- **AND** the chip SHALL be visible regardless of which tab is active

#### Scenario: Dialog opened with only `selectedIssueId` shows the strip
- **WHEN** the dialog is opened with `issueId = undefined` and `selectedIssueId = "rkh0cc"`
- **THEN** the strip SHALL render one chip showing "rkh0cc"

#### Scenario: Dialog opened with neither `issueId` nor `selectedIssueId` hides the strip
- **WHEN** the dialog is opened with `issueId = undefined` and `selectedIssueId = null`
- **THEN** the strip SHALL NOT render

#### Scenario: Copy button writes the id to the clipboard and flashes confirmation
- **WHEN** the user clicks the copy button on a chip
- **THEN** the clipboard SHALL receive the chip's id
- **AND** the button SHALL render a Check icon for approximately two seconds before reverting to the Copy icon

### Requirement: Issues Agent and OpenSpec tabs prefill their textarea with structured issue context

When an issue is linked to the open dialog AND the loaded issue data has resolved, the Issues Agent tab and the OpenSpec tab SHALL prefill their "Additional instructions" textarea with a structured block:

```
Issue ID: {id}
Title: {title}
Description: {description}
```

The prefill SHALL fire only when the textarea is empty at the moment the issue data resolves (first-write-wins). It SHALL NOT overwrite a non-empty textarea.

Lines for absent fields SHALL be omitted. For example, an issue with no description SHALL produce a two-line block containing only Issue ID and Title.

The prefilled content SHALL be fully editable by the user.

The Task Agent tab SHALL NOT receive this prefill; its textarea remains a blank "Additional instructions" surface.

#### Scenario: Issues Agent tab prefills on issue resolution
- **WHEN** the dialog is opened on the Issues Agent tab with `selectedIssueId = "rkh0cc"`
- **AND** the issue has title "Show issue description in input of agent run panel" and a non-empty description
- **THEN** the textarea SHALL render the three-line structured block (Issue ID + Title + Description) once `useIssue` resolves

#### Scenario: OpenSpec tab prefills on issue resolution
- **WHEN** the dialog is opened on the OpenSpec tab with `issueId = "rkh0cc"`
- **AND** the issue has title and description
- **THEN** the textarea SHALL render the three-line structured block once `useIssue` resolves

#### Scenario: Task Agent tab does not prefill
- **WHEN** the dialog is opened on the Task Agent tab with `issueId = "rkh0cc"`
- **THEN** the textarea SHALL remain empty
- **AND** its placeholder "Additional instructions (optional)" SHALL be visible

#### Scenario: Prefill does not overwrite user-typed content
- **WHEN** the user types "custom instructions" into the Issues Agent textarea
- **AND** `useIssue` subsequently resolves with the issue data
- **THEN** the textarea SHALL still contain "custom instructions"
- **AND** the prefill block SHALL NOT be applied

#### Scenario: Issue with no description omits the Description line
- **WHEN** the issue has title but no description
- **THEN** the prefill block SHALL contain only "Issue ID: {id}" and "Title: {title}"
- **AND** no "Description:" line SHALL appear

#### Scenario: Issue fetch failure leaves the textarea empty
- **WHEN** the dialog is opened with an issue id whose fetch fails
- **THEN** the textarea SHALL remain empty
- **AND** the strip SHALL still render the id chip
- **AND** no error toast or inline error SHALL be shown in the dialog

### Requirement: OpenSpec schema override composes with prefill at send time

When the OpenSpec tab's `handleStart` dispatches a session, the `userInstructions` payload SHALL be composed as `[schemaOverride, textareaContent].filter(Boolean).join('\n\n')`, where `schemaOverride` is the existing `"use openspec schema '{schema}' for all openspec commands"` phrase (included only for non-default schemas) and `textareaContent` is the current value of the OpenSpec tab's textarea (which includes the prefilled block plus any user edits).

#### Scenario: Non-default schema prepends override before the prefill
- **WHEN** the project uses schema "custom-schema"
- **AND** the textarea contains the prefilled three-line block
- **THEN** the dispatched `userInstructions` SHALL be `"use openspec schema 'custom-schema' for all openspec commands\n\nIssue ID: rkh0cc\nTitle: ...\nDescription: ..."`

#### Scenario: Default schema dispatches the prefill content as-is
- **WHEN** the project uses the default schema
- **AND** the textarea contains the prefilled three-line block
- **THEN** the dispatched `userInstructions` SHALL be the three-line block exactly

### Requirement: Issues Agent post-dispatch navigation gate is unchanged

The Issues Agent tab's `handleStart` SHALL navigate to the freshly-created session's page IFF the textarea is empty at the moment of dispatch. Otherwise it SHALL close the dialog without navigating.

This rule is preserved literally despite the prefill. With prefilled content populated by default for issue-linked launches, the navigation branch does not fire for those launches and the dialog closes without navigation. The agent still runs; the session is reachable from the sidebar's session list.

#### Scenario: Empty textarea navigates to the session
- **WHEN** the user clicks Start on the Issues Agent tab with an empty textarea (no issue linked, or the user cleared the prefill)
- **THEN** the session SHALL be created
- **AND** the dialog SHALL close
- **AND** the user SHALL be navigated to `/sessions/{sessionId}`

#### Scenario: Issue-bound dispatch closes the dialog without navigating
- **WHEN** the user clicks Start on the Issues Agent tab with the prefilled three-line block present (unedited or edited but non-empty)
- **THEN** the session SHALL be created
- **AND** the dialog SHALL close
- **AND** the user SHALL remain on the originating page
- **AND** no navigation to `/sessions/{sessionId}` SHALL occur

### Requirement: Dialog state resets on each open

Each open of the Run Agent dialog SHALL produce a fresh state — including a fresh prefill — rather than restoring text from a previously-closed open.

#### Scenario: Reopening the dialog re-prefills the textarea
- **WHEN** the user types "additional context" into the Issues Agent textarea, then closes the dialog
- **AND** subsequently reopens the dialog with the same `selectedIssueId`
- **THEN** the textarea SHALL contain only the prefilled block
- **AND** "additional context" SHALL NOT persist
