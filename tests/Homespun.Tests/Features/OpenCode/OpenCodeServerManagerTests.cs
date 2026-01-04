using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class OpenCodeServerManagerTests
{
    private Mock<IOpenCodeClient> _mockClient = null!;
    private Mock<ILogger<OpenCodeServerManager>> _mockLogger = null!;
    private IOptions<OpenCodeOptions> _options = null!;
    private OpenCodeServerManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _mockClient = new Mock<IOpenCodeClient>();
        _mockLogger = new Mock<ILogger<OpenCodeServerManager>>();
        _options = Options.Create(new OpenCodeOptions
        {
            BasePort = 5000,
            MaxConcurrentServers = 3,
            ServerStartTimeoutMs = 1000,
            ExecutablePath = "opencode"
        });
        _manager = new OpenCodeServerManager(_options, _mockClient.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.Dispose();
    }

    [Test]
    public void AllocatePort_ReturnsBasePort_WhenNoServersRunning()
    {
        var port = _manager.AllocatePort();
        Assert.That(port, Is.EqualTo(5000));
    }

    [Test]
    public void AllocatePort_ReturnsNextPort_AfterFirstAllocation()
    {
        _manager.AllocatePort();
        var port = _manager.AllocatePort();
        Assert.That(port, Is.EqualTo(5001));
    }

    [Test]
    public void AllocatePort_ReusesReleasedPort()
    {
        var port1 = _manager.AllocatePort();
        _manager.AllocatePort();
        _manager.ReleasePort(port1);
        
        var port3 = _manager.AllocatePort();
        Assert.That(port3, Is.EqualTo(port1));
    }

    [Test]
    public void AllocatePort_ThrowsWhenMaxServersReached()
    {
        _manager.AllocatePort();
        _manager.AllocatePort();
        _manager.AllocatePort();
        
        Assert.Throws<InvalidOperationException>(() => _manager.AllocatePort());
    }

    [Test]
    public void GetServerForPullRequest_ReturnsNull_WhenNoServerRunning()
    {
        var server = _manager.GetServerForPullRequest("pr-123");
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
            PullRequestId = "pr-123",
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
            PullRequestId = "pr-123",
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
            PullRequestId = "pr-123",
            WorktreePath = "/path/to/worktree",
            Port = 5000
        };
        
        _mockClient.Setup(c => c.GetHealthAsync(server.BaseUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthResponse { Healthy = false, Version = "1.0.0" });

        var result = await _manager.IsHealthyAsync(server);
        
        Assert.That(result, Is.False);
    }
}
