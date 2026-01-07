using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentHubTests
{
    private Mock<IAgentWorkflowService> _workflowServiceMock = null!;
    private Mock<IOpenCodeServerManager> _serverManagerMock = null!;
    private Mock<IOpenCodeClient> _openCodeClientMock = null!;
    private Mock<IGroupManager> _groupsMock = null!;
    private Mock<HubCallerContext> _contextMock = null!;
    private AgentHub _hub = null!;

    [SetUp]
    public void SetUp()
    {
        _workflowServiceMock = new Mock<IAgentWorkflowService>();
        _serverManagerMock = new Mock<IOpenCodeServerManager>();
        _openCodeClientMock = new Mock<IOpenCodeClient>();
        _groupsMock = new Mock<IGroupManager>();
        _contextMock = new Mock<HubCallerContext>();
        
        _contextMock.Setup(c => c.ConnectionId).Returns("test-connection-id");
        
        _hub = new AgentHub(
            _workflowServiceMock.Object,
            _serverManagerMock.Object,
            _openCodeClientMock.Object)
        {
            Groups = _groupsMock.Object,
            Context = _contextMock.Object
        };
    }

    [Test]
    public async Task JoinGlobalGroup_AddsConnectionToGlobalGroup()
    {
        // Act
        await _hub.JoinGlobalGroup();
        
        // Assert
        _groupsMock.Verify(g => g.AddToGroupAsync(
            "test-connection-id", 
            AgentHub.GlobalGroupName, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Test]
    public async Task LeaveGlobalGroup_RemovesConnectionFromGlobalGroup()
    {
        // Act
        await _hub.LeaveGlobalGroup();
        
        // Assert
        _groupsMock.Verify(g => g.RemoveFromGroupAsync(
            "test-connection-id", 
            AgentHub.GlobalGroupName, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Test]
    public void GetAllRunningServers_ReturnsEmptyList_WhenNoServersRunning()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.GetRunningServers())
            .Returns(new List<OpenCodeServer>());
        
        // Act
        var result = _hub.GetAllRunningServers();
        
        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetAllRunningServers_ReturnsRunningServerInfo()
    {
        // Arrange
        var servers = new List<OpenCodeServer>
        {
            new()
            {
                EntityId = "entity-1",
                WorktreePath = @"C:\test\path1",
                Port = 4099,
                ActiveSessionId = "ses_123"
            },
            new()
            {
                EntityId = "entity-2",
                WorktreePath = @"C:\test\path2",
                Port = 4100,
                ActiveSessionId = null
            }
        };
        
        _serverManagerMock.Setup(m => m.GetRunningServers()).Returns(servers);
        
        // Act
        var result = _hub.GetAllRunningServers();
        
        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        
        var first = result.First(r => r.EntityId == "entity-1");
        Assert.That(first.Port, Is.EqualTo(4099));
        Assert.That(first.WebViewUrl, Is.Not.Null);
        Assert.That(first.WebViewUrl, Does.Contain("/session/ses_123"));
        
        var second = result.First(r => r.EntityId == "entity-2");
        Assert.That(second.Port, Is.EqualTo(4100));
        Assert.That(second.WebViewUrl, Is.Null);
    }

    [Test]
    public void GetAllRunningServers_MapsAllProperties()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var server = new OpenCodeServer
        {
            EntityId = "test-entity",
            WorktreePath = @"C:\my\worktree",
            Port = 4500,
            ActiveSessionId = "ses_abc",
            StartedAt = startTime
        };
        
        _serverManagerMock.Setup(m => m.GetRunningServers())
            .Returns(new List<OpenCodeServer> { server });
        
        // Act
        var result = _hub.GetAllRunningServers();
        
        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var info = result[0];
        Assert.That(info.EntityId, Is.EqualTo("test-entity"));
        Assert.That(info.WorktreePath, Is.EqualTo(@"C:\my\worktree"));
        Assert.That(info.Port, Is.EqualTo(4500));
        Assert.That(info.BaseUrl, Is.EqualTo("http://127.0.0.1:4500"));
        Assert.That(info.ActiveSessionId, Is.EqualTo("ses_abc"));
        Assert.That(info.StartedAt, Is.EqualTo(startTime));
        Assert.That(info.WebViewUrl, Is.Not.Null);
    }

    [Test]
    public async Task GetSessionsForServer_WithValidServer_ReturnsSessions()
    {
        // Arrange
        var server = new OpenCodeServer
        {
            EntityId = "entity-1",
            WorktreePath = @"C:\test",
            Port = 4099
        };
        
        var sessions = new List<OpenCodeSession>
        {
            new() { Id = "ses_1", Title = "Session 1" },
            new() { Id = "ses_2", Title = "Session 2" }
        };
        
        _serverManagerMock.Setup(m => m.GetServerForEntity("entity-1")).Returns(server);
        _openCodeClientMock.Setup(c => c.ListSessionsAsync(server.BaseUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);
        
        // Act
        var result = await _hub.GetSessionsForServer("entity-1");
        
        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("ses_1"));
        Assert.That(result[1].Id, Is.EqualTo("ses_2"));
    }

    [Test]
    public async Task GetSessionsForServer_WithNoServer_ReturnsEmptyList()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.GetServerForEntity("unknown")).Returns((OpenCodeServer?)null);
        
        // Act
        var result = await _hub.GetSessionsForServer("unknown");
        
        // Assert
        Assert.That(result, Is.Empty);
        
        // Verify we didn't try to call the client
        _openCodeClientMock.Verify(
            c => c.ListSessionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Test]
    public async Task GetSessionsForServer_CallsClientWithCorrectBaseUrl()
    {
        // Arrange
        var server = new OpenCodeServer
        {
            EntityId = "entity-1",
            WorktreePath = @"C:\test",
            Port = 5555
        };
        
        _serverManagerMock.Setup(m => m.GetServerForEntity("entity-1")).Returns(server);
        _openCodeClientMock.Setup(c => c.ListSessionsAsync("http://127.0.0.1:5555", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenCodeSession>());
        
        // Act
        await _hub.GetSessionsForServer("entity-1");
        
        // Assert
        _openCodeClientMock.Verify(
            c => c.ListSessionsAsync("http://127.0.0.1:5555", It.IsAny<CancellationToken>()), 
            Times.Once);
    }
}

[TestFixture]
public class AgentHubExtensionsTests
{
    [Test]
    public async Task BroadcastServerListChanged_SendsToGlobalGroup()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<AgentHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(AgentHub.GlobalGroupName)).Returns(clientProxyMock.Object);
        
        var servers = new List<RunningServerInfo>
        {
            new()
            {
                EntityId = "test",
                Port = 4099,
                BaseUrl = "http://127.0.0.1:4099",
                WorktreePath = @"C:\test",
                StartedAt = DateTime.UtcNow
            }
        };
        
        // Act
        await hubContextMock.Object.BroadcastServerListChanged(servers);
        
        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "ServerListChanged",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == servers),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BroadcastServerListChanged_WithEmptyList_StillBroadcasts()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<AgentHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(AgentHub.GlobalGroupName)).Returns(clientProxyMock.Object);
        
        var servers = new List<RunningServerInfo>();
        
        // Act
        await hubContextMock.Object.BroadcastServerListChanged(servers);
        
        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "ServerListChanged",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
