using Homespun.Features.ClaudeCodeUI;
using Homespun.Features.ClaudeCodeUI.Models;
using Homespun.Features.ClaudeCodeUI.Services;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Homespun.Tests.Features.ClaudeCodeUI;

/// <summary>
/// Integration tests for Claude Code UI harness.
/// These tests require cloudcli (claude-code-ui) to be installed and accessible in PATH.
/// Run manually with: dotnet test --filter "Category=ClaudeCodeUIIntegration"
/// </summary>
[TestFixture]
[Category("ClaudeCodeUIIntegration")]
public class ClaudeCodeUIIntegrationTests
{
    private ClaudeCodeUIServerManager _serverManager = null!;
    private ClaudeCodeUIClient _client = null!;
    private IOptions<ClaudeCodeUIOptions> _options = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _options = Options.Create(new ClaudeCodeUIOptions
        {
            ExecutablePath = "cloudcli",
            BasePort = 13001, // Use a different port range for tests
            MaxConcurrentServers = 3,
            ServerStartTimeoutMs = 60000, // 60 seconds for server startup
            HealthCheckIntervalMs = 500
        });

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client = new ClaudeCodeUIClient(httpClient, Mock.Of<ILogger<ClaudeCodeUIClient>>());

        // Use an isolated port allocation service for tests
        var portOptions = Options.Create(new OpenCodeOptions
        {
            BasePort = 13001,
            MaxConcurrentServers = 10
        });
        var portAllocationService = new PortAllocationService(
            portOptions,
            Mock.Of<ILogger<PortAllocationService>>());

        _serverManager = new ClaudeCodeUIServerManager(
            _options,
            _client,
            portAllocationService,
            Mock.Of<IGitHubEnvironmentService>(),
            Mock.Of<ILogger<ClaudeCodeUIServerManager>>());

        _tempDir = Path.Combine(Path.GetTempPath(), $"claudeui-integration-test-{Guid.NewGuid()}");
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
    /// Tests that we can start a Claude Code UI server and verify it's healthy.
    /// </summary>
    [Test]
    [Explicit("Requires cloudcli to be installed. Run manually.")]
    public async Task StartServer_BecomesHealthy()
    {
        // Act
        var server = await _serverManager.StartServerAsync("test-entity-1", _tempDir);

        // Assert
        Assert.That(server.Status, Is.EqualTo(ClaudeCodeUIServerStatus.Running));
        Assert.That(server.Port, Is.GreaterThanOrEqualTo(_options.Value.BasePort));

        var isHealthy = await _serverManager.IsHealthyAsync(server);
        Assert.That(isHealthy, Is.True);

        Console.WriteLine($"Server started successfully on port {server.Port}");
        Console.WriteLine($"Web UI URL: {server.WebViewUrl}");

        // Cleanup
        await _serverManager.StopServerAsync("test-entity-1");
    }

    /// <summary>
    /// Tests creating an agent, sending a prompt to create a file, and outputting the URL.
    /// This allows manual verification of the Claude Code UI integration.
    /// </summary>
    [Test]
    [Explicit("Requires cloudcli to be installed and ANTHROPIC_API_KEY configured. Run manually.")]
    [CancelAfter(300000)] // 5 minute timeout for interactive testing
    public async Task StartAgent_CreateTestFile_OutputUrl()
    {
        // === SELF-CONTAINED TEST SETUP ===
        var random = new Random();
        var testPort = random.Next(13000, 14000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"claudeui-agent-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        // Create our own service instances with the unique port
        var testOptions = Options.Create(new ClaudeCodeUIOptions
        {
            ExecutablePath = "cloudcli",
            BasePort = testPort,
            MaxConcurrentServers = 1,
            ServerStartTimeoutMs = 60000,
            HealthCheckIntervalMs = 500
        });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var testClient = new ClaudeCodeUIClient(httpClient, Mock.Of<ILogger<ClaudeCodeUIClient>>());

        var portOptions = Options.Create(new OpenCodeOptions
        {
            BasePort = testPort,
            MaxConcurrentServers = 1
        });
        var testPortAllocationService = new PortAllocationService(
            portOptions,
            Mock.Of<ILogger<PortAllocationService>>());

        var testServerManager = new ClaudeCodeUIServerManager(
            testOptions,
            testClient,
            testPortAllocationService,
            Mock.Of<IGitHubEnvironmentService>(),
            Mock.Of<ILogger<ClaudeCodeUIServerManager>>());

        ClaudeCodeUIServer? server = null;

        try
        {
            Console.WriteLine("=== Claude Code UI Integration Test ===");
            Console.WriteLine($"Temp directory: {testTempDir}");
            Console.WriteLine($"Test port: {testPort}");
            Console.WriteLine();

            // Step 1: Verify cloudcli installation
            Console.WriteLine("=== Step 1: Verify cloudcli Installation ===");
            var cloudcliPath = FindCloudcliPath();
            if (cloudcliPath == null)
            {
                Console.WriteLine("ERROR: Could not find cloudcli in PATH or npm global directory");
                Console.WriteLine("Please install @siteboon/claude-code-ui: npm install -g @siteboon/claude-code-ui");
                throw new InconclusiveException("cloudcli not installed");
            }

            Console.WriteLine($"Found cloudcli at: {cloudcliPath}");

            // Update options to use the found path
            testOptions = Options.Create(new ClaudeCodeUIOptions
            {
                ExecutablePath = cloudcliPath,
                BasePort = testPort,
                MaxConcurrentServers = 1,
                ServerStartTimeoutMs = 60000,
                HealthCheckIntervalMs = 500
            });

            // Recreate server manager with updated options
            testServerManager.Dispose();
            testServerManager = new ClaudeCodeUIServerManager(
                testOptions,
                testClient,
                testPortAllocationService,
                Mock.Of<IGitHubEnvironmentService>(),
                Mock.Of<ILogger<ClaudeCodeUIServerManager>>());

            try
            {
                using var versionProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = cloudcliPath,
                        Arguments = "--version",
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

                Console.WriteLine($"cloudcli version: {versionOutput.Trim()}");
                if (!string.IsNullOrWhiteSpace(versionError))
                    Console.WriteLine($"Version stderr: {versionError}");
                Console.WriteLine($"Exit code: {versionProcess.ExitCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Could not run cloudcli: {ex.Message}");
                throw new InconclusiveException("cloudcli not runnable");
            }
            Console.WriteLine();

            // Step 2: Start server
            Console.WriteLine("=== Step 2: Start Claude Code UI Server ===");
            Console.WriteLine($"Starting server on port {testPort}...");

            server = await testServerManager.StartServerAsync("test-agent", testTempDir);

            Console.WriteLine($"Server started successfully!");
            Console.WriteLine($"Port: {server.Port}");
            Console.WriteLine($"Base URL: {server.BaseUrl}");
            Console.WriteLine($"Web View URL: {server.WebViewUrl}");
            Console.WriteLine($"Status: {server.Status}");
            Console.WriteLine();

            // Step 3: Verify health
            Console.WriteLine("=== Step 3: Health Check ===");
            var isHealthy = await testServerManager.IsHealthyAsync(server);
            Console.WriteLine($"Server healthy: {isHealthy}");
            Assert.That(isHealthy, Is.True, "Server should be healthy");
            Console.WriteLine();

            // Step 4: Send prompt to create a file
            Console.WriteLine("=== Step 4: Send Prompt to Create Test File ===");
            var testFileName = "HelloWorld.cs";
            var prompt = new ClaudeCodeUIPromptRequest
            {
                Message = $"Create a simple C# file named '{testFileName}' in the current directory. " +
                        "The file should contain a basic HelloWorld class with a Main method that prints 'Hello from Claude Code UI!'. " +
                        "Just create the file, no need to compile or run it.",
                ProjectPath = testTempDir,
                Provider = "claude",
                Stream = true
            };

            Console.WriteLine($"Sending prompt: {prompt.Message}");
            Console.WriteLine();

            var response = await testClient.SendPromptAsync(server.BaseUrl, prompt);

            Console.WriteLine("=== Agent Response ===");
            Console.WriteLine($"Success: {response.Success}");
            Console.WriteLine($"Session ID: {response.SessionId}");
            if (!string.IsNullOrEmpty(response.Error))
                Console.WriteLine($"Error: {response.Error}");
            if (!string.IsNullOrEmpty(response.Text))
            {
                Console.WriteLine($"Response text:");
                Console.WriteLine(response.Text);
            }
            Console.WriteLine("=== End Response ===");
            Console.WriteLine();

            // Step 5: Verify file was created
            Console.WriteLine("=== Step 5: Verify File Creation ===");
            var expectedFilePath = Path.Combine(testTempDir, testFileName);
            var fileExists = File.Exists(expectedFilePath);
            Console.WriteLine($"Expected file path: {expectedFilePath}");
            Console.WriteLine($"File exists: {fileExists}");

            if (fileExists)
            {
                var fileContent = await File.ReadAllTextAsync(expectedFilePath);
                Console.WriteLine("File content:");
                Console.WriteLine("---");
                Console.WriteLine(fileContent);
                Console.WriteLine("---");

                Assert.That(fileContent, Does.Contain("Hello").IgnoreCase,
                    "File should contain some greeting code");
            }
            else
            {
                Console.WriteLine("WARNING: File was not created. Listing directory contents:");
                var files = Directory.GetFiles(testTempDir);
                foreach (var file in files)
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)}");
                }
                if (files.Length == 0)
                {
                    Console.WriteLine("  (directory is empty)");
                }
            }
            Console.WriteLine();

            // Step 6: Output URL for manual access
            Console.WriteLine("=== Step 6: Access Information ===");
            Console.WriteLine("==========================================");
            Console.WriteLine("CLAUDE CODE UI IS NOW RUNNING!");
            Console.WriteLine("==========================================");
            Console.WriteLine();
            Console.WriteLine($"Web UI URL: {server.WebViewUrl}");
            Console.WriteLine($"API URL: {server.BaseUrl}");
            Console.WriteLine($"Working Directory: {testTempDir}");
            Console.WriteLine();
            Console.WriteLine("You can now:");
            Console.WriteLine($"  1. Open {server.WebViewUrl} in your browser to interact with the agent");
            Console.WriteLine("  2. Send additional prompts through the web UI");
            Console.WriteLine("  3. View the agent's conversation history");
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+C or wait for test timeout to stop the server.");
            Console.WriteLine();

            // Keep the server running for interactive testing (up to timeout)
            // User can manually explore the UI
            Console.WriteLine("=== Server Running - Waiting for timeout or cancellation ===");

            // Wait for a long time to allow manual interaction
            // The test will be cancelled by the CancelAfter attribute or Ctrl+C
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), TestContext.CurrentContext.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Test cancelled by user or timeout.");
            }

            Console.WriteLine();
            Console.WriteLine("=== TEST COMPLETE ===");
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("=== Cleanup ===");

            if (server != null)
            {
                try
                {
                    Console.WriteLine("Stopping server...");
                    await testServerManager.StopServerAsync("test-agent");
                    Console.WriteLine("Server stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Error stopping server: {ex.Message}");
                }
            }

            // Note: Don't delete temp directory immediately so user can inspect files
            Console.WriteLine($"Temp directory (you can inspect created files): {testTempDir}");

            // Dispose server manager
            testServerManager.Dispose();

            // Schedule cleanup after a delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                if (Directory.Exists(testTempDir))
                {
                    try
                    {
                        Directory.Delete(testTempDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            });
        }
    }

    /// <summary>
    /// Quick test to verify cloudcli can be started and health checked.
    /// Does not send any prompts (no API key required).
    /// </summary>
    [Test]
    [Explicit("Requires cloudcli to be installed. Run manually.")]
    public async Task QuickStartup_HealthCheck_NoPrompt()
    {
        var cloudcliPath = FindCloudcliPath();
        if (cloudcliPath == null)
        {
            throw new InconclusiveException("cloudcli not installed");
        }

        var random = new Random();
        var testPort = random.Next(13000, 14000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"claudeui-quick-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        var testOptions = Options.Create(new ClaudeCodeUIOptions
        {
            ExecutablePath = cloudcliPath,
            BasePort = testPort,
            MaxConcurrentServers = 1,
            ServerStartTimeoutMs = 30000,
            HealthCheckIntervalMs = 500
        });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var testClient = new ClaudeCodeUIClient(httpClient, Mock.Of<ILogger<ClaudeCodeUIClient>>());

        var portOptions = Options.Create(new OpenCodeOptions
        {
            BasePort = testPort,
            MaxConcurrentServers = 1
        });
        var testPortAllocationService = new PortAllocationService(
            portOptions,
            Mock.Of<ILogger<PortAllocationService>>());

        using var testServerManager = new ClaudeCodeUIServerManager(
            testOptions,
            testClient,
            testPortAllocationService,
            Mock.Of<IGitHubEnvironmentService>(),
            Mock.Of<ILogger<ClaudeCodeUIServerManager>>());

        try
        {
            Console.WriteLine($"Starting cloudcli on port {testPort}...");
            var server = await testServerManager.StartServerAsync("quick-test", testTempDir);

            Console.WriteLine($"Server status: {server.Status}");
            Console.WriteLine($"Server URL: {server.WebViewUrl}");

            var isHealthy = await testServerManager.IsHealthyAsync(server);
            Console.WriteLine($"Health check: {isHealthy}");

            Assert.That(server.Status, Is.EqualTo(ClaudeCodeUIServerStatus.Running));
            Assert.That(isHealthy, Is.True);

            Console.WriteLine("SUCCESS: cloudcli started and is healthy!");

            await testServerManager.StopServerAsync("quick-test");
            Console.WriteLine("Server stopped.");
        }
        finally
        {
            if (Directory.Exists(testTempDir))
            {
                try { Directory.Delete(testTempDir, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Finds the cloudcli executable in PATH or common installation locations.
    /// </summary>
    private static string? FindCloudcliPath()
    {
        const string executableName = "cloudcli";

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var path in paths)
        {
            if (OperatingSystem.IsWindows())
            {
                // On Windows, prefer .cmd and .exe over plain files
                var cmdPath = Path.Combine(path, executableName + ".cmd");
                if (File.Exists(cmdPath))
                    return cmdPath;

                var exePath = Path.Combine(path, executableName + ".exe");
                if (File.Exists(exePath))
                    return exePath;
            }

            var fullPath = Path.Combine(path, executableName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Check npm global directory on Windows
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var npmPath = Path.Combine(appData, "npm", $"{executableName}.cmd");
            if (File.Exists(npmPath))
                return npmPath;
        }

        // Check npm global directory on Unix
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var npmPaths = new[]
            {
                Path.Combine(home, ".npm-global", "bin", executableName),
                $"/usr/local/bin/{executableName}",
                $"/usr/bin/{executableName}"
            };

            foreach (var npmPath in npmPaths)
            {
                if (File.Exists(npmPath))
                    return npmPath;
            }
        }

        return null;
    }
}
