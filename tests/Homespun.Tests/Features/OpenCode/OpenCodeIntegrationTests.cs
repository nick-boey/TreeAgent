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
            DefaultModel = "anthropic/claude-opus-4-5"
        });

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _client = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        
        var portAllocationService = new PortAllocationService(
            _options,
            Mock.Of<ILogger<PortAllocationService>>());
        
        _serverManager = new OpenCodeServerManager(
            _options,
            _client,
            portAllocationService,
            Mock.Of<IHubContext<AgentHub>>(),
            CreateMockGitHubEnvironmentService(),
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
    /// Debug test for investigating server startup failures.
    /// Creates a fake worktree with a file and tests the full agent workflow.
    /// This mirrors the flow in FeatureDetailPanel.StartAgent -> AgentWorkflowService.StartAgentForPullRequestAsync
    /// 
    /// NOTE: This test is SELF-CONTAINED and uses its own port and service instances
    /// to avoid conflicts with the main Homespun application or other tests.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually for debugging.")]
    [CancelAfter(60000)] // 60 second timeout for the entire test
    public async Task DebugAgentStartup_WithFakeWorktree_AsksAboutFile()
    {
        // === SELF-CONTAINED TEST SETUP ===
        // Use a random high port to avoid conflicts with other OpenCode instances
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"opencode-debug-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);
        
        // Create our own service instances with the unique port
        var testOptions = Options.Create(new OpenCodeOptions
        {
            ExecutablePath = "opencode",
            BasePort = testPort,
            MaxConcurrentServers = 1,
            ServerStartTimeoutMs = 15000, // 15 seconds for server startup
            DefaultModel = "anthropic/claude-opus-4-5"
        });
        
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(
            testOptions,
            Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(
            testOptions,
            testClient,
            testPortAllocationService,
            Mock.Of<IHubContext<AgentHub>>(),
            CreateMockGitHubEnvironmentService(),
            Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(
            testOptions,
            Mock.Of<ILogger<OpenCodeConfigGenerator>>());
        
        // Track any processes we start for cleanup
        System.Diagnostics.Process? serverProcess = null;
        
        try
        {
            // Arrange - Create a fake worktree with a test file
            var worktreePath = testTempDir;
            
            var testFileName = "Calculator.cs";
            var testFilePath = Path.Combine(worktreePath, testFileName);
            var testFileContent = """
                namespace TestProject;
                
                public class Calculator
                {
                    public int Add(int a, int b) => a + b;
                    
                    public int Subtract(int a, int b) => a - b;
                    
                    public int Multiply(int a, int b) => a * b;
                    
                    public double Divide(int a, int b)
                    {
                        if (b == 0)
                            throw new DivideByZeroException("Cannot divide by zero");
                        return (double)a / b;
                    }
                }
                """;
            
            await File.WriteAllTextAsync(testFilePath, testFileContent);
            Console.WriteLine($"=== Test Setup ===");
            Console.WriteLine($"Worktree path: {worktreePath}");
            Console.WriteLine($"Test port: {testPort}");
            Console.WriteLine($"Created test file: {testFilePath}");
            
            // Step 1: Generate config
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Generate OpenCode Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            Console.WriteLine($"Model: {config.Model}");
            
            await testConfigGenerator.GenerateConfigAsync(worktreePath, config);
            Console.WriteLine("Config file generated successfully");
            
            // Show the generated config
            var configPath = Path.Combine(worktreePath, "opencode.json");
            if (File.Exists(configPath))
            {
                var configContent = await File.ReadAllTextAsync(configPath);
                Console.WriteLine($"Config content:\n{configContent}");
            }

            // List all files in worktree
            var files = Directory.GetFiles(worktreePath);
            Console.WriteLine($"Files in worktree: {string.Join(", ", files.Select(Path.GetFileName))}");

            // Step 2: Verify OpenCode installation
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Verify OpenCode Installation ===");
            try
            {
                using var versionProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "opencode",
                        Arguments = "--version",
                        WorkingDirectory = worktreePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                versionProcess.Start();
                var versionOutput = await versionProcess.StandardOutput.ReadToEndAsync();
                var versionError = await versionProcess.StandardError.ReadToEndAsync();
                await versionProcess.WaitForExitAsync();
                
                Console.WriteLine($"OpenCode version: {versionOutput.Trim()}");
                if (!string.IsNullOrWhiteSpace(versionError))
                    Console.WriteLine($"Version stderr: {versionError}");
                Console.WriteLine($"Version exit code: {versionProcess.ExitCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Could not verify OpenCode installation: {ex.Message}");
                Console.WriteLine("Test will likely fail - OpenCode not found in PATH");
            }
            
            // Step 3: Start server
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Start OpenCode Server ===");
            
            OpenCodeServer server;
            try
            {
                Console.WriteLine($"Starting server on port {testPort}...");
                Console.WriteLine($"Working directory: {worktreePath}");
                
                server = await testServerManager.StartServerAsync("debug-test-pr", worktreePath, continueSession: false);
                serverProcess = server.Process; // Track for cleanup
                
                Console.WriteLine($"Server started successfully!");
                Console.WriteLine($"Port: {server.Port}");
                Console.WriteLine($"URL: {server.BaseUrl}");
                Console.WriteLine($"Status: {server.Status}");
                Console.WriteLine($"Process ID: {server.Process?.Id}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exited with code") || ex.Message.Contains("already in use"))
            {
                Console.WriteLine();
                Console.WriteLine($"!!! SERVER STARTUP FAILED !!!");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Attempting to run 'opencode serve' manually to capture error output...");
                
                await RunManualServeForDiagnostics(worktreePath, testPort);
                throw;
            }

            // Step 4: Check health
            Console.WriteLine();
            Console.WriteLine("=== Step 4: Health Check ===");
            var health = await testClient.GetHealthAsync(server.BaseUrl);
            Console.WriteLine($"Healthy: {health.Healthy}");
            Console.WriteLine($"Version: {health.Version}");
            Assert.That(health.Healthy, Is.True, "Server should be healthy");

            // Step 4.5: Verify working directory
            Console.WriteLine();
            Console.WriteLine("=== Step 4.5: Verify Working Directory ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path from /path endpoint: {reportedPath}");
            Console.WriteLine($"Expected path: {worktreePath}");

            if (reportedPath != null)
            {
                var normalizedReported = Path.GetFullPath(reportedPath);
                var normalizedExpected = Path.GetFullPath(worktreePath);
                var pathsMatch = string.Equals(normalizedReported, normalizedExpected, StringComparison.OrdinalIgnoreCase);
                
                Console.WriteLine($"Normalized reported: {normalizedReported}");
                Console.WriteLine($"Normalized expected: {normalizedExpected}");
                Console.WriteLine($"Paths match: {pathsMatch}");
                
                Assert.That(pathsMatch, Is.True, 
                    $"Working directory mismatch! Expected: {normalizedExpected}, Actual: {normalizedReported}");
            }
            else
            {
                Console.WriteLine("WARNING: Could not verify working directory - /path returned null");
            }

            // Step 5: Create session
            Console.WriteLine();
            Console.WriteLine("=== Step 5: Create Session ===");
            var session = await testClient.CreateSessionAsync(server.BaseUrl, "Debug Test Session");
            Console.WriteLine($"Session ID: {session.Id}");
            Console.WriteLine($"Session Title: {session.Title}");

            // Step 6: Send prompt and get response
            Console.WriteLine();
            Console.WriteLine("=== Step 6: Send Prompt ===");
            var prompt = PromptRequest.FromText(
                $"Look at the file {testFileName} in the current directory. " +
                "What methods does the Calculator class have? List them briefly.");
            
            Console.WriteLine($"Prompt: Look at the file {testFileName}...");
            Console.WriteLine("Waiting for response...");
            
            var response = await testClient.SendPromptAsync(server.BaseUrl, session.Id, prompt);
            Console.WriteLine("Response received!");

            // Assert and print response
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Parts, Is.Not.Empty);

            Console.WriteLine();
            Console.WriteLine("=== Agent Response ===");
            foreach (var part in response.Parts)
            {
                if (part.Type == "text" && !string.IsNullOrEmpty(part.Text))
                {
                    Console.WriteLine(part.Text);
                }
                else if (part.Type == "tool_use")
                {
                    Console.WriteLine($"[Tool Used: {part.ToolUseName}]");
                }
            }
            Console.WriteLine("=== End Response ===");

            // Verify the response mentions the calculator methods
            var responseText = string.Join(" ", response.Parts
                .Where(p => p.Type == "text" && !string.IsNullOrEmpty(p.Text))
                .Select(p => p.Text));
            
            var mentionsMethods = responseText.Contains("Add") || 
                                  responseText.Contains("Subtract") || 
                                  responseText.Contains("Multiply") || 
                                  responseText.Contains("Divide");
            
            Assert.That(mentionsMethods, Is.True, 
                "Expected response to mention at least one Calculator method (Add, Subtract, Multiply, or Divide)");
            
            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Agent correctly identified the Calculator methods.");
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("=== Cleanup ===");
            
            // Force kill any server process we started
            if (serverProcess is { HasExited: false })
            {
                try
                {
                    Console.WriteLine($"Killing server process {serverProcess.Id}...");
                    serverProcess.Kill(entireProcessTree: true);
                    await serverProcess.WaitForExitAsync();
                    Console.WriteLine("Server process killed.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Could not kill server process: {ex.Message}");
                }
            }
            
            // Clean up temp directory
            if (Directory.Exists(testTempDir))
            {
                try
                {
                    Directory.Delete(testTempDir, true);
                    Console.WriteLine($"Cleaned up temp directory: {testTempDir}");
                }
                catch
                {
                    Console.WriteLine($"WARNING: Could not clean up temp directory: {testTempDir}");
                }
            }
        }
    }
    
    /// <summary>
    /// Helper method to run opencode serve manually and capture diagnostic output.
    /// </summary>
    private static async Task RunManualServeForDiagnostics(string workingDirectory, int port)
    {
        try
        {
            var serveProcess = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "opencode",
                    Arguments = $"serve --port {port} --hostname 127.0.0.1",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            serveProcess.Start();
            
            // Wait a short time for it to fail or output something
            var outputTask = serveProcess.StandardOutput.ReadToEndAsync();
            var errorTask = serveProcess.StandardError.ReadToEndAsync();
            
            // Give it 5 seconds to start or fail
            var completed = serveProcess.WaitForExit(5000);
            
            var output = await outputTask;
            var error = await errorTask;
            
            Console.WriteLine($"Serve stdout: {output}");
            Console.WriteLine($"Serve stderr: {error}");
            Console.WriteLine($"Exit code: {(completed ? serveProcess.ExitCode.ToString() : "still running")}");
            
            if (!completed && !serveProcess.HasExited)
            {
                serveProcess.Kill();
            }
        }
        catch (Exception serveEx)
        {
            Console.WriteLine($"Failed to run serve command: {serveEx.Message}");
        }
        
        Console.WriteLine();
        Console.WriteLine("Common causes of exit code 1:");
        Console.WriteLine("1. Missing or invalid opencode.json config file");
        Console.WriteLine("2. Missing API keys (ANTHROPIC_API_KEY environment variable)");
        Console.WriteLine("3. Invalid model specified in config");
        Console.WriteLine("4. Port already in use");
        Console.WriteLine("5. Permission issues with the working directory");
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

    private static IGitHubEnvironmentService CreateMockGitHubEnvironmentService()
    {
        var mock = new Mock<IGitHubEnvironmentService>();
        mock.Setup(g => g.GetGitHubEnvironment()).Returns(new Dictionary<string, string>());
        return mock.Object;
    }
}
