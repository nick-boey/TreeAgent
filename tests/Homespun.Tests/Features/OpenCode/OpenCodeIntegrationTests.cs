using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.OpenCode;

/// <summary>
/// Integration tests for OpenCode server communication.
/// These tests require OpenCode to be installed and accessible in PATH.
/// Run manually with: dotnet test --filter "Category=OpenCodeIntegration"
/// </summary>
[TestFixture]
[Category("OpenCodeIntegration")]
public class OpenCodeIntegrationTests
{
    private OpenCodeServerManager _serverManager = null!;
    private OpenCodeClient _client = null!;
    private OpenCodeConfigGenerator _configGenerator = null!;
    private string _tempDir = null!;
    private IOptions<OpenCodeOptions> _options = null!;

    [SetUp]
    public void SetUp()
    {
        _options = Options.Create(new OpenCodeOptions
        {
            ExecutablePath = "opencode",
            BasePort = 14096, // Use a different port range for tests
            MaxConcurrentServers = 3,
            ServerStartTimeoutMs = 30000, // 30 seconds for server startup
            DefaultModel = "anthropic/claude-sonnet-4-5"
        });

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        
        _serverManager = new OpenCodeServerManager(
            _options,
            _client,
            Mock.Of<ILogger<OpenCodeServerManager>>());

        _configGenerator = new OpenCodeConfigGenerator(
            _options,
            Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        _tempDir = Path.Combine(Path.GetTempPath(), $"opencode-integration-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        _serverManager.Dispose();
        
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Tests that we can start an OpenCode server and verify it's healthy.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    public async Task StartServer_BecomesHealthy()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);

        // Act
        var server = await _serverManager.StartServerAsync("test-pr-1", _tempDir);

        // Assert
        Assert.That(server.Status, Is.EqualTo(OpenCodeServerStatus.Running));
        Assert.That(server.Port, Is.GreaterThanOrEqualTo(_options.Value.BasePort));

        var isHealthy = await _serverManager.IsHealthyAsync(server);
        Assert.That(isHealthy, Is.True);

        // Cleanup
        await _serverManager.StopServerAsync("test-pr-1");
    }

    /// <summary>
    /// Tests creating a session on an OpenCode server.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    public async Task CreateSession_ReturnsSession()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-2", _tempDir);

        try
        {
            // Act
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Test Session");

            // Assert
            Assert.That(session, Is.Not.Null);
            Assert.That(session.Id, Is.Not.Empty);
            Assert.That(session.Title, Is.EqualTo("Test Session"));

            // Verify session appears in list
            var sessions = await _client.ListSessionsAsync(server.BaseUrl);
            Assert.That(sessions.Any(s => s.Id == session.Id), Is.True);
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-2");
        }
    }

    /// <summary>
    /// Tests sending a simple prompt and receiving a response.
    /// This is the full round-trip test.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually.")]
    public async Task SendPrompt_ReceivesResponse()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-3", _tempDir);

        try
        {
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Query Test");
            
            // Act - Send a simple prompt that should get a quick response
            var prompt = PromptRequest.FromText("What is 2 + 2? Reply with just the number.");
            var response = await _client.SendPromptAsync(server.BaseUrl, session.Id, prompt);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Info, Is.Not.Null);
            Assert.That(response.Info.Role, Is.EqualTo("assistant"));
            Assert.That(response.Parts, Is.Not.Empty);

            // Log the response for manual verification
            var textParts = response.Parts
                .Where(p => p.Type == "text" && !string.IsNullOrEmpty(p.Text))
                .Select(p => p.Text)
                .ToList();
            
            Console.WriteLine($"Response from OpenCode:");
            foreach (var text in textParts)
            {
                Console.WriteLine(text);
            }

            // The response should contain "4" somewhere
            var hasExpectedAnswer = textParts.Any(t => t!.Contains("4"));
            Assert.That(hasExpectedAnswer, Is.True, "Expected response to contain '4'");
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-3");
        }
    }

    /// <summary>
    /// Tests sending a prompt asynchronously (fire and forget).
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually.")]
    public async Task SendPromptAsync_NoWait_Succeeds()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-4", _tempDir);

        try
        {
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Async Test");
            
            // Act - Send prompt without waiting
            var prompt = PromptRequest.FromText("Hello! This is an async test.");
            await _client.SendPromptAsyncNoWait(server.BaseUrl, session.Id, prompt);

            // Give it a moment to process
            await Task.Delay(2000);

            // Assert - Check that messages were created
            var messages = await _client.GetMessagesAsync(server.BaseUrl, session.Id);
            Assert.That(messages, Is.Not.Empty);
            Assert.That(messages.Any(m => m.Info.Role == "user"), Is.True);
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-4");
        }
    }

    /// <summary>
    /// Tests listing sessions and getting session details.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    public async Task ListAndGetSessions_ReturnsCorrectData()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-5", _tempDir);

        try
        {
            // Create multiple sessions
            var session1 = await _client.CreateSessionAsync(server.BaseUrl, "Session 1");
            var session2 = await _client.CreateSessionAsync(server.BaseUrl, "Session 2");

            // Act - List sessions
            var sessions = await _client.ListSessionsAsync(server.BaseUrl);

            // Assert
            Assert.That(sessions.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(sessions.Any(s => s.Id == session1.Id), Is.True);
            Assert.That(sessions.Any(s => s.Id == session2.Id), Is.True);

            // Act - Get specific session
            var retrieved = await _client.GetSessionAsync(server.BaseUrl, session1.Id);

            // Assert
            Assert.That(retrieved.Id, Is.EqualTo(session1.Id));
            Assert.That(retrieved.Title, Is.EqualTo("Session 1"));
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-5");
        }
    }

    /// <summary>
    /// Tests aborting a running session.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    public async Task AbortSession_ReturnsTrue()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-6", _tempDir);

        try
        {
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Abort Test");

            // Act
            var result = await _client.AbortSessionAsync(server.BaseUrl, session.Id);

            // Assert
            Assert.That(result, Is.True);
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-6");
        }
    }

    /// <summary>
    /// Tests deleting a session.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    public async Task DeleteSession_RemovesSession()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);
        var server = await _serverManager.StartServerAsync("test-pr-7", _tempDir);

        try
        {
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Delete Test");
            
            // Act
            var deleted = await _client.DeleteSessionAsync(server.BaseUrl, session.Id);

            // Assert
            Assert.That(deleted, Is.True);

            // Verify session is gone
            var sessions = await _client.ListSessionsAsync(server.BaseUrl);
            Assert.That(sessions.All(s => s.Id != session.Id), Is.True);
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-7");
        }
    }

    /// <summary>
    /// Tests the full workflow: start server, create session, send prompt, get response.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually.")]
    public async Task FullWorkflow_StartServer_CreateSession_SendPrompt_GetResponse()
    {
        // Arrange
        var config = _configGenerator.CreateDefaultConfig();
        await _configGenerator.GenerateConfigAsync(_tempDir, config);

        Console.WriteLine($"Starting OpenCode server in: {_tempDir}");
        Console.WriteLine($"Config: model={config.Model}");

        // Act - Start server
        var server = await _serverManager.StartServerAsync("test-pr-full", _tempDir);
        Console.WriteLine($"Server started on port {server.Port}");

        try
        {
            // Act - Check health
            var health = await _client.GetHealthAsync(server.BaseUrl);
            Console.WriteLine($"Server health: {health.Healthy}, version: {health.Version}");
            Assert.That(health.Healthy, Is.True);

            // Act - Create session
            var session = await _client.CreateSessionAsync(server.BaseUrl, "Full Workflow Test");
            Console.WriteLine($"Session created: {session.Id}");

            // Act - Send a coding-related prompt
            var prompt = PromptRequest.FromText(
                "Write a simple C# function that returns the factorial of a number. " +
                "Just show the code, no explanation needed.");
            
            Console.WriteLine("Sending prompt...");
            var response = await _client.SendPromptAsync(server.BaseUrl, session.Id, prompt);
            Console.WriteLine("Response received!");

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Parts, Is.Not.Empty);

            // Print response
            Console.WriteLine("\n=== Response ===");
            foreach (var part in response.Parts)
            {
                if (part.Type == "text" && !string.IsNullOrEmpty(part.Text))
                {
                    Console.WriteLine(part.Text);
                }
                else if (part.Type == "tool_use")
                {
                    Console.WriteLine($"[Tool: {part.ToolUseName}]");
                }
            }
            Console.WriteLine("=== End Response ===\n");

            // Verify we got some code in the response
            var hasCode = response.Parts.Any(p => 
                p.Type == "text" && 
                p.Text != null && 
                (p.Text.Contains("factorial") || p.Text.Contains("Factorial")));
            
            Assert.That(hasCode, Is.True, "Expected response to contain factorial-related code");

            // Get message history
            var messages = await _client.GetMessagesAsync(server.BaseUrl, session.Id);
            Console.WriteLine($"Total messages in session: {messages.Count}");
            Assert.That(messages.Count, Is.GreaterThanOrEqualTo(2)); // At least user + assistant
        }
        finally
        {
            await _serverManager.StopServerAsync("test-pr-full");
            Console.WriteLine("Server stopped.");
        }
    }
}
