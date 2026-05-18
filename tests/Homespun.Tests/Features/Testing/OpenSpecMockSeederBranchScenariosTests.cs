using Fleece.Core.Models;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Commands;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Testing;

/// <summary>
/// End-to-end test: the OpenSpecMockSeeder's per-branch fixtures surface correctly through
/// <see cref="ChangeScannerService"/> (backed by a mocked <see cref="IProjectFleeceService"/>)
/// and through the <see cref="IssueGraphOpenSpecEnricher"/>.
/// Linkage is now driven by Fleece <c>openspec=&lt;name&gt;</c> tags, not sidecars.
/// </summary>
[TestFixture]
public class OpenSpecMockSeederBranchScenariosTests
{
    private string _tempDir = null!;
    private OpenSpecMockSeeder _seeder = null!;
    private Mock<ICommandRunner> _commandRunner = null!;
    private Mock<IProjectFleeceService> _fleeceService = null!;
    private ChangeScannerService _scanner = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openspec-branch-scenarios-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _seeder = new OpenSpecMockSeeder(
            new Mock<ITempDataFolderService>().Object,
            NullLogger<OpenSpecMockSeeder>.Instance);

        _commandRunner = new Mock<ICommandRunner>();
        _commandRunner
            .Setup(c => c.RunAsync("openspec", It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                ExitCode = 0,
                Output = """{"changeName":"x","schemaName":"spec-driven","isComplete":false}""",
                Error = string.Empty
            });

        _fleeceService = new Mock<IProjectFleeceService>();
        _fleeceService
            .Setup(f => f.ListIssuesAsync(It.IsAny<string>(), null, null, null, true, It.IsAny<CancellationToken>()))
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

    [Test]
    public async Task SeedBranch_Issue006_TaggedChange_AppearsLinked()
    {
        var clonePath = await SeedBranchAsync("ISSUE-006");
        StubFleeceTag(clonePath, "ISSUE-006", "openspec=api-v2-impl");

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-006");

        Assert.That(scan.LinkedChanges, Has.Count.EqualTo(1));
        Assert.That(scan.LinkedChanges[0].Name, Is.EqualTo("api-v2-impl"));
        Assert.That(scan.LinkedChanges[0].IsArchived, Is.False);
        Assert.That(scan.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue002_UntaggedChanges_SilentlySkipped()
    {
        var clonePath = await SeedBranchAsync("ISSUE-002");
        // No openspec= tags → both changes are silently skipped.

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-002");

        Assert.That(scan.LinkedChanges, Is.Empty);
        Assert.That(scan.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue001_UntaggedChange_SilentlySkipped()
    {
        var clonePath = await SeedBranchAsync("ISSUE-001");
        // inherited-from-main has no openspec= tag for ISSUE-001 → silently skipped.

        var scan = await _scanner.ScanBranchAsync(clonePath, "ISSUE-001");

        Assert.That(scan.LinkedChanges, Is.Empty);
        Assert.That(scan.OrphanChanges, Is.Empty);
    }

    [Test]
    public async Task SeedBranch_Issue003_ProducesNoOpenspecAtAll()
    {
        var clonePath = await SeedBranchAsync("ISSUE-003");

        Assert.That(Directory.Exists(Path.Combine(clonePath, "openspec")), Is.False);
    }

    [Test]
    public async Task EnrichAsync_Issue006_ReportsWithChangeAndPhases()
    {
        var clonePath = await SeedBranchAsync("ISSUE-006");
        StubFleeceTag(clonePath, "ISSUE-006", "openspec=api-v2-impl");
        var enricher = BuildEnricher(clonePath, branch: "feature/api-v2+ISSUE-006", issueId: "ISSUE-006");

        var response = new TaskGraphResponse
        {
            Nodes = new List<TaskGraphNodeResponse>
            {
                new() { Issue = new IssueResponse { Id = "ISSUE-006" } },
            }
        };

        await enricher.EnrichAsync("proj", response);

        var state = response.OpenSpecStates["ISSUE-006"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.WithChange));
        Assert.That(state.ChangeName, Is.EqualTo("api-v2-impl"));
        Assert.That(state.Phases, Is.Not.Empty);
    }

    private async Task<string> SeedBranchAsync(string branchFleeceId)
    {
        var clonePath = Path.Combine(_tempDir, $"clone-{branchFleeceId}");
        Directory.CreateDirectory(Path.Combine(clonePath, "openspec", "changes"));
        await _seeder.SeedBranchAsync(clonePath, $"feat/x+{branchFleeceId}", branchFleeceId);
        return clonePath;
    }

    private void StubFleeceTag(string clonePath, string issueId, string tag)
    {
        var issue = new Issue
        {
            Id = issueId,
            Title = issueId,
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow,
            Tags = [tag]
        };

        _fleeceService
            .Setup(f => f.ListIssuesAsync(clonePath, null, null, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { issue });
    }

    private IssueGraphOpenSpecEnricher BuildEnricher(string clonePath, string branch, string issueId)
    {
        var dataStore = new Mock<IDataStore>();
        dataStore.Setup(d => d.GetProject("proj"))
            .Returns(new Project
            {
                Id = "proj",
                Name = "Demo",
                LocalPath = _tempDir,
                DefaultBranch = "main",
            });
        dataStore.Setup(d => d.GetPullRequestsByProject("proj"))
            .Returns(new List<Homespun.Shared.Models.PullRequests.PullRequest>());

        var cloneService = new Mock<IGitCloneService>();
        cloneService.Setup(c => c.GetClonePathForBranchAsync(_tempDir, branch))
            .ReturnsAsync(clonePath);
        cloneService.Setup(c => c.ListClonesAsync(_tempDir))
            .ReturnsAsync(new List<Homespun.Shared.Models.Git.CloneInfo>());

        var branchResolver = new Mock<IIssueBranchResolverService>();
        branchResolver.Setup(b => b.ResolveIssueBranchAsync("proj", issueId, It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync(branch);

        var transitionService = new Mock<IFleeceIssueTransitionService>();
        var reconciliation = new ChangeReconciliationService(
            _scanner,
            transitionService.Object,
            NullLogger<ChangeReconciliationService>.Instance);

        var stateResolver = new BranchStateResolverService(
            new BranchStateCacheService(TimeProvider.System),
            reconciliation,
            dataStore.Object,
            cloneService.Object,
            TimeProvider.System,
            NullLogger<BranchStateResolverService>.Instance);

        return new IssueGraphOpenSpecEnricher(
            branchResolver.Object,
            stateResolver,
            dataStore.Object,
            cloneService.Object,
            _scanner,
            NullLogger<IssueGraphOpenSpecEnricher>.Instance);
    }
}
