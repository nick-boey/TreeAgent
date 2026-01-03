namespace TreeAgent.Web.Tests.Features.Agents;

/// <summary>
/// Integration tests for Claude Code using query mode (--print).
/// These tests use stream-json output for reliable message parsing and completion detection.
/// Tests are designed to run in parallel for faster execution.
///
/// Based on the approach used by happy-cli (slopus/happy-cli).
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("ClaudeCode")]
[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ClaudeCodeQueryIntegrationTests
{
    private ClaudeCodeQueryFixture _fixture = null!;

    // Timeout for individual queries - Claude typically responds within 10-15 seconds
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    [SetUp]
    public void SetUp()
    {
        // Each test gets its own fixture with isolated working directory
        _fixture = new ClaudeCodeQueryFixture();
    }

    [TearDown]
    public void TearDown()
    {
        _fixture?.Dispose();
    }

    [Test]
    public async Task Query_SimplePrompt_ReturnsResponse()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        using var process = _fixture.CreateTestProcess();

        // Act
        var result = await process.QueryAsync(
            "Reply with only the word 'hello'",
            QueryTimeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.True, "Query should complete");
            Assert.That(result.TimedOut, Is.False, "Query should not time out");
            Assert.That(result.SystemMessageReceived, Is.True, "Should receive system message");
            Assert.That(result.SessionId, Is.Not.Null.And.Not.Empty, "Should have session ID");

            var responseText = result.GetAssistantText().ToLowerInvariant();
            Assert.That(responseText, Does.Contain("hello"), "Response should contain 'hello'");
        });
    }

    [Test]
    public async Task Query_WithSystemPrompt_UsesSystemPrompt()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        var systemPrompt = "You must always respond with exactly 'PONG' and nothing else when the user says 'PING'.";
        using var process = _fixture.CreateTestProcess(systemPrompt);

        // Act
        var result = await process.QueryAsync("PING", QueryTimeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.True, "Query should complete");
            Assert.That(result.TimedOut, Is.False, "Query should not time out");

            var responseText = result.GetAssistantText().ToUpperInvariant();
            Assert.That(responseText, Does.Contain("PONG"), "Response should contain PONG when system prompt is set");
        });
    }

    [Test]
    public async Task Query_StreamJsonFormat_ReturnsStructuredMessages()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        using var process = _fixture.CreateTestProcess();

        // Act
        var result = await process.QueryAsync("Say 'test'", QueryTimeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Messages, Is.Not.Empty, "Should receive messages");
            Assert.That(result.Messages.Any(m => m.Type == "system"), Is.True, "Should have system message");
            Assert.That(result.Messages.Any(m => m.Type == "assistant"), Is.True, "Should have assistant message");
            Assert.That(result.Messages.Any(m => m.Type == "result"), Is.True, "Should have result message");
        });
    }

    [Test]
    public async Task Query_ResultMessage_IndicatesSuccess()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        using var process = _fixture.CreateTestProcess();

        // Act
        var result = await process.QueryAsync("Reply with 'OK'", QueryTimeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.True, "Should be complete");
            Assert.That(result.IsSuccess, Is.True, "Should be successful");
            Assert.That(result.ExitCode, Is.EqualTo(0).Or.Null, "Exit code should be 0 or not set");
        });
    }

    [Test]
    public void ClaudeCodeAvailability_ReportsCorrectly()
    {
        // This test always runs and documents the Claude Code availability
        if (_fixture.IsClaudeCodeAvailable)
        {
            Assert.Multiple(() =>
            {
                Assert.That(_fixture.ClaudeCodeVersion, Is.Not.Null);
                Assert.That(_fixture.ClaudeCodeVersion, Is.Not.Empty);
            });

            TestContext.WriteLine($"Claude Code Version: {_fixture.ClaudeCodeVersion}");
            TestContext.WriteLine($"Claude Code Path: {_fixture.ClaudeCodePath}");
        }
        else
        {
            Assert.That(_fixture.ClaudeCodeVersion, Is.Null);
            TestContext.WriteLine("Claude Code is not available");
        }
    }

    [Test]
    public async Task Query_WorkingDirectory_IsUsed()
    {
        Assume.That(_fixture.IsClaudeCodeAvailable, Is.True, "Claude Code is not available");

        // Arrange
        using var process = _fixture.CreateTestProcess();

        // Act - Ask Claude to read the test.txt file we created in the fixture
        var result = await process.QueryAsync(
            "Read the file test.txt and tell me what it contains. Reply with just the file contents.",
            QueryTimeout);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsComplete, Is.True, "Query should complete");
            Assert.That(result.TimedOut, Is.False, "Query should not time out");

            var responseText = result.GetAssistantText();
            Assert.That(responseText, Does.Contain("Hello").Or.Contain("World"),
                "Response should reference the test file content");
        });
    }
}
