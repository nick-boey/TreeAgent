using Microsoft.EntityFrameworkCore;
using Moq;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.Agents.Services;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;
using TreeAgent.Web.Features.Roadmap;
using Project = TreeAgent.Web.Features.PullRequests.Data.Entities.Project;

namespace TreeAgent.Web.Tests.Services;

[TestFixture]
public class AgentWorkflowServiceTests
{
    private TreeAgentDbContext _db = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private Mock<IRoadmapService> _mockRoadmapService = null!;
    private AgentWorkflowService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<TreeAgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TreeAgentDbContext(options);
        _mockRunner = new Mock<ICommandRunner>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();
        _mockRoadmapService = new Mock<IRoadmapService>();

        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _service = new AgentWorkflowService(_db, _mockRunner.Object, _mockRoadmapService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = _tempDir,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<Feature> CreateTestFeature(string projectId)
    {
        var feature = new Feature
        {
            ProjectId = projectId,
            Title = "Test Feature",
            BranchName = "feature/test",
            Status = FeatureStatus.Future
        };

        _db.Features.Add(feature);
        await _db.SaveChangesAsync();
        return feature;
    }

    private async Task<Agent> CreateTestAgent(string featureId, AgentStatus status = AgentStatus.Idle)
    {
        var agent = new Agent
        {
            FeatureId = featureId,
            Status = status
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        return agent;
    }

    #region 5.1 Agent Status Updates

    [Test]
    public async Task Agent_StartWork_PRMovesToInProgress()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        var agent = await CreateTestAgent(feature.Id);

        // Act
        await _service.OnAgentStartedAsync(agent.Id);

        // Assert
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.Status, Is.EqualTo(FeatureStatus.InDevelopment));
    }

    [Test]
    public async Task Agent_CompleteWork_PRMovesToReadyForReview()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.InDevelopment;
        await _db.SaveChangesAsync();

        var agent = await CreateTestAgent(feature.Id, AgentStatus.Running);

        // Act
        await _service.OnAgentCompletedAsync(agent.Id);

        // Assert
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.Status, Is.EqualTo(FeatureStatus.ReadyForReview));
    }

    [Test]
    public async Task Agent_RoadmapOnlyChange_CreatesPlanUpdatePR()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.InDevelopment;
        feature.BranchName = "plan-update/add-features";
        await _db.SaveChangesAsync();

        var agent = await CreateTestAgent(feature.Id, AgentStatus.Running);

        // Mock: only ROADMAP.json changed
        _mockRoadmapService.Setup(r => r.IsPlanUpdateOnlyAsync(feature.Id))
            .ReturnsAsync(true);

        // Act
        var isPlanUpdate = await _service.IsPlanUpdateOnlyAsync(agent.Id);

        // Assert
        Assert.That(isPlanUpdate, Is.True);
    }

    [Test]
    public async Task Agent_MixedChanges_IsNotPlanUpdateOnly()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.InDevelopment;
        await _db.SaveChangesAsync();

        var agent = await CreateTestAgent(feature.Id, AgentStatus.Running);

        // Mock: multiple files changed
        _mockRoadmapService.Setup(r => r.IsPlanUpdateOnlyAsync(feature.Id))
            .ReturnsAsync(false);

        // Act
        var isPlanUpdate = await _service.IsPlanUpdateOnlyAsync(agent.Id);

        // Assert
        Assert.That(isPlanUpdate, Is.False);
    }

    #endregion

    #region 5.2 Review Comment Handling

    [Test]
    public async Task ReviewComments_Received_FeatureMovesToInProgress()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.ReadyForReview;
        await _db.SaveChangesAsync();

        // Act
        await _service.OnReviewCommentsReceivedAsync(feature.Id);

        // Assert
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.Status, Is.EqualTo(FeatureStatus.InDevelopment));
    }

    [Test]
    public async Task ReviewComments_SpawnsNewAgent()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.ReadyForReview;
        await _db.SaveChangesAsync();

        var reviewComments = "Please fix the null check on line 42";

        // Act
        var newAgent = await _service.SpawnAgentForReviewAsync(feature.Id, reviewComments);

        // Assert
        Assert.That(newAgent, Is.Not.Null);
        Assert.That(newAgent!.FeatureId, Is.EqualTo(feature.Id));
        Assert.That(newAgent.SystemPrompt, Does.Contain(reviewComments));
    }

    [Test]
    public async Task ReviewComments_Resolved_MovesToReadyForReview()
    {
        // Arrange
        var project = await CreateTestProject();
        var feature = await CreateTestFeature(project.Id);
        feature.Status = FeatureStatus.InDevelopment;
        await _db.SaveChangesAsync();

        // Create an agent that will complete
        var agent = await CreateTestAgent(feature.Id, AgentStatus.Running);

        // Act - Simulate agent completion after addressing review
        await _service.OnAgentCompletedAsync(agent.Id);

        // Assert
        var updatedFeature = await _db.Features.FindAsync(feature.Id);
        Assert.That(updatedFeature!.Status, Is.EqualTo(FeatureStatus.ReadyForReview));
    }

    [Test]
    public async Task StartWorkOnFutureChange_CreatesFeatureAndAgent()
    {
        // Arrange
        var project = await CreateTestProject();
        var changeId = "new-feature";
        var mockFeature = new Feature
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            Title = "New Feature",
            BranchName = "core/feature/new-feature",
            Status = FeatureStatus.InDevelopment
        };

        _mockRoadmapService.Setup(r => r.PromoteChangeAsync(project.Id, changeId))
            .ReturnsAsync(mockFeature);

        // Mock the FindChangeByIdAsync to return the change with instructions
        var change = new RoadmapChange
        {
            Id = changeId,
            Group = "core",
            Type = ChangeType.Feature,
            Title = "New Feature",
            Instructions = "Implement the new feature following TDD"
        };
        _mockRoadmapService.Setup(r => r.FindChangeByIdAsync(project.Id, changeId))
            .ReturnsAsync(change);

        // Save the feature to the database for lookup
        _db.Features.Add(mockFeature);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.StartWorkOnFutureChangeAsync(project.Id, changeId);

        // Assert
        Assert.That(result.Feature, Is.Not.Null);
        Assert.That(result.Agent, Is.Not.Null);
        Assert.That(result.Agent!.FeatureId, Is.EqualTo(mockFeature.Id));
    }

    #endregion
}
