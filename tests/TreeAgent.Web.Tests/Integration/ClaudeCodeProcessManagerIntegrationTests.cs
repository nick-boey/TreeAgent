using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for ClaudeCodeProcessManager that test multiple agent management.
/// These tests require Claude Code to be installed and available on the PATH.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("ClaudeCode")]
public class ClaudeCodeProcessManagerIntegrationTests
{
    private ClaudeCodeTestFixture _fixture = null!;
    private ClaudeCodeProcessManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new ClaudeCodeTestFixture();
        _manager = new ClaudeCodeProcessManager(new ClaudeCodeProcessFactory(_fixture.ClaudeCodePath));
    }

    [TearDown]
    public void TearDown()
    {
        _manager.Dispose();
        _fixture.Dispose();
    }

    [Test]
    public async Task StartAgent_SingleAgent_StartsSuccessfully()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-agent-1";
        var statusChanges = new List<(string AgentId, AgentStatus Status)>();
        _manager.OnStatusChanged += (id, status) => statusChanges.Add((id, status));

        // Act
        var result = await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000); // Allow time for process to start

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_manager.IsAgentRunning(agentId), Is.True);
        Assert.That(_manager.GetAgentStatus(agentId), Is.EqualTo(AgentStatus.Running));
    }

    [Test]
    public async Task StartAgent_DuplicateAgentId_ReturnsFalse()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-duplicate";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(1000);

        // Act
        var result = await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task StartAgent_MultipleAgents_AllRunConcurrently()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentIds = new[] { "agent-1", "agent-2", "agent-3" };

        // Act
        foreach (var id in agentIds)
        {
            await _manager.StartAgentAsync(id, _fixture.WorkingDirectory);
        }
        await Task.Delay(3000); // Allow time for all processes to start

        // Assert
        Assert.That(_manager.GetRunningAgentCount(), Is.EqualTo(3));
        foreach (var id in agentIds)
        {
            Assert.That(_manager.IsAgentRunning(id), Is.True);
        }
    }

    [Test]
    public async Task StopAgent_RunningAgent_StopsSuccessfully()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-stop-manager";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000);
        Assert.That(_manager.IsAgentRunning(agentId), Is.True);

        // Act
        var result = await _manager.StopAgentAsync(agentId);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_manager.IsAgentRunning(agentId), Is.False);
        Assert.That(_manager.GetAgentStatus(agentId), Is.EqualTo(AgentStatus.Stopped));
    }

    [Test]
    public async Task StopAgent_NonExistentAgent_ReturnsFalse()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Act
        var result = await _manager.StopAgentAsync("non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendMessage_RunningAgent_SendsSuccessfully()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-send-message";
        var messagesReceived = new List<string>();
        _manager.OnMessageReceived += (id, message) =>
        {
            if (id == agentId) messagesReceived.Add(message);
        };

        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(2000);

        // Act
        var result = await _manager.SendMessageAsync(agentId, "Reply with only 'OK'");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task SendMessage_StoppedAgent_ReturnsFalse()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-stopped-send";
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory);
        await Task.Delay(1000);
        await _manager.StopAgentAsync(agentId);

        // Act
        var result = await _manager.SendMessageAsync(agentId, "This should fail");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetAllAgentIds_ReturnsAllRunningAgents()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentIds = new[] { "list-agent-1", "list-agent-2" };
        foreach (var id in agentIds)
        {
            await _manager.StartAgentAsync(id, _fixture.WorkingDirectory);
        }
        await Task.Delay(2000);

        // Act
        var result = _manager.GetAllAgentIds().ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("list-agent-1"));
        Assert.That(result, Does.Contain("list-agent-2"));
    }

    [Test]
    public async Task StartAgent_WithSystemPrompt_PassesPromptToProcess()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var agentId = "test-system-prompt";
        var systemPrompt = "Always respond with 'ACKNOWLEDGED'.";
        var messagesReceived = new List<string>();
        var messageReceived = new TaskCompletionSource<bool>();

        _manager.OnMessageReceived += (id, message) =>
        {
            if (id == agentId)
            {
                messagesReceived.Add(message);
                messageReceived.TrySetResult(true);
            }
        };

        // Act
        await _manager.StartAgentAsync(agentId, _fixture.WorkingDirectory, systemPrompt);
        await Task.Delay(2000);
        await _manager.SendMessageAsync(agentId, "Hello");

        await Task.WhenAny(messageReceived.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.That(messagesReceived, Is.Not.Empty);
        var allMessages = string.Join(" ", messagesReceived);
        Assert.That(allMessages.ToUpperInvariant(), Does.Contain("ACKNOWLEDGED"));
    }

    [Test]
    public void GetAgentStatus_NonExistentAgent_ReturnsStopped()
    {
        // Act
        var status = _manager.GetAgentStatus("non-existent");

        // Assert
        Assert.That(status, Is.EqualTo(AgentStatus.Stopped));
    }

    [Test]
    public void IsAgentRunning_NonExistentAgent_ReturnsFalse()
    {
        // Act
        var isRunning = _manager.IsAgentRunning("non-existent");

        // Assert
        Assert.That(isRunning, Is.False);
    }
}
