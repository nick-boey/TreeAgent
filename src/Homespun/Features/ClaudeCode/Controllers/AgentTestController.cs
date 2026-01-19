using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// Test controller for debugging agent communication.
/// </summary>
[ApiController]
[Route("test/agent")]
public class AgentTestController : ControllerBase
{
    private readonly IClaudeSessionService _sessionService;
    private readonly SessionOptionsFactory _optionsFactory;
    private readonly ILogger<AgentTestController> _logger;

    public AgentTestController(
        IClaudeSessionService sessionService,
        SessionOptionsFactory optionsFactory,
        ILogger<AgentTestController> logger)
    {
        _sessionService = sessionService;
        _optionsFactory = optionsFactory;
        _logger = logger;
    }

    /// <summary>
    /// Test basic CLI invocation with a simple prompt.
    /// </summary>
    [HttpGet("cli-test")]
    public async Task<IActionResult> TestCliInvocation([FromQuery] string prompt = "Say 'Hello World' and nothing else.")
    {
        _logger.LogInformation("Testing CLI invocation with prompt: {Prompt}", prompt);

        var results = new List<object>();

        try
        {
            var options = new ClaudeAgentOptions
            {
                Model = "sonnet",
                PermissionMode = PermissionMode.BypassPermissions,
                IncludePartialMessages = true,
                SettingSources = [],
                Cwd = Directory.GetCurrentDirectory()
            };

            await using var client = new ClaudeSdkClient(options);
            await client.ConnectAsync(prompt);

            await foreach (var msg in client.ReceiveMessagesAsync())
            {
                results.Add(new
                {
                    Type = msg.GetType().Name,
                    Data = msg
                });

                if (msg is ResultMessage)
                    break;
            }

            return Ok(new
            {
                Success = true,
                Prompt = prompt,
                MessageCount = results.Count,
                Messages = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI test failed");
            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test session-based messaging with streaming.
    /// </summary>
    [HttpPost("session-test")]
    public async Task<IActionResult> TestSessionMessaging([FromBody] SessionTestRequest request)
    {
        _logger.LogInformation("Testing session messaging with prompt: {Prompt}", request.Prompt);

        try
        {
            // Create a test session
            var session = await _sessionService.StartSessionAsync(
                entityId: $"test-{Guid.NewGuid():N}",
                projectId: "test-project",
                workingDirectory: request.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                mode: request.Mode,
                model: request.Model ?? "sonnet",
                systemPrompt: request.SystemPrompt);

            _logger.LogInformation("Created session {SessionId}", session.Id);

            // Send the test message
            await _sessionService.SendMessageAsync(session.Id, request.Prompt);

            // Get the updated session with messages
            var updatedSession = _sessionService.GetSession(session.Id);

            return Ok(new
            {
                Success = true,
                SessionId = session.Id,
                Status = updatedSession?.Status.ToString(),
                MessageCount = updatedSession?.Messages.Count,
                Messages = updatedSession?.Messages.Select(m => new
                {
                    m.Role,
                    ContentCount = m.Content.Count,
                    Content = m.Content.Select(c => new
                    {
                        Type = c.Type.ToString(),
                        Text = c.Text?.Length > 200 ? c.Text[..200] + "..." : c.Text,
                        ToolName = c.ToolName,
                        ToolInput = c.ToolInput?.Length > 200 ? c.ToolInput[..200] + "..." : c.ToolInput
                    })
                }),
                TotalCost = updatedSession?.TotalCostUsd,
                DurationMs = updatedSession?.TotalDurationMs,
                ConversationId = updatedSession?.ConversationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session test failed");
            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test direct subprocess execution to isolate the issue.
    /// </summary>
    [HttpGet("subprocess-test")]
    public async Task<IActionResult> TestSubprocess([FromQuery] string prompt = "Say hi")
    {
        _logger.LogInformation("Testing subprocess execution");

        var results = new List<string>();
        var errors = new List<string>();

        try
        {
            // Find claude.cmd
            var cliPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "claude.cmd"))
                .FirstOrDefault(System.IO.File.Exists)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd");

            if (!System.IO.File.Exists(cliPath))
            {
                return Ok(new
                {
                    Success = false,
                    Error = $"Claude CLI not found at {cliPath}"
                });
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = cliPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            // Build arguments similar to SubprocessCliTransport
            var args = new[]
            {
                "--output-format", "stream-json",
                "--verbose",
                "--model", "sonnet",
                "--permission-mode", "bypassPermissions",
                "--include-partial-messages",
                "--setting-sources", "",
                "--print",
                "--",
                prompt
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _logger.LogInformation("Starting process: {Cli} with args: {Args}",
                cliPath, string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                return Ok(new { Success = false, Error = "Failed to start process" });
            }

            // Close stdin immediately since we're in --print mode
            process.StandardInput.Close();

            // Read output
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            return Ok(new
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Stdout = output.Length > 2000 ? output[..2000] + "..." : output,
                Stderr = error.Length > 2000 ? error[..2000] + "..." : error,
                Arguments = args
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subprocess test failed");
            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test subprocess with prompt via stdin instead of argument.
    /// </summary>
    [HttpGet("subprocess-stdin-test")]
    public async Task<IActionResult> TestSubprocessWithStdin([FromQuery] string prompt = "Say hi")
    {
        _logger.LogInformation("Testing subprocess execution with stdin");

        try
        {
            // Find claude.cmd
            var cliPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "claude.cmd"))
                .FirstOrDefault(System.IO.File.Exists)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd");

            if (!System.IO.File.Exists(cliPath))
            {
                return Ok(new
                {
                    Success = false,
                    Error = $"Claude CLI not found at {cliPath}"
                });
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = cliPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            // Build arguments - use stdin for prompt instead of positional arg
            var args = new[]
            {
                "--output-format", "stream-json",
                "--verbose",
                "--model", "sonnet",
                "--permission-mode", "bypassPermissions",
                "--include-partial-messages",
                "--setting-sources", "",
                "--print"  // No -- and prompt here, will use stdin
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _logger.LogInformation("Starting process with stdin: {Cli} with args: {Args}",
                cliPath, string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                return Ok(new { Success = false, Error = "Failed to start process" });
            }

            // Write prompt to stdin and close
            await process.StandardInput.WriteLineAsync(prompt);
            process.StandardInput.Close();

            // Read output
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            return Ok(new
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Stdout = output.Length > 2000 ? output[..2000] + "..." : output,
                Stderr = error.Length > 2000 ? error[..2000] + "..." : error,
                Arguments = args,
                StdinPrompt = prompt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subprocess stdin test failed");
            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test tool call streaming - verifies messages are received incrementally.
    /// Creates a test file and asks Claude to read it, tracking each message received.
    /// </summary>
    [HttpGet("tool-call")]
    public async Task<IActionResult> TestToolCallStreaming()
    {
        _logger.LogInformation("Testing tool call streaming");

        var messageLog = new List<object>();
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "test");
        var testFile = Path.Combine(testDir, "streaming-test.txt");

        try
        {
            // Create test directory and file
            if (!Directory.Exists(testDir))
                Directory.CreateDirectory(testDir);

            var testContent = $"Test file created at {DateTime.UtcNow:O}\nThis is line 2.\nThis is line 3.";
            await System.IO.File.WriteAllTextAsync(testFile, testContent);

            var options = new ClaudeAgentOptions
            {
                Model = "sonnet",
                PermissionMode = PermissionMode.BypassPermissions,
                IncludePartialMessages = true,
                SettingSources = [],
                Cwd = testDir
            };

            var prompt = $"Read the file at {testFile} and tell me what's in it. Use the Read tool.";

            await using var client = new ClaudeSdkClient(options);
            await client.ConnectAsync(prompt);

            var messageIndex = 0;
            var streamEventCount = 0;
            var assistantMessageCount = 0;
            var toolUseCount = 0;

            await foreach (var msg in client.ReceiveMessagesAsync())
            {
                var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                var msgType = msg.GetType().Name;

                if (msg is StreamEvent streamEvent)
                {
                    streamEventCount++;
                    // Extract event type from the stream event
                    string? eventType = null;
                    if (streamEvent.Event?.TryGetValue("type", out var typeObj) == true)
                    {
                        eventType = typeObj is System.Text.Json.JsonElement je ? je.GetString() : typeObj?.ToString();
                    }

                    messageLog.Add(new
                    {
                        Index = messageIndex++,
                        Timestamp = timestamp,
                        Type = msgType,
                        EventType = eventType
                    });
                }
                else if (msg is AssistantMessage assistantMsg)
                {
                    assistantMessageCount++;
                    var contentTypes = assistantMsg.Content?.Select(c => c?.GetType().Name).ToList();
                    var hasToolUse = assistantMsg.Content?.Any(c => c is ToolUseBlock) ?? false;
                    if (hasToolUse) toolUseCount++;

                    messageLog.Add(new
                    {
                        Index = messageIndex++,
                        Timestamp = timestamp,
                        Type = msgType,
                        ContentTypes = contentTypes,
                        HasToolUse = hasToolUse
                    });
                }
                else
                {
                    messageLog.Add(new
                    {
                        Index = messageIndex++,
                        Timestamp = timestamp,
                        Type = msgType
                    });
                }

                if (msg is ResultMessage)
                    break;
            }

            // Clean up test file
            if (System.IO.File.Exists(testFile))
                System.IO.File.Delete(testFile);

            var isStreaming = streamEventCount > 5; // Should have many stream events if streaming works

            return Ok(new
            {
                Success = true,
                IsStreaming = isStreaming,
                TotalMessages = messageLog.Count,
                StreamEventCount = streamEventCount,
                AssistantMessageCount = assistantMessageCount,
                ToolUseCount = toolUseCount,
                StreamingVerdict = isStreaming
                    ? "STREAMING WORKING - Multiple stream events received"
                    : "NOT STREAMING - Few stream events, messages may be batched",
                Messages = messageLog
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool call streaming test failed");

            // Clean up on error
            if (System.IO.File.Exists(testFile))
                System.IO.File.Delete(testFile);

            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Test SignalR streaming - verifies that SignalR events reach clients.
    /// Creates a test session, connects as a SignalR client, sends a message,
    /// and compares server events vs client-received events.
    /// </summary>
    [HttpGet("signalr-stream")]
    public async Task<IActionResult> TestSignalRStreaming([FromQuery] string prompt = "Say 'Hello streaming world!' and nothing else.")
    {
        _logger.LogInformation("Testing SignalR streaming");

        var clientEvents = new List<SignalREventRecord>();
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "test");
        ClaudeSession? session = null;
        HubConnection? hubConnection = null;

        try
        {
            // Create test directory if needed
            if (!Directory.Exists(testDir))
                Directory.CreateDirectory(testDir);

            // Create a test session first
            session = await _sessionService.StartSessionAsync(
                entityId: $"signalr-test-{Guid.NewGuid():N}",
                projectId: "test-project",
                workingDirectory: testDir,
                mode: SessionMode.Plan,
                model: "sonnet",
                systemPrompt: "You are a helpful assistant. Keep responses very brief.");

            _logger.LogInformation("Created test session {SessionId}", session.Id);

            // Set up SignalR client connection
            var baseUrl = "http://localhost:5093"; // Default development port
            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/claudecode")
                .WithAutomaticReconnect()
                .Build();

            // Track all events received
            var receivedSessionState = false;
            ClaudeSession? sessionState = null;

            hubConnection.On<ClaudeSession>("SessionState", (s) =>
            {
                receivedSessionState = true;
                sessionState = s;
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "SessionState",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Session {s.Id}, Status: {s.Status}, Messages: {s.Messages.Count}"
                });
            });

            hubConnection.On<ClaudeMessage>("MessageReceived", (message) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "MessageReceived",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Role: {message.Role}, ContentBlocks: {message.Content.Count}"
                });
            });

            hubConnection.On<ClaudeMessageContent>("StreamingContentStarted", (content) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "StreamingContentStarted",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Type: {content.Type}, ToolName: {content.ToolName}"
                });
            });

            hubConnection.On<ClaudeMessageContent, string>("StreamingContentDelta", (content, delta) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "StreamingContentDelta",
                    Timestamp = DateTime.UtcNow,
                    DeltaLength = delta.Length,
                    Details = $"Type: {content.Type}, DeltaLen: {delta.Length}"
                });
            });

            hubConnection.On<ClaudeMessageContent>("StreamingContentStopped", (content) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "StreamingContentStopped",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Type: {content.Type}, FinalTextLen: {content.Text?.Length ?? 0}"
                });
            });

            hubConnection.On<ClaudeMessageContent>("ContentBlockReceived", (content) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "ContentBlockReceived",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Type: {content.Type}"
                });
            });

            hubConnection.On<string, ClaudeSessionStatus>("SessionStatusChanged", (sessionId, status) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "SessionStatusChanged",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Status: {status}"
                });
            });

            hubConnection.On<string, decimal, long>("SessionResultReceived", (sessionId, cost, duration) =>
            {
                clientEvents.Add(new SignalREventRecord
                {
                    Type = "SessionResultReceived",
                    Timestamp = DateTime.UtcNow,
                    Details = $"Cost: ${cost:F6}, Duration: {duration}ms"
                });
            });

            // Connect to SignalR hub
            _logger.LogInformation("Connecting to SignalR hub...");
            await hubConnection.StartAsync();
            _logger.LogInformation("SignalR connected, joining session {SessionId}", session.Id);

            // Join the session group
            await hubConnection.SendAsync("JoinSession", session.Id);

            // Wait for SessionState to be received
            await Task.Delay(100);
            _logger.LogInformation("Received SessionState: {Received}", receivedSessionState);

            // Now send a message that will trigger streaming
            _logger.LogInformation("Sending message to trigger streaming...");
            var sendTask = _sessionService.SendMessageAsync(session.Id, prompt);

            // Wait for message processing to complete
            await sendTask;

            // Give SignalR time to deliver remaining events
            await Task.Delay(500);

            // Count streaming events in client received list
            var streamingDeltaCount = clientEvents.Count(e => e.Type == "StreamingContentDelta");
            var streamingStartCount = clientEvents.Count(e => e.Type == "StreamingContentStarted");
            var streamingStopCount = clientEvents.Count(e => e.Type == "StreamingContentStopped");

            // Get session to check final state
            var finalSession = _sessionService.GetSession(session.Id);

            // Determine if streaming is working
            var isStreamingWorking = streamingDeltaCount > 5;
            var streamingFidelity = streamingDeltaCount > 0 ? 1.0 : 0.0;

            return Ok(new
            {
                Success = true,
                SessionId = session.Id,
                ReceivedSessionState = receivedSessionState,
                ClientEventCount = clientEvents.Count,
                StreamingDeltaCount = streamingDeltaCount,
                StreamingStartCount = streamingStartCount,
                StreamingStopCount = streamingStopCount,
                StreamingFidelity = streamingFidelity,
                Verdict = isStreamingWorking
                    ? "STREAMING WORKING - Multiple delta events received by SignalR client"
                    : "EVENTS MAY BE LOST - Few delta events received, check server-side streaming",
                EventsReceived = clientEvents.Select(e => new
                {
                    e.Type,
                    e.Timestamp,
                    e.DeltaLength,
                    e.Details
                }),
                FinalSessionState = finalSession != null ? new
                {
                    finalSession.Status,
                    MessageCount = finalSession.Messages.Count,
                    finalSession.TotalCostUsd,
                    finalSession.TotalDurationMs
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR streaming test failed");
            return Ok(new
            {
                Success = false,
                Error = ex.Message,
                StackTrace = ex.StackTrace,
                ClientEventsBeforeError = clientEvents.Count,
                EventsReceived = clientEvents.Select(e => new { e.Type, e.Timestamp, e.Details })
            });
        }
        finally
        {
            // Clean up SignalR connection
            if (hubConnection != null)
            {
                try
                {
                    if (session != null)
                    {
                        await hubConnection.SendAsync("LeaveSession", session.Id);
                    }
                    await hubConnection.DisposeAsync();
                }
                catch { }
            }

            // Clean up session
            if (session != null)
            {
                try
                {
                    await _sessionService.StopSessionAsync(session.Id);
                }
                catch { }
            }
        }
    }
}

/// <summary>
/// Record of a SignalR event received by the test client.
/// </summary>
public class SignalREventRecord
{
    public required string Type { get; set; }
    public DateTime Timestamp { get; set; }
    public int? DeltaLength { get; set; }
    public string? Details { get; set; }
}

public class SessionTestRequest
{
    public required string Prompt { get; set; }
    public string? WorkingDirectory { get; set; }
    public SessionMode Mode { get; set; } = SessionMode.Plan;
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
}
