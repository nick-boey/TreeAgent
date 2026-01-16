using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class TestAgentServiceTests
{
    private Mock<IAgentHarnessFactory> _mockHarnessFactory = null!;
    private Mock<IAgentHarness> _mockHarness = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private Mock<IDataStore> _mockDataStore = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private TestAgentService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockHarnessFactory = new Mock<IAgentHarnessFactory>();
        _mockHarness = new Mock<IAgentHarness>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();
        _mockDataStore = new Mock<IDataStore>();
        _mockCommandRunner = new Mock<ICommandRunner>();

        // Default harness factory behavior
        _mockHarnessFactory.Setup(f => f.GetHarness("claudeui")).Returns(_mockHarness.Object);

        _service = new TestAgentService(
            _mockHarnessFactory.Object,
            _mockWorktreeService.Object,
            _mockDataStore.Object,
            _mockCommandRunner.Object,
            Mock.Of<ILogger<TestAgentService>>());
    }

    private static Project CreateTestProject(string id = "proj-123", string localPath = "/path/to/project", string defaultBranch = "main")
    {
        return new Project
        {
            Id = id,
            Name = "TestProject",
            LocalPath = localPath,
            DefaultBranch = defaultBranch,
            GitHubOwner = null,
            GitHubRepo = null
        };
    }

    private static AgentInstance CreateTestAgentInstance(string entityId = "test-agent-proj-123")
    {
        return new AgentInstance
        {
            AgentId = "agent-123",
            EntityId = entityId,
            HarnessType = "claudeui",
            WorkingDirectory = "/path/to/hsp/test",
            Status = AgentInstanceStatus.Running,
            WebViewUrl = "http://localhost:5000",
            ActiveSessionId = "ses_test123"
        };
    }

    [Test]
    public async Task StartTestAgentAsync_ProjectNotFound_ReturnsError()
    {
        // Arrange
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns((Project?)null);

        // Act
        var result = await _service.StartTestAgentAsync("proj-123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
    }

    [Test]
    public async Task StartTestAgentAsync_WorktreeCreationFails_ReturnsError()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.StartTestAgentAsync("proj-123");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("worktree"));
    }

    [Test]
    public async Task StartTestAgentAsync_Success_CreatesWorktreeAndStartsAgent()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);

        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");

        _mockHarness
            .Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        // Act
        var result = await _service.StartTestAgentAsync("proj-123");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.WorktreePath, Is.Not.Null);
    }

    [Test]
    public async Task StartTestAgentAsync_Success_TracksStatus()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);

        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");

        _mockHarness
            .Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        // Act
        await _service.StartTestAgentAsync("proj-123");
        var status = _service.GetTestAgentStatus("proj-123");

        // Assert
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.ProjectId, Is.EqualTo("proj-123"));
        Assert.That(status.WebViewUrl, Is.Not.Null);
    }

    [Test]
    public async Task StopTestAgentAsync_StopsAgentAndCleansUpWorktree()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService.Setup(w => w.RemoveWorktreeAsync("/path/to/project", "hsp/test")).ReturnsAsync(true);
        _mockCommandRunner
            .Setup(c => c.RunAsync("git", "branch -D \"hsp/test\"", "/path/to/project"))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.StopTestAgentAsync("proj-123");

        // Assert
        _mockHarness.Verify(h => h.StopAgentAsync("test-agent-proj-123", default), Times.Once);
        _mockWorktreeService.Verify(w => w.RemoveWorktreeAsync("/path/to/project", "hsp/test"), Times.Once);
        _mockCommandRunner.Verify(c => c.RunAsync("git", "branch -D \"hsp/test\"", "/path/to/project"), Times.Once);
    }

    [Test]
    public async Task StopTestAgentAsync_RemovesFromTracking()
    {
        // Arrange - First start an agent
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);

        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");

        _mockHarness
            .Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        await _service.StartTestAgentAsync("proj-123");
        Assert.That(_service.GetTestAgentStatus("proj-123"), Is.Not.Null);

        // Setup for stop
        _mockWorktreeService.Setup(w => w.RemoveWorktreeAsync(It.IsAny<string>(), "hsp/test")).ReturnsAsync(true);
        _mockCommandRunner.Setup(c => c.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.StopTestAgentAsync("proj-123");

        // Assert
        Assert.That(_service.GetTestAgentStatus("proj-123"), Is.Null);
    }

    [Test]
    public async Task VerifySessionVisibilityAsync_NoActiveAgent_ReturnsError()
    {
        // Act
        var result = await _service.VerifySessionVisibilityAsync("proj-123");

        // Assert
        Assert.That(result.SessionFound, Is.False);
        Assert.That(result.Error, Does.Contain("No active test agent"));
    }

    [Test]
    public async Task VerifySessionVisibilityAsync_AgentFound_ReturnsTrue()
    {
        // Arrange - First start a test agent
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), true, "main"))
            .ReturnsAsync("/path/to/worktree");
        _mockHarness.Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        await _service.StartTestAgentAsync("proj-123");

        // Setup agent lookup
        _mockHarness.Setup(h => h.GetAgentForEntity("test-agent-proj-123"))
            .Returns(CreateTestAgentInstance());

        // Act
        var result = await _service.VerifySessionVisibilityAsync("proj-123");

        // Assert
        Assert.That(result.SessionFound, Is.True);
        Assert.That(result.SessionId, Is.EqualTo("ses_test123"));
    }

    [Test]
    public async Task VerifySessionVisibilityAsync_AgentNotFoundInHarness_ReturnsFalse()
    {
        // Arrange - First start a test agent
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), true, "main"))
            .ReturnsAsync("/path/to/worktree");
        _mockHarness.Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        await _service.StartTestAgentAsync("proj-123");

        // Setup - agent not found in harness
        _mockHarness.Setup(h => h.GetAgentForEntity("test-agent-proj-123"))
            .Returns((AgentInstance?)null);

        // Act
        var result = await _service.VerifySessionVisibilityAsync("proj-123");

        // Assert
        Assert.That(result.SessionFound, Is.False);
        Assert.That(result.Error, Does.Contain("Agent not found"));
    }

    [Test]
    public void GetTestAgentStatus_NoActiveAgent_ReturnsNull()
    {
        // Act
        var status = _service.GetTestAgentStatus("proj-123");

        // Assert
        Assert.That(status, Is.Null);
    }

    [Test]
    public async Task StartTestAgentAsync_FetchesBaseBranchBeforeCreatingWorktree()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);

        _mockWorktreeService
            .Setup(w => w.FetchAndUpdateBranchAsync("/path/to/project", "main"))
            .ReturnsAsync(true);

        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");

        _mockHarness
            .Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        // Act
        var result = await _service.StartTestAgentAsync("proj-123");

        // Assert
        Assert.That(result.Success, Is.True);
        _mockWorktreeService.Verify(
            w => w.FetchAndUpdateBranchAsync("/path/to/project", "main"),
            Times.Once,
            "FetchAndUpdateBranchAsync should be called before creating a new branch");
    }

    [Test]
    public async Task StartTestAgentAsync_ContinuesEvenIfFetchFails()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);

        // Fetch fails but should not block worktree creation
        _mockWorktreeService
            .Setup(w => w.FetchAndUpdateBranchAsync("/path/to/project", "main"))
            .ReturnsAsync(false);

        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");

        _mockHarness
            .Setup(h => h.StartAgentAsync(It.IsAny<AgentStartOptions>(), default))
            .ReturnsAsync(CreateTestAgentInstance());

        // Act
        var result = await _service.StartTestAgentAsync("proj-123");

        // Assert
        Assert.That(result.Success, Is.True, "Should succeed even if fetch fails");
        _mockWorktreeService.Verify(
            w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"),
            Times.Once,
            "CreateWorktreeAsync should still be called after fetch failure");
    }
}
