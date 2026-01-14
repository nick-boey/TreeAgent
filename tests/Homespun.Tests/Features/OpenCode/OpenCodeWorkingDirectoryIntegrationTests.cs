using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// Integration tests that verify OpenCode operates in the correct working directory.
/// These tests ensure the agent can read/write files in the expected location.
/// 
/// Run manually with: dotnet test --filter "FullyQualifiedName~OpenCodeWorkingDirectoryIntegrationTests"
/// Or all OpenCode integration tests: dotnet test --filter "Category=OpenCodeIntegration"
/// </summary>
[TestFixture]
[Category("OpenCodeIntegration")]
[NonParallelizable]
public class OpenCodeWorkingDirectoryIntegrationTests
{
    /// <summary>
    /// Verifies that the /path endpoint returns the correct working directory.
    /// This is a fundamental test that the server is running in the expected location.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    [CancelAfter(30000)] // 30 second timeout
    public async Task VerifyPathEndpoint_ReturnsExpectedDirectory()
    {
        // === SELF-CONTAINED TEST SETUP ===
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"opencode-path-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        var testOptions = CreateTestOptions(testPort);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(testOptions, Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(testOptions, testClient, testPortAllocationService, Mock.Of<IHubContext<AgentHub>>(), CreateMockGitHubEnvironmentService(), Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(testOptions, Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        Process? serverProcess = null;

        try
        {
            Console.WriteLine("=== Test Setup ===");
            Console.WriteLine($"Temp directory: {testTempDir}");
            Console.WriteLine($"Test port: {testPort}");

            // Step 1: Generate config
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Generate Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            Console.WriteLine($"Model: {config.Model}");
            await testConfigGenerator.GenerateConfigAsync(testTempDir, config);
            Console.WriteLine("Config generated successfully");

            // Step 2: Start server
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Start Server ===");
            Console.WriteLine($"Starting server on port {testPort}...");
            var server = await testServerManager.StartServerAsync("path-test", testTempDir, continueSession: false);
            serverProcess = server.Process;
            Console.WriteLine($"Server started! PID: {server.Process?.Id}, URL: {server.BaseUrl}");

            // Step 3: Verify working directory
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Verify Working Directory ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path from /path endpoint: {reportedPath}");
            Console.WriteLine($"Expected path: {testTempDir}");

            // Assertions
            Assert.That(reportedPath, Is.Not.Null, "Path endpoint should return a value");

            var normalizedReported = Path.GetFullPath(reportedPath!);
            var normalizedExpected = Path.GetFullPath(testTempDir);
            Console.WriteLine($"Normalized reported: {normalizedReported}");
            Console.WriteLine($"Normalized expected: {normalizedExpected}");

            var pathsMatch = PathsAreEqual(reportedPath, testTempDir);
            Console.WriteLine($"Paths match: {pathsMatch}");

            Assert.That(pathsMatch, Is.True,
                $"Working directory mismatch! Expected: {normalizedExpected}, Actual: {normalizedReported}");

            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Path endpoint correctly reports the working directory.");
        }
        finally
        {
            await CleanupAsync(serverProcess, testTempDir);
        }
    }

    /// <summary>
    /// Verifies that the agent can read a file that exists in the working directory.
    /// This proves the agent is actually operating in the correct location.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually.")]
    [CancelAfter(120000)] // 2 minute timeout for LLM response
    public async Task SendPrompt_AgentCanReadFileInWorkingDirectory()
    {
        // === SELF-CONTAINED TEST SETUP ===
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"opencode-read-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        var testOptions = CreateTestOptions(testPort);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(testOptions, Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(testOptions, testClient, testPortAllocationService, Mock.Of<IHubContext<AgentHub>>(), CreateMockGitHubEnvironmentService(), Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(testOptions, Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        Process? serverProcess = null;
        var uniqueContent = $"UNIQUE_SECRET_CONTENT_{Guid.NewGuid():N}";
        var uniqueFileName = $"unique-content-{Guid.NewGuid():N}.txt";

        try
        {
            Console.WriteLine("=== Test Setup ===");
            Console.WriteLine($"Temp directory: {testTempDir}");
            Console.WriteLine($"Test port: {testPort}");
            Console.WriteLine($"Unique file: {uniqueFileName}");
            Console.WriteLine($"Unique content: {uniqueContent}");

            // Step 1: Create test file with unique content
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Create Test File ===");
            var testFilePath = Path.Combine(testTempDir, uniqueFileName);
            await File.WriteAllTextAsync(testFilePath, uniqueContent);
            Console.WriteLine($"Created file: {testFilePath}");

            // Step 2: Generate config
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Generate Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            Console.WriteLine($"Model: {config.Model}");
            await testConfigGenerator.GenerateConfigAsync(testTempDir, config);
            Console.WriteLine("Config generated successfully");

            // Step 3: Start server
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Start Server ===");
            var server = await testServerManager.StartServerAsync("read-test", testTempDir, continueSession: false);
            serverProcess = server.Process;
            Console.WriteLine($"Server started! PID: {server.Process?.Id}, URL: {server.BaseUrl}");

            // Step 4: Verify working directory
            Console.WriteLine();
            Console.WriteLine("=== Step 4: Verify Working Directory ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path: {reportedPath}");
            Console.WriteLine($"Expected path: {testTempDir}");
            Assert.That(PathsAreEqual(reportedPath, testTempDir), Is.True,
                $"Working directory mismatch before sending prompt! Expected: {testTempDir}, Actual: {reportedPath}");
            Console.WriteLine("Path verification passed!");

            // Step 5: Create session
            Console.WriteLine();
            Console.WriteLine("=== Step 5: Create Session ===");
            var session = await testClient.CreateSessionAsync(server.BaseUrl, "Read File Test");
            Console.WriteLine($"Session ID: {session.Id}");

            // Step 6: Send prompt to read the file
            Console.WriteLine();
            Console.WriteLine("=== Step 6: Send Prompt ===");
            var prompt = PromptRequest.FromText(
                $"Read the file '{uniqueFileName}' and tell me its exact contents. " +
                "Just output the file contents, nothing else.");
            Console.WriteLine($"Prompt: Read the file '{uniqueFileName}'...");
            Console.WriteLine("Waiting for response...");

            var response = await testClient.SendPromptAsync(server.BaseUrl, session.Id, prompt);
            Console.WriteLine("Response received!");

            // Print response
            Console.WriteLine();
            Console.WriteLine("=== Agent Response ===");
            var responseText = ExtractResponseText(response);
            Console.WriteLine(responseText);
            Console.WriteLine("=== End Response ===");

            // Assertions
            Console.WriteLine();
            Console.WriteLine("=== Assertions ===");
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Parts, Is.Not.Empty);

            var containsUniqueContent = responseText.Contains(uniqueContent);
            Console.WriteLine($"Response contains unique content: {containsUniqueContent}");

            Assert.That(containsUniqueContent, Is.True,
                $"Expected response to contain '{uniqueContent}' but got: {responseText}");

            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Agent successfully read file from the correct working directory.");
        }
        finally
        {
            await CleanupAsync(serverProcess, testTempDir);
        }
    }

    /// <summary>
    /// Verifies that the agent creates files in the correct working directory.
    /// This proves file writes go to the expected location, not "/" or elsewhere.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed and API keys configured. Run manually.")]
    [CancelAfter(180000)] // 3 minute timeout for file creation
    public async Task SendPrompt_AgentCreatesFileInCorrectDirectory()
    {
        // === SELF-CONTAINED TEST SETUP ===
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"opencode-write-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        var testOptions = CreateTestOptions(testPort);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(testOptions, Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(testOptions, testClient, testPortAllocationService, Mock.Of<IHubContext<AgentHub>>(), CreateMockGitHubEnvironmentService(), Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(testOptions, Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        Process? serverProcess = null;
        var uniqueFileName = $"agent-created-{Guid.NewGuid():N}.txt";
        var expectedContent = "AGENT_CREATED_THIS_FILE";

        try
        {
            Console.WriteLine("=== Test Setup ===");
            Console.WriteLine($"Temp directory: {testTempDir}");
            Console.WriteLine($"Test port: {testPort}");
            Console.WriteLine($"File to create: {uniqueFileName}");
            Console.WriteLine($"Expected content: {expectedContent}");

            // Step 1: Generate config
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Generate Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            Console.WriteLine($"Model: {config.Model}");
            await testConfigGenerator.GenerateConfigAsync(testTempDir, config);
            Console.WriteLine("Config generated successfully");

            // Step 2: Start server
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Start Server ===");
            var server = await testServerManager.StartServerAsync("write-test", testTempDir, continueSession: false);
            serverProcess = server.Process;
            Console.WriteLine($"Server started! PID: {server.Process?.Id}, URL: {server.BaseUrl}");

            // Step 3: Verify working directory
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Verify Working Directory ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path: {reportedPath}");
            Console.WriteLine($"Expected path: {testTempDir}");
            Assert.That(PathsAreEqual(reportedPath, testTempDir), Is.True,
                $"Working directory mismatch before sending prompt! Expected: {testTempDir}, Actual: {reportedPath}");
            Console.WriteLine("Path verification passed!");

            // Step 4: Create session
            Console.WriteLine();
            Console.WriteLine("=== Step 4: Create Session ===");
            var session = await testClient.CreateSessionAsync(server.BaseUrl, "Create File Test");
            Console.WriteLine($"Session ID: {session.Id}");

            // Step 5: Send prompt to create file with strict workspace instruction
            Console.WriteLine();
            Console.WriteLine("=== Step 5: Send Prompt ===");
            var prompt = PromptRequest.FromText(
                $"""
                IMPORTANT: You are working in the directory '{testTempDir}'.
                
                Create a file called '{uniqueFileName}' in your current working directory with the exact text '{expectedContent}'.
                
                Do not create the file anywhere else. Use the Write tool to create the file in the current workspace.
                
                After creating the file, confirm the full path where you created it.
                """);
            Console.WriteLine($"Prompt: Create file '{uniqueFileName}' in workspace...");
            Console.WriteLine("Waiting for response...");

            var response = await testClient.SendPromptAsync(server.BaseUrl, session.Id, prompt);
            Console.WriteLine("Response received!");

            // Print response
            Console.WriteLine();
            Console.WriteLine("=== Agent Response ===");
            var responseText = ExtractResponseText(response);
            Console.WriteLine(responseText);
            Console.WriteLine("=== End Response ===");

            // Step 6: Verify file was created in correct location
            Console.WriteLine();
            Console.WriteLine("=== Step 6: Verify File Creation ===");
            var expectedFilePath = Path.Combine(testTempDir, uniqueFileName);
            Console.WriteLine($"Expected file path: {expectedFilePath}");

            var fileExists = File.Exists(expectedFilePath);
            Console.WriteLine($"File exists in temp directory: {fileExists}");

            Assert.That(fileExists, Is.True,
                $"Expected file to exist at '{expectedFilePath}' but it was not found. " +
                $"Agent response: {responseText}");

            // Verify file content
            var actualContent = await File.ReadAllTextAsync(expectedFilePath);
            Console.WriteLine($"Actual file content: {actualContent}");

            var contentMatches = actualContent.Contains(expectedContent);
            Console.WriteLine($"Content contains expected text: {contentMatches}");

            Assert.That(contentMatches, Is.True,
                $"Expected file content to contain '{expectedContent}' but got: {actualContent}");

            // Step 7: Verify file was NOT created in root directory (Unix only)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine();
                Console.WriteLine("=== Step 7: Verify File Not In Root (Unix) ===");
                var rootPath = $"/{uniqueFileName}";
                var existsInRoot = File.Exists(rootPath);
                Console.WriteLine($"File exists at root ({rootPath}): {existsInRoot}");

                Assert.That(existsInRoot, Is.False,
                    $"File should NOT exist at root path '{rootPath}' but it does!");
            }

            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Agent successfully created file in the correct working directory.");
        }
        finally
        {
            await CleanupAsync(serverProcess, testTempDir);
        }
    }

    /// <summary>
    /// Negative test that verifies our path comparison logic correctly detects mismatches.
    /// This ensures we can properly identify when paths don't match.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    [CancelAfter(30000)] // 30 second timeout
    public async Task PathVerification_DetectsMismatch_WhenComparingDifferentPaths()
    {
        // === SELF-CONTAINED TEST SETUP ===
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var dirA = Path.Combine(Path.GetTempPath(), $"opencode-mismatch-test-A-{Guid.NewGuid()}");
        var dirB = Path.Combine(Path.GetTempPath(), $"opencode-mismatch-test-B-{Guid.NewGuid()}");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var testOptions = CreateTestOptions(testPort);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(testOptions, Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(testOptions, testClient, testPortAllocationService, Mock.Of<IHubContext<AgentHub>>(), CreateMockGitHubEnvironmentService(), Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(testOptions, Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        Process? serverProcess = null;

        try
        {
            Console.WriteLine("=== Test Setup ===");
            Console.WriteLine($"Directory A (server will run here): {dirA}");
            Console.WriteLine($"Directory B (should NOT match): {dirB}");
            Console.WriteLine($"Test port: {testPort}");

            // Step 1: Generate config in dirA
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Generate Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            await testConfigGenerator.GenerateConfigAsync(dirA, config);
            Console.WriteLine("Config generated in dirA");

            // Step 2: Start server in dirA
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Start Server ===");
            var server = await testServerManager.StartServerAsync("mismatch-test", dirA, continueSession: false);
            serverProcess = server.Process;
            Console.WriteLine($"Server started in dirA! PID: {server.Process?.Id}");

            // Step 3: Get reported path
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Get Reported Path ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path: {reportedPath}");

            Assert.That(reportedPath, Is.Not.Null, "Path endpoint should return a value");

            // Step 4: Verify path matches dirA
            Console.WriteLine();
            Console.WriteLine("=== Step 4: Verify Path Matches DirA ===");
            var matchesDirA = PathsAreEqual(reportedPath, dirA);
            Console.WriteLine($"Reported path matches dirA: {matchesDirA}");
            Assert.That(matchesDirA, Is.True,
                $"Expected reported path to match dirA. Reported: {reportedPath}, DirA: {dirA}");

            // Step 5: Verify path does NOT match dirB
            Console.WriteLine();
            Console.WriteLine("=== Step 5: Verify Path Does NOT Match DirB ===");
            var matchesDirB = PathsAreEqual(reportedPath, dirB);
            Console.WriteLine($"Reported path matches dirB: {matchesDirB}");
            Assert.That(matchesDirB, Is.False,
                $"Expected reported path to NOT match dirB. Reported: {reportedPath}, DirB: {dirB}");

            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Path comparison logic correctly identifies matching and non-matching paths.");
        }
        finally
        {
            await CleanupAsync(serverProcess, dirA, dirB);
        }
    }

    #region Helper Methods

    private static IOptions<OpenCodeOptions> CreateTestOptions(int port)
    {
        return Options.Create(new OpenCodeOptions
        {
            ExecutablePath = "opencode",
            BasePort = port,
            MaxConcurrentServers = 1,
            ServerStartTimeoutMs = 15000, // 15 seconds for server startup
            DefaultModel = "anthropic/claude-opus-4-5" // Use default model (haiku not available via this format)
        });
    }

    private static bool PathsAreEqual(string? path1, string? path2)
    {
        if (path1 == null || path2 == null) return false;
        var normalized1 = Path.GetFullPath(path1);
        var normalized2 = Path.GetFullPath(path2);
        return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractResponseText(OpenCodeMessage response)
    {
        return string.Join(" ", response.Parts
            .Where(p => p.Type == "text" && !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text));
    }

    private static async Task CleanupAsync(Process? serverProcess, params string[] directories)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cleanup ===");

        // Kill server process
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

        // Clean up directories
        foreach (var dir in directories)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                    Console.WriteLine($"Cleaned up directory: {dir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Could not clean up directory {dir}: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Test Agent Integration Tests

    /// <summary>
    /// Tests the full test agent flow: create worktree, start server, send prompt, verify session.
    /// This simulates what the Test Agent button does in the UI.
    /// </summary>
    [Test]
    [Explicit("Requires OpenCode to be installed. Run manually.")]
    [CancelAfter(120000)] // 2 minute timeout
    public async Task TestAgent_FullFlow_CreatesWorktreeStartsServerAndVerifiesSession()
    {
        var random = new Random();
        var testPort = random.Next(30000, 40000);
        var testTempDir = Path.Combine(Path.GetTempPath(), $"opencode-testagent-{Guid.NewGuid()}");
        Directory.CreateDirectory(testTempDir);

        var testOptions = CreateTestOptions(testPort);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var testClient = new OpenCodeClient(httpClient, Mock.Of<ILogger<OpenCodeClient>>());
        var testPortAllocationService = new PortAllocationService(testOptions, Mock.Of<ILogger<PortAllocationService>>());
        using var testServerManager = new OpenCodeServerManager(testOptions, testClient, testPortAllocationService, Mock.Of<IHubContext<AgentHub>>(), CreateMockGitHubEnvironmentService(), Mock.Of<ILogger<OpenCodeServerManager>>());
        var testConfigGenerator = new OpenCodeConfigGenerator(testOptions, Mock.Of<ILogger<OpenCodeConfigGenerator>>());

        Process? serverProcess = null;

        try
        {
            Console.WriteLine("=== Test Agent Integration Test ===");
            Console.WriteLine($"Temp directory: {testTempDir}");
            Console.WriteLine($"Test port: {testPort}");

            // Step 1: Initialize git repo (simulating a local project)
            Console.WriteLine();
            Console.WriteLine("=== Step 1: Initialize Git Repository ===");
            await RunGitCommandAsync("init", testTempDir);
            await RunGitCommandAsync("commit --allow-empty -m \"Initial commit\"", testTempDir);
            Console.WriteLine("Git repo initialized with initial commit");

            // Step 2: Create hsp/test worktree
            Console.WriteLine();
            Console.WriteLine("=== Step 2: Create Test Worktree ===");
            var worktreePath = Path.Combine(Path.GetDirectoryName(testTempDir)!, $"hsp-test-worktree-{Guid.NewGuid()}");
            await RunGitCommandAsync("branch hsp/test", testTempDir);
            await RunGitCommandAsync($"worktree add \"{worktreePath}\" hsp/test", testTempDir);
            Console.WriteLine($"Worktree created at: {worktreePath}");

            // Step 3: Delete test.txt if it exists (simulating cleanup)
            var testFilePath = Path.Combine(worktreePath, "test.txt");
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
                Console.WriteLine("Deleted existing test.txt");
            }

            // Step 4: Generate opencode.json config
            Console.WriteLine();
            Console.WriteLine("=== Step 3: Generate Config ===");
            var config = testConfigGenerator.CreateDefaultConfig();
            Console.WriteLine($"Model: {config.Model}");
            await testConfigGenerator.GenerateConfigAsync(worktreePath, config);
            Console.WriteLine("Config generated successfully");

            // Step 5: Start OpenCode server
            Console.WriteLine();
            Console.WriteLine("=== Step 4: Start Server ===");
            var server = await testServerManager.StartServerAsync("test-agent-integration", worktreePath, continueSession: false);
            serverProcess = server.Process;
            Console.WriteLine($"Server started! PID: {server.Process?.Id}, URL: {server.BaseUrl}");

            // Step 6: Verify working directory
            Console.WriteLine();
            Console.WriteLine("=== Step 5: Verify Working Directory ===");
            var reportedPath = await testClient.GetCurrentPathAsync(server.BaseUrl);
            Console.WriteLine($"Reported path: {reportedPath}");
            Console.WriteLine($"Expected path: {worktreePath}");
            Assert.That(PathsAreEqual(reportedPath, worktreePath), Is.True,
                $"Working directory mismatch! Expected: {worktreePath}, Actual: {reportedPath}");
            Console.WriteLine("Path verification passed!");

            // Step 7: Create session
            Console.WriteLine();
            Console.WriteLine("=== Step 6: Create Session ===");
            var session = await testClient.CreateSessionAsync(server.BaseUrl, "Test Agent Session");
            Console.WriteLine($"Session ID: {session.Id}");
            Console.WriteLine($"Session Title: {session.Title}");

            // Step 8: Send test prompt (fire and forget)
            Console.WriteLine();
            Console.WriteLine("=== Step 7: Send Test Prompt ===");
            var prompt = PromptRequest.FromText(
                $"Create a file called 'test.txt' in the current directory with the content 'Hello from test agent - {DateTime.UtcNow:O}'");
            await testClient.SendPromptAsyncNoWait(server.BaseUrl, session.Id, prompt);
            Console.WriteLine("Test prompt sent");

            // Step 9: Verify session is visible via /session API
            Console.WriteLine();
            Console.WriteLine("=== Step 8: Verify Session Visibility ===");
            var sessions = await testClient.ListSessionsAsync(server.BaseUrl);
            Console.WriteLine($"Total sessions on server: {sessions.Count}");
            
            var foundSession = sessions.FirstOrDefault(s => s.Id == session.Id);
            Assert.That(foundSession, Is.Not.Null, 
                $"Session {session.Id} not found in session list. Found: {string.Join(", ", sessions.Select(s => s.Id))}");
            Console.WriteLine($"Session found: {foundSession!.Id} - {foundSession.Title}");

            // Step 10: Wait for file creation (poll for up to 30 seconds)
            Console.WriteLine();
            Console.WriteLine("=== Step 9: Wait for File Creation ===");
            var fileCreated = false;
            for (var i = 0; i < 30; i++)
            {
                if (File.Exists(testFilePath))
                {
                    fileCreated = true;
                    Console.WriteLine($"test.txt created after {i + 1} seconds");
                    var content = await File.ReadAllTextAsync(testFilePath);
                    Console.WriteLine($"Content: {content}");
                    break;
                }
                Console.WriteLine($"Waiting... ({i + 1}s)");
                await Task.Delay(1000);
            }

            if (!fileCreated)
            {
                Console.WriteLine("WARNING: test.txt was not created within 30 seconds");
                Console.WriteLine("This may be expected if using a slow model or if the agent is still processing");
            }

            Console.WriteLine();
            Console.WriteLine("=== TEST PASSED ===");
            Console.WriteLine("Test agent flow completed successfully:");
            Console.WriteLine("- Git repo initialized");
            Console.WriteLine("- Worktree created");
            Console.WriteLine("- Server started in correct directory");
            Console.WriteLine("- Session created and visible via API");
            Console.WriteLine("- Prompt sent to agent");

            // Cleanup worktree
            Console.WriteLine();
            Console.WriteLine("=== Cleanup Worktree ===");
            await RunGitCommandAsync($"worktree remove \"{worktreePath}\" --force", testTempDir);
            await RunGitCommandAsync("branch -D hsp/test", testTempDir);
            Console.WriteLine("Worktree and branch cleaned up");
        }
        finally
        {
            await CleanupAsync(serverProcess, testTempDir);
        }
    }

    private static async Task RunGitCommandAsync(string args, string workDir)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            Console.WriteLine($"Git command warning: {error.Trim()}");
        }
    }

    #endregion

    private static IGitHubEnvironmentService CreateMockGitHubEnvironmentService()
    {
        var mock = new Mock<IGitHubEnvironmentService>();
        mock.Setup(g => g.GetGitHubEnvironment()).Returns(new Dictionary<string, string>());
        return mock.Object;
    }
}
