using Homespun.Features.GitHub;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class OpenCodeServerManagerTests
{
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<IPortAllocationService> _mockPortAllocationService = null!;
    private Mock<IGitHubEnvironmentService> _mockGitHubEnvironmentService = null!;
    private Mock<ILogger<OpenCodeServerManager>> _mockLogger = null!;
    private IOptions<OpenCodeOptions> _options = null!;
    private OpenCodeServerManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _mockClient = new Mock<IOpenCodeClient>();
        _mockPortAllocationService = new Mock<IPortAllocationService>();
        _mockGitHubEnvironmentService = new Mock<IGitHubEnvironmentService>();
        _mockLogger = new Mock<ILogger<OpenCodeServerManager>>();
        _options = Options.Create(new OpenCodeOptions
        {
            BasePort = 5000,
            MaxConcurrentServers = 3,
            ServerStartTimeoutMs = 1000,
            ExecutablePath = "opencode"
        });

        // Default port allocation behavior
        var nextPort = 5000;
        _mockPortAllocationService.Setup(p => p.AllocatePort()).Returns(() => nextPort++);

        // Default GitHub environment (empty)
        _mockGitHubEnvironmentService.Setup(g => g.GetGitHubEnvironment())
            .Returns(new Dictionary<string, string>());

        _manager = new OpenCodeServerManager(
            _options,
            _mockClient.Object,
            _mockPortAllocationService.Object,
            Mock.Of<IHubContext<AgentHub>>(),
            _mockGitHubEnvironmentService.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.Dispose();
    }

    [Test]
    public void GetServerForEntity_ReturnsNull_WhenNoServerRunning()
    {
        var server = _manager.GetServerForEntity("pr-123");
        Assert.That(server, Is.Null);
    }

    [Test]
    public void GetRunningServers_ReturnsEmptyList_Initially()
    {
        var servers = _manager.GetRunningServers();
        Assert.That(servers, Is.Empty);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_WhenClientReturnsHealthy()
    {
        var server = new OpenCodeServer
        {
            EntityId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000
        };
        
        _mockClient.Setup(c => c.GetHealthAsync(server.BaseUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthResponse { Healthy = true, Version = "1.0.0" });

        var result = await _manager.IsHealthyAsync(server);
        
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsFalse_WhenClientThrows()
    {
        var server = new OpenCodeServer
        {
            EntityId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000
        };
        
        _mockClient.Setup(c => c.GetHealthAsync(server.BaseUrl, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _manager.IsHealthyAsync(server);
        
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsFalse_WhenHealthResponseIsUnhealthy()
    {
        var server = new OpenCodeServer
        {
            EntityId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000
        };
        
        _mockClient.Setup(c => c.GetHealthAsync(server.BaseUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthResponse { Healthy = false, Version = "1.0.0" });

        var result = await _manager.IsHealthyAsync(server);
        
        Assert.That(result, Is.False);
    }

    #region ContinueSession Tests

    [Test]
    public void OpenCodeServer_ContinueSession_DefaultsToFalse()
    {
        var server = new OpenCodeServer
        {
            EntityId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000
        };
        
        Assert.That(server.ContinueSession, Is.False);
    }

    [Test]
    public void OpenCodeServer_ContinueSession_CanBeSetToTrue()
    {
        var server = new OpenCodeServer
        {
            EntityId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000,
            ContinueSession = true
        };
        
        Assert.That(server.ContinueSession, Is.True);
    }

    #endregion
}
