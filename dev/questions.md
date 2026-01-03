# Implementation Questions

Questions to clarify before implementing the PR workflow feature.

## 1. Rebase conflict handling

When automatic rebasing fails due to conflicts, what should happen?
- Mark the PR with a special "Conflict" status?
- Spawn an agent to attempt resolution?
- Just notify the user and wait?

## 2. Closed PRs in the timeline

Should closed (not merged) PRs be included in the `t` ordering, or excluded entirely from the timeline? The spec shows them as red/past, but it's unclear if they get assigned negative `t` values.

## 3. Thread scope

Is a thread meant to:
- Link a single feature across past → current → future (like one PR per thread), or
- Group multiple related PRs together (like an epic containing several PRs)?

## 4. Plan-update PR blocking

When you say plan-update PRs "must be merged before other PRs" - should this be:
- Enforced (block merge buttons for non-plan PRs), or
- Advisory (warning/recommendation only)?

## 5. Agent completion detection

How should the system determine an agent has finished work and the PR should transition to "Ready for Review"?
- Agent process exits cleanly?
- Specific message/signal from Claude Code?
- User manually marks it?

## 6. Multiple t=1 PR priority

When several PRs are open simultaneously, is there a priority order for which gets reviewed/merged first, or is that purely user discretion?
