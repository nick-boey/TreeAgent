using Fleece.Core.Models;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class ChangeScannerServiceTests
{
    private string _tempDir = null!;
    private Mock<ICommandRunner> _commandRunner = null!;
    private Mock<IProjectFleeceService> _fleeceService = null!;
    private ChangeScannerService _scanner = null!;

    private const string BranchFleeceId = "issue-abc";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"scanner-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _commandRunner = new Mock<ICommandRunner>();
        _fleeceService = new Mock<IProjectFleeceService>();

        _fleeceService
            .Setup(f => f.ListIssuesAsync(_tempDir, null, null, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Issue>());

        _scanner = new ChangeScannerService(
            _fleeceService.Object,
            _commandRunner.Object,
            NullLogger<ChangeScannerService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- Tag-map linkage tests (task 1.1) ---

    [Test]
    public async Task ScanBranchAsync_NoChangesDir_ReturnsEmpty()
    {
        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Is.Empty);
        Assert.That(result.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task ScanBranchAsync_TaggedChange_AppearsLinked()
    {
        CreateChangeDir("my-change");
        StubFleeceIssues(("issue-1", "openspec=my-change"));
        StubArtifactStatusSuccess("my-change", isComplete: false);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(result.LinkedChanges[0].Name, Is.EqualTo("my-change"));
        Assert.That(result.LinkedChanges[0].IsArchived, Is.False);
        Assert.That(result.LinkedChanges[0].ArtifactState!.IsComplete, Is.False);
    }

    [Test]
    public async Task ScanBranchAsync_UntaggedChange_SilentlySkipped()
    {
        CreateChangeDir("no-tag-change");
        // No openspec= tag on any issue — change should be silently skipped.

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Is.Empty);
        Assert.That(result.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task ScanBranchAsync_ArchivedChange_MatchesViaTag()
    {
        var archivedDir = CreateArchivedChangeDir("2026-04-16-old-change");
        _ = archivedDir;
        StubFleeceIssues(("issue-2", "openspec=old-change"));

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        var linked = result.LinkedChanges[0];
        Assert.That(linked.Name, Is.EqualTo("old-change"));
        Assert.That(linked.IsArchived, Is.True);
        Assert.That(linked.ArchivedFolderName, Is.EqualTo("2026-04-16-old-change"));
    }

    [Test]
    public async Task ScanBranchAsync_MultipleChanges_DifferentTags_LandUnderCorrectIssues()
    {
        CreateChangeDir("change-foo");
        CreateChangeDir("change-bar");
        StubFleeceIssues(
            ("issue-A", "openspec=change-foo"),
            ("issue-B", "openspec=change-bar"));
        StubArtifactStatusSuccess("change-foo", isComplete: false);
        StubArtifactStatusSuccess("change-bar", isComplete: true);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(2));
        Assert.That(result.LinkedChanges.Select(c => c.Name),
            Is.EquivalentTo(new[] { "change-foo", "change-bar" }));
    }

    [Test]
    public async Task ScanBranchAsync_LiveAndArchived_PrefersLive()
    {
        CreateChangeDir("dup-change");
        CreateArchivedChangeDir("2026-03-01-dup-change");
        StubFleeceIssues(("issue-1", "openspec=dup-change"));
        StubArtifactStatusSuccess("dup-change", isComplete: false);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(result.LinkedChanges[0].IsArchived, Is.False);
    }

    [Test]
    public async Task ScanBranchAsync_IncludesTaskState()
    {
        var changeDir = CreateChangeDir("with-tasks");
        StubFleeceIssues(("issue-1", "openspec=with-tasks"));
        await File.WriteAllTextAsync(Path.Combine(changeDir, "tasks.md"),
            "## 1. Phase\n\n- [x] Done\n- [ ] Pending\n");
        StubArtifactStatusSuccess("with-tasks", isComplete: true);

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        var linked = result.LinkedChanges.Single();
        Assert.That(linked.TaskState.TasksTotal, Is.EqualTo(2));
        Assert.That(linked.TaskState.TasksDone, Is.EqualTo(1));
        Assert.That(linked.TaskState.NextIncomplete, Is.EqualTo("Pending"));
    }

    [Test]
    public async Task ScanBranchAsync_FleeceServiceFailure_ReturnsNoLinked()
    {
        CreateChangeDir("my-change");
        _fleeceService
            .Setup(f => f.ListIssuesAsync(_tempDir, null, null, null, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fleece unavailable"));

        var result = await _scanner.ScanBranchAsync(_tempDir, BranchFleeceId);

        Assert.That(result.LinkedChanges, Is.Empty);
    }

    // --- Artifact state cache tests (unchanged) ---

    [Test]
    public async Task GetArtifactStateAsync_ParsesOpenSpecJson()
    {
        _commandRunner
            .Setup(r => r.RunAsync("openspec", It.Is<string>(s => s.Contains("--change \"my-change\"")), _tempDir))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                ExitCode = 0,
                Output = """
                    - Loading change status...
                    {
                      "changeName": "my-change",
                      "schemaName": "spec-driven",
                      "isComplete": true,
                      "applyRequires": ["tasks"],
                      "artifacts": [
                        { "id": "proposal", "outputPath": "proposal.md", "status": "done" }
                      ]
                    }
                    """
            });

        var result = await _scanner.GetArtifactStateAsync(_tempDir, "my-change");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ChangeName, Is.EqualTo("my-change"));
        Assert.That(result.SchemaName, Is.EqualTo("spec-driven"));
        Assert.That(result.IsComplete, Is.True);
        Assert.That(result.Artifacts, Has.Count.EqualTo(1));
        Assert.That(result.Artifacts[0].Id, Is.EqualTo("proposal"));
    }

    [Test]
    public async Task GetArtifactStateAsync_CliFailure_ReturnsNull()
    {
        _commandRunner
            .Setup(r => r.RunAsync("openspec", It.IsAny<string>(), _tempDir))
            .ReturnsAsync(new CommandResult { Success = false, ExitCode = 1, Error = "no such change" });

        var result = await _scanner.GetArtifactStateAsync(_tempDir, "missing");

        Assert.That(result, Is.Null);
    }

    // --- StripDatePrefix tests (unchanged) ---

    [Test]
    public void StripDatePrefix_ValidDate_RemovesPrefix()
    {
        Assert.That(ChangeScannerService.StripDatePrefix("2026-04-16-my-change"),
            Is.EqualTo("my-change"));
    }

    [Test]
    public void StripDatePrefix_NoDatePrefix_ReturnsUnchanged()
    {
        Assert.That(ChangeScannerService.StripDatePrefix("just-a-name"),
            Is.EqualTo("just-a-name"));
    }

    // --- helpers ---

    private string CreateChangeDir(string name)
    {
        var dir = Path.Combine(_tempDir, "openspec", "changes", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string CreateArchivedChangeDir(string archivedFolderName)
    {
        var dir = Path.Combine(_tempDir, "openspec", "changes", "archive", archivedFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void StubFleeceIssues(params (string issueId, string tag)[] entries)
    {
        var issues = entries.Select(e => new Issue
        {
            Id = e.issueId,
            Title = e.issueId,
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow,
            Tags = [e.tag]
        }).ToList();

        _fleeceService
            .Setup(f => f.ListIssuesAsync(_tempDir, null, null, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issues);
    }

    private void StubArtifactStatusSuccess(string changeName, bool isComplete)
    {
        var json = $$"""
            {
              "changeName": "{{changeName}}",
              "schemaName": "spec-driven",
              "isComplete": {{(isComplete ? "true" : "false")}},
              "applyRequires": ["tasks"],
              "artifacts": []
            }
            """;

        _commandRunner
            .Setup(r => r.RunAsync("openspec",
                It.Is<string>(s => s.Contains($"--change \"{changeName}\"")),
                _tempDir))
            .ReturnsAsync(new CommandResult { Success = true, ExitCode = 0, Output = json });
    }
}
