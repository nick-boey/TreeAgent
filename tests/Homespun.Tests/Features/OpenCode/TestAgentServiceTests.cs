using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class TestAgentServiceTests
{
    private Mock<IOpenCodeServerManager> _mockServerManager = null!;
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private Mock<IDataStore> _mockDataStore = null!;
    private Mock<IOpenCodeConfigGenerator> _mockConfigGenerator = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private TestAgentService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServerManager = new Mock<IOpenCodeServerManager>();
        _mockClient = new Mock<IOpenCodeClient>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();
        _mockDataStore = new Mock<IDataStore>();
        _mockConfigGenerator = new Mock<IOpenCodeConfigGenerator>();
        _mockCommandRunner = new Mock<ICommandRunner>();

        _service = new TestAgentService(
            _mockServerManager.Object,
            _mockClient.Object,
            _mockWorktreeService.Object,
            _mockDataStore.Object,
            _mockConfigGenerator.Object,
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

    private static OpenCodeServer CreateTestServer(string entityId = "test-agent-proj-123", int port = 4099)
    {
        return new OpenCodeServer
        {
            EntityId = entityId,
            WorktreePath = "/path/to/hsp/test",
            Port = port,
            Status = OpenCodeServerStatus.Running
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
    public async Task StartTestAgentAsync_Success_CreatesWorktreeAndStartsServer()
    {
        // Arrange
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        
        _mockWorktreeService
            .Setup(w => w.CreateWorktreeAsync("/path/to/project", "hsp/test", true, "main"))
            .ReturnsAsync("/path/to/hsp/test");
        
        _mockServerManager
            .Setup(s => s.StartServerAsync("test-agent-proj-123", It.IsAny<string>(), false, default))
            .ReturnsAsync(CreateTestServer());
        
        _mockClient
            .Setup(c => c.CreateSessionAsync("http://127.0.0.1:4099", "Test Agent Session", default))
            .ReturnsAsync(new OpenCodeSession { Id = "ses_test123", Title = "Test Agent Session" });
        
        // Act
        var result = await _service.StartTestAgentAsync("proj-123");
        
        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.ServerUrl, Is.EqualTo("http://127.0.0.1:4099"));
        Assert.That(result.SessionId, Is.EqualTo("ses_test123"));
        
        // Verify prompt was sent containing test.txt
        _mockClient.Verify(c => c.SendPromptAsyncNoWait(
            "http://127.0.0.1:4099",
            "ses_test123",
            It.Is<PromptRequest>(r => r.Parts[0].Text!.Contains("test.txt")),
            default), Times.Once);
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
        
        _mockServerManager
            .Setup(s => s.StartServerAsync(It.IsAny<string>(), It.IsAny<string>(), false, default))
            .ReturnsAsync(CreateTestServer());
        
        _mockClient
            .Setup(c => c.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new OpenCodeSession { Id = "ses_test123", Title = "Test" });
        
        // Act
        await _service.StartTestAgentAsync("proj-123");
        var status = _service.GetTestAgentStatus("proj-123");
        
        // Assert
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.ProjectId, Is.EqualTo("proj-123"));
        Assert.That(status.ServerUrl, Is.EqualTo("http://127.0.0.1:4099"));
        Assert.That(status.SessionId, Is.EqualTo("ses_test123"));
    }

    [Test]
    public async Task StopTestAgentAsync_StopsServerAndCleansUpWorktree()
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
        _mockServerManager.Verify(s => s.StopServerAsync("test-agent-proj-123", default), Times.Once);
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
        
        _mockServerManager
            .Setup(s => s.StartServerAsync(It.IsAny<string>(), It.IsAny<string>(), false, default))
            .ReturnsAsync(CreateTestServer());
        
        _mockClient
            .Setup(c => c.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new OpenCodeSession { Id = "ses_test123", Title = "Test" });
        
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
    public async Task VerifySessionVisibilityAsync_SessionFound_ReturnsTrue()
    {
        // Arrange - First start a test agent
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), true, "main"))
            .ReturnsAsync("/path/to/worktree");
        _mockServerManager.Setup(s => s.StartServerAsync(It.IsAny<string>(), It.IsAny<string>(), false, default))
            .ReturnsAsync(CreateTestServer());
        _mockClient.Setup(c => c.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new OpenCodeSession { Id = "ses_test123", Title = "Test" });
        
        await _service.StartTestAgentAsync("proj-123");
        
        // Setup session list
        _mockClient
            .Setup(c => c.ListSessionsAsync("http://127.0.0.1:4099", default))
            .ReturnsAsync([
                new OpenCodeSession { Id = "ses_test123", Title = "Test Agent Session" },
                new OpenCodeSession { Id = "ses_other", Title = "Other Session" }
            ]);
        
        // Act
        var result = await _service.VerifySessionVisibilityAsync("proj-123");
        
        // Assert
        Assert.That(result.SessionFound, Is.True);
        Assert.That(result.TotalSessions, Is.EqualTo(2));
        Assert.That(result.SessionId, Is.EqualTo("ses_test123"));
        Assert.That(result.SessionTitle, Is.EqualTo("Test Agent Session"));
        Assert.That(result.AllSessionIds, Contains.Item("ses_test123"));
        Assert.That(result.AllSessionIds, Contains.Item("ses_other"));
    }

    [Test]
    public async Task VerifySessionVisibilityAsync_SessionNotFound_ReturnsFalse()
    {
        // Arrange - First start a test agent
        var project = CreateTestProject();
        _mockDataStore.Setup(d => d.GetProject("proj-123")).Returns(project);
        _mockWorktreeService.Setup(w => w.CreateWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), true, "main"))
            .ReturnsAsync("/path/to/worktree");
        _mockServerManager.Setup(s => s.StartServerAsync(It.IsAny<string>(), It.IsAny<string>(), false, default))
            .ReturnsAsync(CreateTestServer());
        _mockClient.Setup(c => c.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new OpenCodeSession { Id = "ses_test123", Title = "Test" });
        
        await _service.StartTestAgentAsync("proj-123");
        
        // Setup session list - doesn't contain our session
        _mockClient
            .Setup(c => c.ListSessionsAsync("http://127.0.0.1:4099", default))
            .ReturnsAsync([
                new OpenCodeSession { Id = "ses_different", Title = "Different Session" }
            ]);
        
        // Act
        var result = await _service.VerifySessionVisibilityAsync("proj-123");
        
        // Assert
        Assert.That(result.SessionFound, Is.False);
        Assert.That(result.TotalSessions, Is.EqualTo(1));
    }

    [Test]
    public void GetTestAgentStatus_NoActiveAgent_ReturnsNull()
    {
        // Act
        var status = _service.GetTestAgentStatus("proj-123");
        
        // Assert
        Assert.That(status, Is.Null);
    }
}
