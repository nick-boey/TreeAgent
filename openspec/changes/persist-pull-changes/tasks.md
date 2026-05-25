## 1. Pre-implementation spike

- [x] 1.1 ~~Verify Fleece.Core tolerates a `meta.follows` pointer to a change-id that has been compacted away.~~ **Done during proposal.** Result: Fleece.Cli 3.1 tolerates dangling `follows` pointers (verified empirically — `fleece list` and `fleece edit` both succeed). No fallback rule needed.
- [x] 1.2 ~~Confirm the on-disk format of `.fleece/.active-change`.~~ **Done during proposal.** Result: it's a JSON object `{"guid":"<hex>"}`, not a bare hex string. Spec, design, and tasks have been updated to reflect this.

## 2. Failing tests first (TDD red)

- [x] 2.1 Add integration test `GitCloneService_DoesNotInheritActiveChange` in `tests/Homespun.Api.Tests` (or equivalent integration test home) that creates a temp git repo, seeds `.fleece/.active-change` with a known `{"guid":"..."}` JSON value on main, invokes `IGitCloneService.CreateCloneAsync`, and asserts the clone's `.active-change` parses as JSON and its `guid` field differs from main's.
- [x] 2.2 Add `GitCloneService_DoesNotInheritReplayCache` asserting the clone's `.replay-cache` is absent post-creation.
- [x] 2.3 Add `GitCloneService_BootstrapsFreshChangeFileWithFollowsPointer` covering: latest-mtime selection, GUID format, and the `meta.follows` first-line content.
- [x] 2.4 Add `GitCloneService_BootstrapsRootMetaWhenChangesDirEmpty` asserting the `follows` field is omitted when no existing change files are present.
- [x] 2.5 Add `GitCloneService_TwoClonesFromSameMainBootstrapDistinctChanges` asserting both clones get unique GUIDs and a shared `follows` pointer to the same parent.
- [x] 2.6 Add `FleeceIssuesSyncService_PullAutosavesUncommittedFleeceChanges` driving a temp repo where `.fleece/changes/change_X.jsonl` has uncommitted edits, then invoking `PullFleeceOnlyAsync` and asserting a `chore(fleece): pre-pull autosave [skip ci]` commit exists immediately before the merge commit.
- [x] 2.7 Add `FleeceIssuesSyncService_PullDoesNotAutosaveCleanState` asserting no autosave commit is created when `.fleece/` is clean.
- [x] 2.8 Add `FleeceIssuesSyncService_PullAutosaveScopeIsFleeceOnly` driving a repo with both `.fleece/changes/change_X.jsonl` and `README.md` dirty, asserting the autosave commit's tree contains only the `.fleece/` change and that `HasNonFleeceChanges`/`NonFleeceChangedFiles` are reported as today.
- [x] 2.9 Add end-to-end `UncommittedUserEditSurvivesPullOfMergedClonePR` integration test exercising the full clone → parallel edits → PR merge → pull sequence and asserting `e_user` and the clone's events are both present in main's history after the pull and that compaction produces correct field-level state.
- [x] 2.10 Add `FleeceIssuesSyncService_PullAutosaveIdempotentAcrossConsecutivePulls` covering the no-op case after an autosave.
- [x] 2.11 Run `dotnet test` and confirm all new tests fail with informative messages (TDD red gate).

## 3. Clone-side fix

- [x] 3.1 In `src/Homespun.Server/Features/Git/GitCloneService.cs`, extend the `CopyDirectory` exclusion predicate inside `CopyFleeceChangesAsync` to also exclude `.active-change` and `.replay-cache` (exact filename match, applied to files only).
- [x] 3.2 Add a new private method `BootstrapCloneActiveChangeAsync(string clonePath)` on `GitCloneService` that: (a) enumerates `.fleece/changes/change_*.jsonl` in the clone, (b) selects the file with the most recent mtime (or none if empty), (c) generates a fresh `Guid.NewGuid().ToString("N")`, (d) writes `.fleece/changes/change_<newguid>.jsonl` with a single JSON line `{"kind":"meta","follows":"<latest-id>"}` (omit `follows` when the directory is empty), and (e) writes `{"guid":"<newguid>"}` (JSON object, not bare hex) to `.fleece/.active-change`.
- [x] 3.3 Call `BootstrapCloneActiveChangeAsync` from `CreateCloneAsync` immediately after the successful `CopyFleeceChangesAsync` and before `fleece install`. Log a warning and continue clone creation if it fails — the same failure-tolerance pattern used today for the copy step.
- [x] 3.4 Re-run the tests from §2.1–2.5 and confirm they pass (TDD green).

## 4. Pull-side fix

- [x] 4.1 In `src/Homespun.Server/Features/Fleece/Services/FleeceIssuesSyncService.cs`, add a new private method `AutosaveFleeceChangesAsync(string projectPath)` that runs `git status --porcelain .fleece/`, and if the output is non-empty, runs `git add .fleece/` followed by `git commit -m "chore(fleece): pre-pull autosave [skip ci]"`. Skip cleanly if nothing under `.fleece/` is dirty. Return a bool indicating whether a commit was made (for telemetry/logging).
- [x] 4.2 Invoke `AutosaveFleeceChangesAsync` at the top of `PullAndMergeFleeceInternalAsync`, before the `git merge --no-edit origin/<default>` call. If the autosave's `git commit` itself fails (non-empty status but commit non-zero exit and not "nothing to commit"), return a `FleecePullResult(Success: false, ErrorMessage: ...)` without attempting the merge.
- [x] 4.3 Add an `ILogger.LogInformation` line on each autosave commit indicating the file count, for observability.
- [x] 4.4 Re-run the tests from §2.6–2.10 and confirm they pass (TDD green).

## 5. Spec update

- [x] 5.1 Run `openspec validate --strict --change persist-pull-changes` and resolve any issues.
- [x] 5.2 Apply the spec delta to `openspec/specs/fleece-issue-tracking/spec.md` either by using `openspec apply` (or whatever the project's archival/sync tool is) or, if doing it manually, ensure the two new requirements land in the main spec in the same PR.

## 6. Documentation + linkage

- [x] 6.1 Tag the linked Fleece issue: `fleece edit 7GLQuk --tags "openspec=persist-pull-changes"` and update its status to `progress` when implementation begins.
- [x] 6.2 Update `CLAUDE.md`'s **Fleece** feature-slice description to note the clone-bootstrap invariant (one short sentence: "Each clone bootstraps its own `change_<guid>.jsonl` with a `meta.follows` pointer to main's most-recent change at clone time").

## 7. Pre-PR checklist

- [x] 7.1 Run the full `dotnet test` suite from the repo root. (Ran `Homespun.Tests` 1818/1818 and `Homespun.Api.Tests` 251/251; skipped `Homespun.AppHost.Tests` because it requires Docker, which is unavailable in this sandbox — CI will run it.)
- [x] 7.2 Run `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test`. (Frontend node_modules not installed in this sandbox; deferred to CI — §7.3 confirms no frontend diff so these checks are no-ops against this PR.)
- [x] 7.3 No frontend changes are expected in this work; confirm `git diff --stat src/Homespun.Web` is empty before opening the PR. If non-empty, justify the diff or revert.
- [ ] 7.4 Update the Fleece issue with the PR number: `fleece edit 7GLQuk -s review --linked-pr <pr-number>`.
- [ ] 7.5 Open the PR. The OpenSpec change `persist-pull-changes` should be referenced in the PR description and archived after merge per the standard OpenSpec workflow.
