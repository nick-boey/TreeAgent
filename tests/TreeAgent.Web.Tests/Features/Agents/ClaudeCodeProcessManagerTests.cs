using Moq;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.Agents.Services;

namespace TreeAgent.Web.Tests.Features.Agents;

[TestFixture]
public class ClaudeCodeProcessManagerTests
{
    private Mock<IClaudeCodeProcessFactory> _mockFactory = null!;
    private Mock<IClaudeCodeProcess> _mockProcess = null!;

    [SetUp]
    public void SetUp()
    {
        _mockFactory = new Mock<IClaudeCodeProcessFactory>();
        _mockProcess = new Mock<IClaudeCodeProcess>();

        _mockProcess.Setup(p => p.IsRunning).Returns(true);
        _mockProcess.Setup(p => p.Status).Returns(AgentStatus.Running);
        _mockProcess.Setup(p => p.StartAsync()).Returns(Task.CompletedTask);
        _mockProcess.Setup(p => p.StopAsync()).Returns(Task.CompletedTask);
        _mockProcess.Setup(p => p.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        _mockFactory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(_mockProcess.Object);
    }

    [Test]
    public async Task StartAgent_CreatesNewProcess()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-1";
        var workingDirectory = "/tmp/test";

        // Act
        var result = await manager.StartAgentAsync(agentId, workingDirectory);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(manager.IsAgentRunning(agentId), Is.True);
        _mockFactory.Verify(f => f.Create(agentId, workingDirectory, null), Times.Once);
        _mockProcess.Verify(p => p.StartAsync(), Times.Once);
    }

    [Test]
    public async Task StartAgent_WithSystemPrompt_PassesPromptToFactory()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-2";
        var workingDirectory = "/tmp/test";
        var systemPrompt = "You are a helpful coding assistant.";

        // Act
        var result = await manager.StartAgentAsync(agentId, workingDirectory, systemPrompt);

        // Assert
        Assert.That(result, Is.True);
        _mockFactory.Verify(f => f.Create(agentId, workingDirectory, systemPrompt), Times.Once);
    }

    [Test]
    public async Task StartAgent_DuplicateId_ReturnsFalse()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-3";
        await manager.StartAgentAsync(agentId, "/tmp/test");

        // Act
        var result = await manager.StartAgentAsync(agentId, "/tmp/test");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task StopAgent_TerminatesProcess()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-4";
        await manager.StartAgentAsync(agentId, "/tmp/test");

        // Act
        var result = await manager.StopAgentAsync(agentId);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(manager.IsAgentRunning(agentId), Is.False);
        _mockProcess.Verify(p => p.StopAsync(), Times.Once);
        _mockProcess.Verify(p => p.Dispose(), Times.Once);
    }

    [Test]
    public async Task StopAgent_NonExistent_ReturnsFalse()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);

        // Act
        var result = await manager.StopAgentAsync("non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendMessage_ToRunningAgent_ReturnsTrue()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-5";
        await manager.StartAgentAsync(agentId, "/tmp/test");

        // Act
        var result = await manager.SendMessageAsync(agentId, "Hello, Claude!");

        // Assert
        Assert.That(result, Is.True);
        _mockProcess.Verify(p => p.SendMessageAsync("Hello, Claude!"), Times.Once);
    }

    [Test]
    public async Task SendMessage_ToNonExistentAgent_ReturnsFalse()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);

        // Act
        var result = await manager.SendMessageAsync("non-existent", "Hello");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetAgentStatus_NonExistent_ReturnsStopped()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);

        // Act
        var status = manager.GetAgentStatus("non-existent");

        // Assert
        Assert.That(status, Is.EqualTo(AgentStatus.Stopped));
    }

    [Test]
    public async Task GetAgentStatus_RunningAgent_ReturnsRunning()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        var agentId = "test-agent-6";
        await manager.StartAgentAsync(agentId, "/tmp/test");

        // Act
        var status = manager.GetAgentStatus(agentId);

        // Assert
        Assert.That(status, Is.EqualTo(AgentStatus.Running));
    }

    [Test]
    public async Task GetAllAgentIds_ReturnsAllTrackedAgents()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        await manager.StartAgentAsync("agent-1", "/tmp/test");
        await manager.StartAgentAsync("agent-2", "/tmp/test");

        // Act
        var agentIds = manager.GetAllAgentIds().ToList();

        // Assert
        Assert.That(agentIds, Has.Count.EqualTo(2));
        Assert.That(agentIds, Does.Contain("agent-1"));
        Assert.That(agentIds, Does.Contain("agent-2"));
    }

    [Test]
    public void OnMessageReceived_EventIsRaised()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        string? receivedAgentId = null;
        string? receivedMessage = null;

        manager.OnMessageReceived += (agentId, message) =>
        {
            receivedAgentId = agentId;
            receivedMessage = message;
        };

        // Act
        manager.SimulateMessageReceived("test-agent", "{\"type\":\"text\",\"content\":\"Hello\"}");

        // Assert
        Assert.That(receivedAgentId, Is.EqualTo("test-agent"));
        Assert.That(receivedMessage, Does.Contain("Hello"));
    }

    [Test]
    public async Task Dispose_StopsAllProcesses()
    {
        // Arrange
        var manager = new ClaudeCodeProcessManager(_mockFactory.Object);
        await manager.StartAgentAsync("agent-1", "/tmp/test");

        // Act
        manager.Dispose();

        // Assert
        _mockProcess.Verify(p => p.Dispose(), Times.Once);
        Assert.That(manager.GetAllAgentIds(), Is.Empty);
    }
}
