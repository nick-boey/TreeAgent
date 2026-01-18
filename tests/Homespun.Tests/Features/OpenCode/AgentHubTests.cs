using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Abstractions.Models;
using Homespun.Features.Agents.Hubs;
using Homespun.Features.Agents.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

[TestFixture]
public class AgentHubTests
{
    private Mock<IAgentWorkflowService> _workflowServiceMock = null!;
    private Mock<IAgentHarnessFactory> _harnessFactoryMock = null!;
    private Mock<IGroupManager> _groupsMock = null!;
    private Mock<HubCallerContext> _contextMock = null!;
    private AgentHub _hub = null!;

    [SetUp]
    public void SetUp()
    {
        _workflowServiceMock = new Mock<IAgentWorkflowService>();
        _harnessFactoryMock = new Mock<IAgentHarnessFactory>();
        _groupsMock = new Mock<IGroupManager>();
        _contextMock = new Mock<HubCallerContext>();

        _contextMock.Setup(c => c.ConnectionId).Returns("test-connection-id");

        _hub = new AgentHub(
            _workflowServiceMock.Object,
            _harnessFactoryMock.Object)
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
    public async Task JoinAgentGroup_AddsConnectionToEntityGroup()
    {
        // Act
        await _hub.JoinAgentGroup("entity-123");

        // Assert
        _groupsMock.Verify(g => g.AddToGroupAsync(
            "test-connection-id",
            "entity-123",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task LeaveAgentGroup_RemovesConnectionFromEntityGroup()
    {
        // Act
        await _hub.LeaveAgentGroup("entity-123");

        // Assert
        _groupsMock.Verify(g => g.RemoveFromGroupAsync(
            "test-connection-id",
            "entity-123",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void GetAllRunningAgents_ReturnsEmptyList_WhenNoAgentsRunning()
    {
        // Arrange
        _workflowServiceMock.Setup(m => m.GetAllRunningAgents())
            .Returns(new List<AgentInstance>());

        // Act
        var result = _hub.GetAllRunningAgents();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetAllRunningAgents_ReturnsRunningAgentInfo()
    {
        // Arrange
        var agents = new List<AgentInstance>
        {
            new()
            {
                AgentId = "agent-1",
                EntityId = "entity-1",
                HarnessType = "claudeui",
                WorkingDirectory = @"C:\test\path1",
                Status = AgentInstanceStatus.Running,
                ActiveSessionId = "ses_123",
                WebViewUrl = "http://localhost:4099/session/ses_123"
            },
            new()
            {
                AgentId = "agent-2",
                EntityId = "entity-2",
                HarnessType = "claudeui",
                WorkingDirectory = @"C:\test\path2",
                Status = AgentInstanceStatus.Running,
                ActiveSessionId = null,
                WebViewUrl = null
            }
        };

        _workflowServiceMock.Setup(m => m.GetAllRunningAgents()).Returns(agents);

        // Act
        var result = _hub.GetAllRunningAgents();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));

        var first = result.First(r => r.EntityId == "entity-1");
        Assert.That(first.WebViewUrl, Does.Contain("/session/ses_123"));

        var second = result.First(r => r.EntityId == "entity-2");
        Assert.That(second.WebViewUrl, Is.Null);
    }

    [Test]
    public void GetAvailableHarnessTypes_ReturnsHarnessTypes()
    {
        // Arrange
        _harnessFactoryMock.Setup(f => f.AvailableHarnessTypes)
            .Returns(new List<string> { "opencode", "claudeui" });

        // Act
        var result = _hub.GetAvailableHarnessTypes();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("opencode"));
        Assert.That(result, Does.Contain("claudeui"));
    }

    [Test]
    public void GetDefaultHarnessType_ReturnsDefault()
    {
        // Arrange
        _harnessFactoryMock.Setup(f => f.DefaultHarnessType).Returns("claudeui");

        // Act
        var result = _hub.GetDefaultHarnessType();

        // Assert
        Assert.That(result, Is.EqualTo("claudeui"));
    }

    [Test]
    public async Task SendPromptStreaming_YieldsEventsFromWorkflowService()
    {
        // Arrange
        var expectedEvents = new List<AgentEvent>
        {
            new() { Type = AgentEventTypes.MessageCreated, AgentId = "agent-1", Content = "Hello" },
            new() { Type = AgentEventTypes.ToolStarted, AgentId = "agent-1", ToolName = "Bash" },
            new() { Type = AgentEventTypes.ToolCompleted, AgentId = "agent-1", ToolName = "Bash" }
        };

        _workflowServiceMock.Setup(m => m.SendPromptStreamingAsync("entity-1", "test prompt", It.IsAny<CancellationToken>()))
            .Returns(expectedEvents.ToAsyncEnumerable());

        // Setup clients mock for broadcasting
        var clientsMock = new Mock<IHubCallerClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Group("entity-1")).Returns(clientProxyMock.Object);
        _hub.Clients = clientsMock.Object;

        // Act
        var receivedEvents = new List<AgentEvent>();
        await foreach (var evt in _hub.SendPromptStreaming("entity-1", "test prompt"))
        {
            receivedEvents.Add(evt);
        }

        // Assert
        Assert.That(receivedEvents, Has.Count.EqualTo(3));
        Assert.That(receivedEvents[0].Type, Is.EqualTo(AgentEventTypes.MessageCreated));
        Assert.That(receivedEvents[1].Type, Is.EqualTo(AgentEventTypes.ToolStarted));
        Assert.That(receivedEvents[2].Type, Is.EqualTo(AgentEventTypes.ToolCompleted));
    }

    [Test]
    public async Task SendPromptStreaming_BroadcastsEachEventToGroup()
    {
        // Arrange
        var expectedEvents = new List<AgentEvent>
        {
            new() { Type = AgentEventTypes.MessageCreated, AgentId = "agent-1" },
            new() { Type = AgentEventTypes.ToolCompleted, AgentId = "agent-1" }
        };

        _workflowServiceMock.Setup(m => m.SendPromptStreamingAsync("entity-1", "test prompt", It.IsAny<CancellationToken>()))
            .Returns(expectedEvents.ToAsyncEnumerable());

        // Setup clients mock for broadcasting
        var clientsMock = new Mock<IHubCallerClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Group("entity-1")).Returns(clientProxyMock.Object);
        _hub.Clients = clientsMock.Object;

        // Act
        await foreach (var _ in _hub.SendPromptStreaming("entity-1", "test prompt"))
        {
            // Consume the stream
        }

        // Assert - verify broadcast was called for each event
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "AgentEvent",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}

[TestFixture]
public class AgentHubExtensionsTests
{
    [Test]
    public async Task BroadcastAgentListChanged_SendsToGlobalGroup()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<AgentHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(AgentHub.GlobalGroupName)).Returns(clientProxyMock.Object);

        var agents = new List<RunningAgentInfo>
        {
            new()
            {
                EntityId = "test",
                HarnessType = "claudeui",
                WorkingDirectory = @"C:\test",
                StartedAt = DateTime.UtcNow,
                WebViewUrl = "http://localhost:4099"
            }
        };

        // Act
        await hubContextMock.Object.BroadcastAgentListChanged(agents);

        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "AgentListChanged",
            It.Is<object?[]>(args => args.Length == 1 && args[0] == agents),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BroadcastAgentListChanged_WithEmptyList_StillBroadcasts()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<AgentHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(AgentHub.GlobalGroupName)).Returns(clientProxyMock.Object);

        var agents = new List<RunningAgentInfo>();

        // Act
        await hubContextMock.Object.BroadcastAgentListChanged(agents);

        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "AgentListChanged",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task BroadcastAgentStartupStateChanged_SendsToGlobalGroup()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<AgentHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();

        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(AgentHub.GlobalGroupName)).Returns(clientProxyMock.Object);

        // Act
        await hubContextMock.Object.BroadcastAgentStartupStateChanged("entity-1", AgentInstanceStatus.Running);

        // Assert
        clientProxyMock.Verify(c => c.SendCoreAsync(
            "AgentStartupStateChanged",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
