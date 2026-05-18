using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class IssueGraphOpenSpecEnricherTests
{
    private Mock<IIssueBranchResolverService> _branchResolver = null!;
    private Mock<IBranchStateResolverService> _stateResolver = null!;
    private Mock<IDataStore> _dataStore = null!;
    private Mock<IGitCloneService> _cloneService = null!;
    private Mock<IChangeScannerService> _scanner = null!;
    private IssueGraphOpenSpecEnricher _enricher = null!;

    private const string ProjectId = "proj-1";

    [SetUp]
    public void SetUp()
    {
        _branchResolver = new Mock<IIssueBranchResolverService>();
        _stateResolver = new Mock<IBranchStateResolverService>();
        _dataStore = new Mock<IDataStore>();
        _cloneService = new Mock<IGitCloneService>();
        _scanner = new Mock<IChangeScannerService>();

        _dataStore.Setup(d => d.GetProject(ProjectId))
            .Returns((Project?)null);

        _enricher = new IssueGraphOpenSpecEnricher(
            _branchResolver.Object,
            _stateResolver.Object,
            _dataStore.Object,
            _cloneService.Object,
            _scanner.Object,
            NullLogger<IssueGraphOpenSpecEnricher>.Instance);
    }

    [Test]
    public async Task EnrichAsync_IssueWithoutBranch_ReportsNoBranchState()
    {
        var response = ResponseWith("issue-1");
        _branchResolver.Setup(b => b.ResolveIssueBranchAsync(ProjectId, "issue-1", It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync((string?)null);

        await _enricher.EnrichAsync(ProjectId, response);

        Assert.That(response.OpenSpecStates, Contains.Key("issue-1"));
        Assert.That(response.OpenSpecStates["issue-1"].BranchState, Is.EqualTo(BranchPresence.None));
        Assert.That(response.OpenSpecStates["issue-1"].ChangeState, Is.EqualTo(ChangePhase.None));
    }

    [Test]
    public async Task EnrichAsync_BranchExistsNoChange_ReportsExistsNone()
    {
        var response = ResponseWith("issue-1");
        _branchResolver.Setup(b => b.ResolveIssueBranchAsync(ProjectId, "issue-1", It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync("feat/foo+issue-1");
        _stateResolver.Setup(s => s.GetOrScanAsync(ProjectId, "feat/foo+issue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchStateSnapshot
            {
                ProjectId = ProjectId,
                Branch = "feat/foo+issue-1",
                FleeceId = "issue-1"
            });

        await _enricher.EnrichAsync(ProjectId, response);

        var state = response.OpenSpecStates["issue-1"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.Exists));
        Assert.That(state.ChangeState, Is.EqualTo(ChangePhase.None));
    }

    [Test]
    public async Task EnrichAsync_ArchivedChange_MapsToArchivedPhase()
    {
        ConfigureSnapshot("issue-1", new SnapshotChange
        {
            Name = "done-change",
            CreatedBy = "agent",
            IsArchived = true,
            TasksDone = 5,
            TasksTotal = 5
        });

        var response = ResponseWith("issue-1");
        await _enricher.EnrichAsync(ProjectId, response);

        var state = response.OpenSpecStates["issue-1"];
        Assert.That(state.BranchState, Is.EqualTo(BranchPresence.WithChange));
        Assert.That(state.ChangeState, Is.EqualTo(ChangePhase.Archived));
        Assert.That(state.ChangeName, Is.EqualTo("done-change"));
    }

    [Test]
    public async Task EnrichAsync_AllTasksDone_MapsToReadyToArchive()
    {
        ConfigureSnapshot("issue-1", new SnapshotChange
        {
            Name = "x",
            CreatedBy = "agent",
            IsArchived = false,
            TasksDone = 4,
            TasksTotal = 4,
            ArtifactState = new ChangeArtifactState
            {
                ChangeName = "x", SchemaName = "spec-driven", IsComplete = true
            }
        });

        var response = ResponseWith("issue-1");
        await _enricher.EnrichAsync(ProjectId, response);

        Assert.That(response.OpenSpecStates["issue-1"].ChangeState, Is.EqualTo(ChangePhase.ReadyToArchive));
    }

    [Test]
    public async Task EnrichAsync_ArtifactsComplete_MapsToReadyToApply()
    {
        ConfigureSnapshot("issue-1", new SnapshotChange
        {
            Name = "x",
            CreatedBy = "agent",
            IsArchived = false,
            TasksDone = 0,
            TasksTotal = 4,
            ArtifactState = new ChangeArtifactState
            {
                ChangeName = "x", SchemaName = "spec-driven", IsComplete = true
            }
        });

        var response = ResponseWith("issue-1");
        await _enricher.EnrichAsync(ProjectId, response);

        Assert.That(response.OpenSpecStates["issue-1"].ChangeState, Is.EqualTo(ChangePhase.ReadyToApply));
    }

    [Test]
    public async Task EnrichAsync_ArtifactsIncomplete_MapsToIncomplete()
    {
        ConfigureSnapshot("issue-1", new SnapshotChange
        {
            Name = "x",
            CreatedBy = "agent",
            IsArchived = false,
            TasksDone = 0,
            TasksTotal = 0,
            ArtifactState = new ChangeArtifactState
            {
                ChangeName = "x", SchemaName = "spec-driven", IsComplete = false
            }
        });

        var response = ResponseWith("issue-1");
        await _enricher.EnrichAsync(ProjectId, response);

        Assert.That(response.OpenSpecStates["issue-1"].ChangeState, Is.EqualTo(ChangePhase.Incomplete));
    }

    [Test]
    public async Task EnrichAsync_IncludesPhases()
    {
        ConfigureSnapshot("issue-1", new SnapshotChange
        {
            Name = "x",
            CreatedBy = "agent",
            TasksDone = 1,
            TasksTotal = 3,
            Phases = new List<PhaseState>
            {
                new() { Name = "1. Design", Done = 1, Total = 1 },
                new() { Name = "2. Build", Done = 0, Total = 2 }
            }
        });

        var response = ResponseWith("issue-1");
        await _enricher.EnrichAsync(ProjectId, response);

        var state = response.OpenSpecStates["issue-1"];
        Assert.That(state.Phases, Has.Count.EqualTo(2));
        Assert.That(state.Phases[0].Name, Is.EqualTo("1. Design"));
        Assert.That(state.Phases[0].Total, Is.EqualTo(1));
        Assert.That(state.Phases[1].Total, Is.EqualTo(2));
    }

    [Test]
    public async Task EnrichAsync_ErrorInResolver_SwallowsAndContinues()
    {
        var response = ResponseWith("issue-1", "issue-2");
        _branchResolver.Setup(b => b.ResolveIssueBranchAsync(ProjectId, "issue-1", It.IsAny<BranchResolutionContext>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _branchResolver.Setup(b => b.ResolveIssueBranchAsync(ProjectId, "issue-2", It.IsAny<BranchResolutionContext>()))
            .ReturnsAsync((string?)null);

        await _enricher.EnrichAsync(ProjectId, response);

        Assert.That(response.OpenSpecStates, Does.Not.ContainKey("issue-1"));
        Assert.That(response.OpenSpecStates, Contains.Key("issue-2"));
    }

    // --- helpers ---

    private static TaskGraphResponse ResponseWith(params string[] issueIds)
    {
        return new TaskGraphResponse
        {
            Nodes = issueIds.Select(id => new TaskGraphNodeResponse
            {
                Issue = new IssueResponse { Id = id, Title = id }
            }).ToList()
        };
    }

    private void ConfigureSnapshot(string issueId, SnapshotChange change)
    {
        var branch = $"feat/auto+{issueId}";
        _branchResolver.Setup(b => b.ResolveIssueBranchAsync(ProjectId, issueId, It.IsAny<BranchResolutionContext>())).ReturnsAsync(branch);
        _stateResolver.Setup(s => s.GetOrScanAsync(ProjectId, branch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchStateSnapshot
            {
                ProjectId = ProjectId,
                Branch = branch,
                FleeceId = issueId,
                Changes = new List<SnapshotChange> { change }
            });
    }
}
