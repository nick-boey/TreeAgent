using System.Diagnostics;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Tests.Features.Fleece.Services;

/// <summary>
/// Integration tests covering the OpenSpec `persist-pull-changes` pull-side
/// invariants: uncommitted `.fleece/` working-tree changes must be committed
/// locally as a `chore(fleece): pre-pull autosave [skip ci]` commit before
/// `git merge --no-edit origin/&lt;default&gt;` runs, so the merge becomes a
/// real three-way merge instead of silently overwriting working-tree lines.
/// </summary>
[TestFixture]
[Category("Integration")]
public class FleeceIssuesSyncServicePersistPullTests
{
    private string _root = null!;
    private string _bareRemotePath = null!;
    private string _mainPath = null!;
    private string _defaultBranch = null!;
    private FleeceIssuesSyncService _service = null!;
    private CommandRunner _commandRunner = null!;
    private GitCloneService _cloneService = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Homespun_PersistPullTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _bareRemotePath = Path.Combine(_root, "origin.git");
        _mainPath = Path.Combine(_root, "main");

        // Bare remote whose default branch is `main` — `fleece project` refuses to run on any
        // other branch, so we pin both sides to `main` regardless of the machine's git default.
        RunGit(_root, "init --bare --initial-branch=main origin.git");

        // Working copy initialised directly with `main` as the default branch. We add `origin`
        // and push once we have a commit, which seeds the bare remote's `main` ref.
        RunGit(_root, "init --initial-branch=main main");
        RunGit(_mainPath, "config user.email \"test@example.com\"");
        RunGit(_mainPath, "config user.name \"Test User\"");
        RunGit(_mainPath, $"remote add origin \"{_bareRemotePath}\"");

        File.WriteAllText(Path.Combine(_mainPath, "README.md"), "# Test\n");
        RunGit(_mainPath, "add README.md");
        RunGit(_mainPath, "commit -m \"Initial commit\"");

        _defaultBranch = "main";
        RunGit(_mainPath, $"push -u origin {_defaultBranch}");

        // Configure the bare remote to accept pushes to the currently-checked-out branch
        // (so we can simulate "merged PR" pushes from another working copy).
        RunGit(_bareRemotePath, "config receive.denyCurrentBranch ignore");

        _commandRunner = new CommandRunner(
            new NullGitHubEnvironmentService(),
            NullLogger<CommandRunner>.Instance);
        _service = new FleeceIssuesSyncService(_commandRunner, NullLogger<FleeceIssuesSyncService>.Instance);
        _cloneService = new GitCloneService();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                ForceDelete(_root);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private class NullGitHubEnvironmentService : IGitHubEnvironmentService
    {
        public bool IsConfigured => false;
        public IDictionary<string, string> GetGitHubEnvironment() => new Dictionary<string, string>();
        public string? GetMaskedToken() => null;
        public Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new GitHubAuthStatus
            {
                IsAuthenticated = false,
                AuthMethod = GitHubAuthMethod.None
            });
        public string GetGitAuthorName() => "Test User";
        public string GetGitAuthorEmail() => "test@example.com";
    }

    private static string RunGit(string workingDir, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed (cwd={workingDir}): {error}");
        }
        return output;
    }

    private static void ForceDelete(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Write a change file inside the supplied working copy, then commit + push so
    /// the bare remote advances by one commit. This simulates "another collaborator
    /// merged their PR" without needing a second working copy per test.
    /// </summary>
    private void SeedRemoteCommit(string changeId, string contents, string commitMessage)
    {
        var tempClonePath = Path.Combine(_root, $"sim-{Guid.NewGuid():N}");
        RunGit(_root, $"clone -b main \"{_bareRemotePath}\" \"{Path.GetFileName(tempClonePath)}\"");
        RunGit(tempClonePath, "config user.email \"sim@example.com\"");
        RunGit(tempClonePath, "config user.name \"Sim User\"");

        var changesDir = Path.Combine(tempClonePath, ".fleece", "changes");
        Directory.CreateDirectory(changesDir);
        File.WriteAllText(Path.Combine(changesDir, $"change_{changeId}.jsonl"), contents);
        RunGit(tempClonePath, "add .fleece/");
        RunGit(tempClonePath, $"commit -m \"{commitMessage}\"");
        RunGit(tempClonePath, $"push origin {_defaultBranch}");

        ForceDelete(tempClonePath);
    }

    private void CommitInitialFleeceState(string changeId, string contents)
    {
        var changesDir = Path.Combine(_mainPath, ".fleece", "changes");
        Directory.CreateDirectory(changesDir);
        File.WriteAllText(Path.Combine(changesDir, $"change_{changeId}.jsonl"), contents);
        // Seed an empty snapshot so `fleece project` has something to write into.
        File.WriteAllText(Path.Combine(_mainPath, ".fleece", "issues.jsonl"), string.Empty);
        RunGit(_mainPath, "add .fleece/");
        RunGit(_mainPath, "commit -m \"Initial fleece state\"");
        RunGit(_mainPath, $"push origin {_defaultBranch}");
    }

    private string RunGitLog(string format = "%s")
        => RunGit(_mainPath, $"log --format=\"{format}\"");

    [Test]
    public async Task FleeceIssuesSyncService_PullAutosavesUncommittedFleeceChanges()
    {
        // Arrange — committed initial state in main + remote.
        var localChangeId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CommitInitialFleeceState(localChangeId, "{\"kind\":\"meta\"}\n");

        // Remote advances by one commit (simulated PR merge).
        SeedRemoteCommit(
            changeId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            contents: "{\"kind\":\"meta\"}\n",
            commitMessage: "PR merge");

        // Uncommitted edit in main's working tree.
        var localChangePath = Path.Combine(_mainPath, ".fleece", "changes", $"change_{localChangeId}.jsonl");
        File.AppendAllText(
            localChangePath,
            "{\"kind\":\"set\",\"at\":\"2026-05-25T00:00:00Z\",\"by\":\"user\",\"issueId\":\"AAA111\",\"property\":\"title\",\"value\":\"e_user\"}\n");

        // Act
        var result = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);

        // Assert
        Assert.That(result.Success, Is.True, $"Pull should succeed. Error: {result.ErrorMessage}");
        var log = RunGitLog();
        Assert.That(log, Does.Contain("chore(fleece): pre-pull autosave [skip ci]"),
            "Expected an autosave commit before the merge.");
        Assert.That(log, Does.Contain("PR merge"),
            "Expected the merged remote commit to be present in main's history.");

        // The autosave commit must appear in history *before* (i.e., as an ancestor of) the merge commit.
        // `git log --format=%H` lists most-recent first. Both the autosave subject and the PR-merge
        // subject should be present in the visible history.
        var subjectsTopDown = RunGit(_mainPath, "log --format=%s").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var autosaveIdx = Array.FindIndex(subjectsTopDown, s => s.Contains("pre-pull autosave"));
        var prMergeIdx = Array.FindIndex(subjectsTopDown, s => s.Contains("PR merge"));
        Assert.That(autosaveIdx, Is.GreaterThanOrEqualTo(0), "Autosave commit should be in history.");
        Assert.That(prMergeIdx, Is.GreaterThanOrEqualTo(0), "PR merge commit should be in history.");
    }

    [Test]
    public async Task FleeceIssuesSyncService_PullDoesNotAutosaveCleanState()
    {
        // Arrange — committed initial state, no uncommitted local changes.
        CommitInitialFleeceState("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "{\"kind\":\"meta\"}\n");
        SeedRemoteCommit(
            changeId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            contents: "{\"kind\":\"meta\"}\n",
            commitMessage: "Remote-only commit");

        // Act
        var result = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);

        // Assert
        Assert.That(result.Success, Is.True, $"Pull should succeed. Error: {result.ErrorMessage}");
        var log = RunGitLog();
        Assert.That(log, Does.Not.Contain("pre-pull autosave"),
            "No autosave commit should be created when the working tree is clean.");
        Assert.That(log, Does.Contain("Remote-only commit"),
            "Remote commit should be merged in.");
    }

    [Test]
    public async Task FleeceIssuesSyncService_PullAutosaveScopeIsFleeceOnly()
    {
        // Arrange — committed initial state, then dirty edits to BOTH a fleece file AND README.md.
        var localChangeId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CommitInitialFleeceState(localChangeId, "{\"kind\":\"meta\"}\n");
        SeedRemoteCommit(
            changeId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            contents: "{\"kind\":\"meta\"}\n",
            commitMessage: "Remote commit");

        // Dirty both fleece + non-fleece files.
        File.AppendAllText(
            Path.Combine(_mainPath, ".fleece", "changes", $"change_{localChangeId}.jsonl"),
            "{\"kind\":\"set\",\"at\":\"2026-05-25T00:00:00Z\",\"by\":\"user\",\"issueId\":\"AAA111\",\"property\":\"title\",\"value\":\"e_user\"}\n");
        File.AppendAllText(Path.Combine(_mainPath, "README.md"), "\n\nLocal user edit.\n");

        // Act
        var result = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);

        // Assert — pull succeeds, autosave only touches .fleece/.
        Assert.That(result.HasNonFleeceChanges, Is.True,
            "HasNonFleeceChanges must surface the README.md edit, unchanged from today's behavior.");
        Assert.That(result.NonFleeceChangedFiles, Is.Not.Null);
        Assert.That(result.NonFleeceChangedFiles!, Has.Some.Matches<string>(f => f == "README.md"));

        // Identify the autosave commit and inspect the tree it changed.
        var autosaveSha = RunGit(_mainPath, "log --format=%H -n 1 --grep \"pre-pull autosave\"").Trim();
        Assert.That(autosaveSha, Is.Not.Empty, "Autosave commit should exist.");

        // List paths changed by the autosave commit; every path must be under .fleece/.
        var diffOutput = RunGit(_mainPath, $"diff-tree --no-commit-id --name-only -r {autosaveSha}");
        var changedPaths = diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(changedPaths, Is.Not.Empty, "Autosave commit must include at least one path.");
        foreach (var p in changedPaths)
        {
            Assert.That(p.StartsWith(".fleece/", StringComparison.OrdinalIgnoreCase), Is.True,
                $"Autosave commit must not include non-fleece path '{p}'.");
        }

        // README.md edit must remain in the working tree (unstaged) — untouched by autosave.
        var statusOutput = RunGit(_mainPath, "status --porcelain README.md");
        Assert.That(statusOutput.Trim(), Is.Not.Empty,
            "README.md edit must still be uncommitted/unstaged after the pull.");
    }

    [Test]
    public async Task FleeceIssuesSyncService_PullAutosaveIdempotentAcrossConsecutivePulls()
    {
        // Arrange — set up an autosave-producing scenario.
        var localChangeId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CommitInitialFleeceState(localChangeId, "{\"kind\":\"meta\"}\n");
        SeedRemoteCommit(
            changeId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            contents: "{\"kind\":\"meta\"}\n",
            commitMessage: "First remote commit");
        File.AppendAllText(
            Path.Combine(_mainPath, ".fleece", "changes", $"change_{localChangeId}.jsonl"),
            "{\"kind\":\"set\",\"at\":\"2026-05-25T00:00:00Z\",\"by\":\"user\",\"issueId\":\"AAA111\",\"property\":\"title\",\"value\":\"e_user\"}\n");

        // First pull — autosave commit expected.
        var firstResult = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);
        Assert.That(firstResult.Success, Is.True, $"First pull should succeed. Error: {firstResult.ErrorMessage}");
        var firstLog = RunGitLog();
        Assert.That(firstLog, Does.Contain("pre-pull autosave"),
            "First pull should produce an autosave commit.");

        var autosaveCountAfterFirst = firstLog.Split('\n').Count(l => l.Contains("pre-pull autosave"));

        // Act — immediately pull again with no additional edits.
        var secondResult = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);

        // Assert — no new autosave commit; working tree is clean.
        Assert.That(secondResult.Success, Is.True, $"Second pull should succeed. Error: {secondResult.ErrorMessage}");
        var secondLog = RunGitLog();
        var autosaveCountAfterSecond = secondLog.Split('\n').Count(l => l.Contains("pre-pull autosave"));
        Assert.That(autosaveCountAfterSecond, Is.EqualTo(autosaveCountAfterFirst),
            "Second consecutive pull must not produce a new autosave commit when there is nothing to autosave.");
    }

    [Test]
    public async Task UncommittedUserEditSurvivesPullOfMergedClonePR()
    {
        // End-to-end scenario: clone → parallel edits → PR merge → pull.
        // Asserts that both the user's uncommitted edit AND the clone's events survive in main's
        // history, and that compaction (fleece project) folds both into issues.jsonl.

        // ---- Step 1: Initial state in main + remote ----
        var initialChangeId = "0000000000000000000000000000aaaa";
        // Seed a single open issue so the change events have something to update.
        var initialSnapshot = "{\"id\":\"AAA111\",\"title\":\"Original Title\",\"description\":\"\",\"status\":\"Open\",\"type\":\"Task\",\"linkedIssues\":[],\"parentIssues\":[],\"tags\":[],\"createdBy\":\"seed\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"lastUpdate\":\"2026-01-01T00:00:00Z\",\"executionMode\":\"Series\"}";
        Directory.CreateDirectory(Path.Combine(_mainPath, ".fleece", "changes"));
        File.WriteAllText(Path.Combine(_mainPath, ".fleece", "issues.jsonl"), initialSnapshot + "\n");
        File.WriteAllText(
            Path.Combine(_mainPath, ".fleece", "changes", $"change_{initialChangeId}.jsonl"),
            "{\"kind\":\"meta\"}\n");
        RunGit(_mainPath, "add .fleece/");
        RunGit(_mainPath, "commit -m \"Seed initial snapshot + change file\"");
        RunGit(_mainPath, $"push origin {_defaultBranch}");

        // ---- Step 2: Use GitCloneService to create a clone (bootstraps fresh change file) ----
        var branchName = "feature/persist-pull-e2e";
        var cloneWorkdir = await _cloneService.CreateCloneAsync(_mainPath, branchName, createBranch: true);
        Assert.That(cloneWorkdir, Is.Not.Null, "Clone should be created successfully.");
        RunGit(cloneWorkdir!, "config user.email \"clone@example.com\"");
        RunGit(cloneWorkdir!, "config user.name \"Clone User\"");

        // The clone's bootstrapped change file id.
        var cloneChangesDir = Path.Combine(cloneWorkdir!, ".fleece", "changes");
        var cloneBootstrappedFile = Directory
            .GetFiles(cloneChangesDir, "change_*.jsonl")
            .Single(p => !p.EndsWith($"change_{initialChangeId}.jsonl"));

        // Clone makes an edit (e_clone): change the status field.
        File.AppendAllText(
            cloneBootstrappedFile,
            "{\"kind\":\"set\",\"at\":\"2026-05-25T01:00:00Z\",\"by\":\"clone\",\"issueId\":\"AAA111\",\"property\":\"status\",\"value\":\"Progress\"}\n");
        RunGit(cloneWorkdir!, "add .fleece/");
        RunGit(cloneWorkdir!, "commit -m \"Clone: update status\"");

        // ---- Step 3: Simulate "PR merge" — push the clone branch up as the new default-branch tip on the bare remote ----
        // The clone's origin currently points back to main (because `git clone --local` + remote-set-url uses
        // main's `origin get-url`, which here is the bare remote path). Push the clone branch into the bare
        // remote's default branch directly to simulate a maintainer landing the PR.
        RunGit(cloneWorkdir!, $"push origin {branchName}:{_defaultBranch}");

        // ---- Step 4: User makes uncommitted edit (e_user) on main's bootstrapped change file ----
        File.AppendAllText(
            Path.Combine(_mainPath, ".fleece", "changes", $"change_{initialChangeId}.jsonl"),
            "{\"kind\":\"set\",\"at\":\"2026-05-25T00:30:00Z\",\"by\":\"user\",\"issueId\":\"AAA111\",\"property\":\"title\",\"value\":\"User Updated Title\"}\n");

        // ---- Step 5: PullFleeceOnlyAsync — should autosave e_user, merge clone's events, then compact ----
        var pullResult = await _service.PullFleeceOnlyAsync(_mainPath, _defaultBranch);
        Assert.That(pullResult.Success, Is.True, $"Pull should succeed. Error: {pullResult.ErrorMessage}");

        // Both edit streams are visible in main's history (autosave commit + merged clone commit).
        var allLog = RunGit(_mainPath, "log --all --format=%s");
        Assert.That(allLog, Does.Contain("pre-pull autosave"),
            "e_user must have been autosaved into a commit on main before the merge.");
        Assert.That(allLog, Does.Contain("Clone: update status"),
            "Clone's commit must be present in main's history after the merge.");

        // The merge commit's tree (HEAD) carries both change files — fleece project runs *after*
        // the merge but does not commit, so HEAD remains the merge commit.
        var headTree = RunGit(_mainPath, "ls-tree -r HEAD .fleece/changes/");
        Assert.That(headTree, Does.Contain($"change_{initialChangeId}.jsonl"),
            "Main's HEAD tree must include the user's change file (post-autosave, pre-compaction).");
        var cloneChangeFileName = Path.GetFileName(cloneBootstrappedFile);
        Assert.That(headTree, Does.Contain(cloneChangeFileName),
            "Main's HEAD tree must include the clone's bootstrapped change file (post-merge, pre-compaction).");

        // ---- Step 6: Verify `fleece project` (run inside PullAndMergeFleeceInternalAsync) folded both
        // edit streams into the snapshot — field-level LWW: title from user, status from clone. ----
        var compactedSnapshot = File.ReadAllText(Path.Combine(_mainPath, ".fleece", "issues.jsonl"));
        Assert.That(compactedSnapshot, Does.Contain("\"title\":\"User Updated Title\""),
            "Compaction must retain the user's title edit.");
        Assert.That(compactedSnapshot, Does.Contain("\"status\":\"Progress\""),
            "Compaction must retain the clone's status edit.");
    }
}
