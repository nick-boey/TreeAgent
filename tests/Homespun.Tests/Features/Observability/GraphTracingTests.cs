using System.Diagnostics;
using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Commands.Telemetry;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Gitgraph.Telemetry;
using Homespun.Features.GitHub;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Task 1.11 — drives <see cref="GraphService.BuildEnhancedTaskGraphAsync"/> once through
/// the real enrichment chain and asserts every named span registered in Tier 1 fires.
/// </summary>
[TestFixture]
public class GraphTracingTests
{
    private string _testPath = null!;
    private string _changeDir = null!;
    private MockDataStore _dataStore = null!;
    private Project _project = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"graph-tracing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testPath);
        Directory.CreateDirectory(Path.Combine(_testPath, ".fleece"));

        // openspec/changes/my-change with sidecar pointing at our fleece id
        _changeDir = Path.Combine(_testPath, "openspec", "changes", "my-change");
        Directory.CreateDirectory(_changeDir);
        Directory.CreateDirectory(Path.Combine(_changeDir, "specs"));
        await File.WriteAllTextAsync(Path.Combine(_changeDir, "proposal.md"), "# proposal\n");
        await File.WriteAllTextAsync(Path.Combine(_changeDir, "tasks.md"), "- [ ] 1.1 do stuff\n");
        await File.WriteAllTextAsync(
            Path.Combine(_changeDir, ".homespun.yaml"),
            "fleeceId: issue-1\ncreatedBy: test\n");

        _dataStore = new MockDataStore();
        _project = new Project
        {
            Name = "test-repo",
            LocalPath = _testPath,
            GitHubOwner = "test",
            GitHubRepo = "test",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(_project);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
        if (Directory.Exists(_testPath))
        {
            try { Directory.Delete(_testPath, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_Fires_All_Named_Spans()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src =>
                src.Name == GraphgraphActivitySource.Name
                || src.Name == OpenSpecActivitySource.Name
                || src.Name == CommandsActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => spans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var service = BuildGraphServiceWithRealEnrichmentChain();

        var response = await service.BuildEnhancedTaskGraphAsync(_project.Id, maxPastPRs: 5);

        Assert.That(response, Is.Not.Null);

        var names = spans.Select(s => s.OperationName).ToHashSet();

        // Graph-tier spans
        Assert.That(names, Does.Contain("graph.taskgraph.build"));
        Assert.That(names, Does.Contain("graph.taskgraph.fleece.scan"));
        Assert.That(names, Does.Contain("graph.taskgraph.sessions"));
        Assert.That(names, Does.Contain("graph.taskgraph.prcache"));

        // OpenSpec enrichment chain
        Assert.That(names, Does.Contain("openspec.enrich"));
        Assert.That(names, Does.Contain("openspec.enrich.node"));
        Assert.That(names, Does.Contain("openspec.branch.resolve"));
        Assert.That(names, Does.Contain("openspec.state.resolve"));
        Assert.That(names, Does.Contain("openspec.reconcile"));
        Assert.That(names, Does.Contain("openspec.scan.branch"));
        Assert.That(names, Does.Contain("openspec.artifact.state"));

        // CommandRunner (fires even when the `openspec` binary is absent because
        // the catch branch still records the stopped activity).
        Assert.That(names, Does.Contain("cmd.run"));

        // openspec.state.resolve on the miss path carries cache.hit=false.
        var stateResolveSpan = spans.First(s => s.OperationName == "openspec.state.resolve");
        Assert.That(stateResolveSpan.GetTagItem("cache.hit"), Is.EqualTo(false));
    }

    private GraphService BuildGraphServiceWithRealEnrichmentChain()
    {
        // Fleece returns one visible node for issue-1
        var fleeceService = new Mock<IProjectFleeceService>();
        fleeceService
            .Setup(f => f.GetTaskGraphWithAdditionalIssuesAsync(
                _testPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphLayout<Issue>
            {
                TotalLanes = 1,
                TotalRows = 1,
                Nodes =
                [
                    new PositionedNode<Issue>
                    {
                        Node = new Issue
                        {
                            Id = "issue-1",
                            Title = "test",
                            Type = IssueType.Task,
                            Status = IssueStatus.Open,
                            WorkingBranchId = "abc",
                            CreatedAt = DateTime.UtcNow,
                            LastUpdate = DateTime.UtcNow
                        },
                        Lane = 0,
                        Row = 0
                    }
                ],
                Edges = [],
                Occupancy = new OccupancyCell[1, 1]
            });
        fleeceService
            .Setup(f => f.GetIssueAsync(_testPath, "issue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Issue
            {
                Id = "issue-1",
                Title = "test",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                WorkingBranchId = "abc",
                CreatedAt = DateTime.UtcNow,
                LastUpdate = DateTime.UtcNow
            });

        // Clones: single clone whose path matches the test project directory so
        // the branch-state resolver lands on the on-disk openspec tree we set up.
        var expectedBranch = Homespun.Shared.Models.PullRequests.BranchNameGenerator
            .GenerateBranchNamePreview("issue-1", IssueType.Task, "test", "abc");
        var cloneInfo = new CloneInfo { Path = _testPath, Branch = expectedBranch };
        var cloneService = new Mock<IGitCloneService>();
        cloneService.Setup(c => c.ListClonesAsync(_testPath))
            .ReturnsAsync([cloneInfo]);
        cloneService.Setup(c => c.GetClonePathForBranchAsync(_testPath, expectedBranch))
            .ReturnsAsync(_testPath);

        var transition = new Mock<IFleeceIssueTransitionService>();

        // Return an issue tagged openspec=my-change so BuildTagMapAsync links the
        // change directory and ScanBranchAsync calls GetArtifactStateAsync (which
        // emits the openspec.artifact.state span we assert on).
        var scannerFleeceService = new Mock<IProjectFleeceService>();
        scannerFleeceService
            .Setup(f => f.ListIssuesAsync(
                _testPath,
                It.IsAny<IssueStatus?>(),
                It.IsAny<IssueType?>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Issue>)new List<Issue>
            {
                new Issue
                {
                    Id = "issue-1",
                    Title = "test",
                    Type = IssueType.Task,
                    Status = IssueStatus.Open,
                    Tags = ["openspec=my-change"],
                    CreatedAt = DateTime.UtcNow,
                    LastUpdate = DateTime.UtcNow
                }
            });

        var commandRunner = new CommandRunner(
            new MockGitHubEnvironmentService(),
            NullLogger<CommandRunner>.Instance);
        var scanner = new ChangeScannerService(
            scannerFleeceService.Object,
            commandRunner,
            NullLogger<ChangeScannerService>.Instance);
        var reconciliation = new ChangeReconciliationService(
            scanner,
            transition.Object,
            NullLogger<ChangeReconciliationService>.Instance);

        var branchCache = new BranchStateCacheService(TimeProvider.System);
        var stateResolver = new BranchStateResolverService(
            branchCache,
            reconciliation,
            _dataStore,
            cloneService.Object,
            TimeProvider.System,
            NullLogger<BranchStateResolverService>.Instance);

        var branchResolver = new IssueBranchResolverService(
            _dataStore,
            cloneService.Object,
            fleeceService.Object,
            NullLogger<IssueBranchResolverService>.Instance);

        var enricher = new IssueGraphOpenSpecEnricher(
            branchResolver,
            stateResolver,
            _dataStore,
            cloneService.Object,
            scanner,
            NullLogger<IssueGraphOpenSpecEnricher>.Instance);

        var projectService = new Mock<IProjectService>();
        projectService.Setup(p => p.GetByIdAsync(_project.Id)).ReturnsAsync(_project);
        var sessionStore = new Mock<IClaudeSessionStore>();
        sessionStore.Setup(s => s.GetByProjectId(_project.Id)).Returns([]);
        var workflow = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose, _dataStore, null!, null!, null!, null!);
        var cache = new Mock<IGraphCacheService>();
        cache.Setup(c => c.GetCachedPRData(_project.Id)).Returns((CachedPRData?)null);

        return new GraphService(
            projectService.Object,
            new Mock<IGitHubService>().Object,
            fleeceService.Object,
            sessionStore.Object,
            _dataStore,
            workflow.Object,
            cache.Object,
            new Mock<IPRStatusResolver>().Object,
            enricher,
            cloneService.Object,
            NullLogger<GraphService>.Instance);
    }
}
