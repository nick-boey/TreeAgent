using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for ClaudeCodeProcess that test against a real Claude Code installation.
/// These tests require Claude Code to be installed and available on the PATH.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("ClaudeCode")]
public class ClaudeCodeProcessIntegrationTests
{
    private ClaudeCodeTestFixture _fixture = null!;
    private readonly List<IClaudeCodeProcess> _processesToCleanup = [];

    [SetUp]
    public void SetUp()
    {
        _fixture = new ClaudeCodeTestFixture();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var process in _processesToCleanup)
        {
            try
            {
                process.Dispose();
            }
            catch
            {
                // Best effort cleanup
            }
        }
        _processesToCleanup.Clear();

        _fixture.Dispose();
    }

    [Test]
    public async Task ClaudeCodeProcess_Start_StartsSuccessfully()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-start");
        var statusChanges = new List<AgentStatus>();
        process.OnStatusChanged += status => statusChanges.Add(status);

        // Act
        await process.StartAsync();

        // Allow time for process to start
        await Task.Delay(2000);

        // Assert
        Assert.That(process.IsRunning, Is.True);
        Assert.That(statusChanges, Does.Contain(AgentStatus.Running));
    }

    [Test]
    public async Task ClaudeCodeProcess_SendMessage_ReceivesResponse()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-message");
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            if (messagesReceived.Count >= 1)
            {
                messageReceivedEvent.TrySetResult(true);
            }
        };

        await process.StartAsync();
        await Task.Delay(2000); // Wait for startup

        // Act
        await process.SendMessageAsync("Reply with only the word 'hello'");

        // Wait for response with timeout
        var completed = await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.That(messagesReceived, Is.Not.Empty);
    }

    [Test]
    public async Task ClaudeCodeProcess_Stop_StopsGracefully()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-stop");
        var statusChanges = new List<AgentStatus>();
        process.OnStatusChanged += status => statusChanges.Add(status);

        await process.StartAsync();
        await Task.Delay(2000); // Wait for startup
        Assert.That(process.IsRunning, Is.True);

        // Act
        await process.StopAsync();

        // Assert
        Assert.That(process.IsRunning, Is.False);
        Assert.That(process.Status, Is.EqualTo(AgentStatus.Stopped));
    }

    [Test]
    public async Task ClaudeCodeProcess_WithSystemPrompt_UsesSystemPrompt()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var systemPrompt = "You are a helpful assistant that always responds with 'PONG' when asked 'PING'.";
        var process = CreateProcess("test-system-prompt", systemPrompt);
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageReceivedEvent.TrySetResult(true);
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act
        await process.SendMessageAsync("PING");

        await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.That(messagesReceived, Is.Not.Empty);
        // The response should contain PONG if the system prompt was applied
        var allMessages = string.Join(" ", messagesReceived);
        Assert.That(allMessages.ToUpperInvariant(), Does.Contain("PONG"));
    }

    [Test]
    public async Task ClaudeCodeProcess_MultipleMessages_MaintainsContext()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-context");
        var messagesReceived = new List<string>();
        var messageCount = 0;
        var secondMessageReceived = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageCount++;
            if (messageCount >= 2)
            {
                secondMessageReceived.TrySetResult(true);
            }
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act - Send two related messages
        await process.SendMessageAsync("Remember this number: 42");
        await Task.Delay(5000);

        await process.SendMessageAsync("What number did I ask you to remember?");

        await Task.WhenAny(
            secondMessageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.That(messagesReceived.Count, Is.GreaterThanOrEqualTo(2));
        var allMessages = string.Join(" ", messagesReceived);
        Assert.That(allMessages, Does.Contain("42"));
    }

    [Test]
    public async Task ClaudeCodeProcess_JsonOutputFormat_ReturnsJson()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var process = CreateProcess("test-json");
        var messagesReceived = new List<string>();
        var messageReceivedEvent = new TaskCompletionSource<bool>();

        process.OnMessageReceived += message =>
        {
            messagesReceived.Add(message);
            messageReceivedEvent.TrySetResult(true);
        };

        await process.StartAsync();
        await Task.Delay(2000);

        // Act
        await process.SendMessageAsync("Say hello");

        await Task.WhenAny(
            messageReceivedEvent.Task,
            Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert - Should receive JSON formatted output
        Assert.That(messagesReceived, Is.Not.Empty);
        // JSON output typically starts with { or contains json-like structure
        var firstMessage = messagesReceived.First();
        Assert.That(
            firstMessage.Contains("{") || firstMessage.Contains("\""),
            Is.True,
            $"Expected JSON-like output but got: {firstMessage}");
    }

    [Test]
    public void ClaudeCodeAvailability_ReportsCorrectly()
    {
        // This test always runs and documents the Claude Code availability
        var version = _fixture.GetClaudeCodeVersion();

        if (_fixture.IsClaudeCodeAvailable)
        {
            Assert.That(version, Is.Not.Null);
            Assert.That(version, Is.Not.Empty);
        }
        else
        {
            Assert.That(version, Is.Null);
        }
    }

    private IClaudeCodeProcess CreateProcess(string agentId, string? systemPrompt = null)
    {
        var factory = new ClaudeCodeProcessFactory(_fixture.ClaudeCodePath);
        var process = factory.Create(agentId, _fixture.WorkingDirectory, systemPrompt);
        _processesToCleanup.Add(process);
        return process;
    }
}
