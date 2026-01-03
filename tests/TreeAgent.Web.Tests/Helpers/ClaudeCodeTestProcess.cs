using System.Diagnostics;
using System.Text.Json;

namespace TreeAgent.Web.Tests.Helpers;

/// <summary>
/// A Claude Code process wrapper designed for integration testing.
/// Uses --print mode for single queries and stream-json for structured output.
/// Based on the approach used by happy-cli (slopus/happy-cli).
/// </summary>
public class ClaudeCodeTestProcess(string claudeCodePath, string workingDirectory, string? systemPrompt = null)
    : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Executes a single query against Claude Code and returns the result.
    /// Uses --print mode which completes after the response is generated.
    /// </summary>
    /// <param name="prompt">The prompt to send to Claude</param>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The query result containing all messages and final result</returns>
    public async Task<ClaudeQueryResult> QueryAsync(
        string prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = new ClaudeQueryResult();
        var tcs = new TaskCompletionSource<bool>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var args = BuildArguments(prompt);
        var startInfo = new ProcessStartInfo
        {
            FileName = claudeCodePath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            try
            {
                var message = ParseMessage(e.Data);
                if (message != null)
                {
                    result.Messages.Add(message);

                    switch (message.Type)
                    {
                        case "system":
                            result.SessionId = message.SessionId;
                            result.SystemMessageReceived = true;
                            break;
                        case "assistant":
                            result.AssistantMessages.Add(e.Data);
                            break;
                        case "result":
                            result.IsComplete = true;
                            result.IsSuccess = message.Subtype == "success";
                            result.FinalResult = message.Result;
                            tcs.TrySetResult(true);
                            break;
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON output, add as raw message
                result.RawOutput.Add(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                result.ErrorOutput.Add(e.Data);
            }
        };

        process.Exited += (sender, e) =>
        {
            result.ExitCode = process.ExitCode;
            tcs.TrySetResult(true);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion or timeout
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                await tcs.Task;
            }

            // Give a small grace period for output to be processed
            await Task.Delay(100, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            result.TimedOut = true;
            try
            {
                process.Kill();
            }
            catch { }
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch { }
            }
        }

        return result;
    }

    private string BuildArguments(string prompt)
    {
        // Use --print for one-shot queries that complete after response
        // Use --output-format stream-json for structured JSON output
        // Use --verbose to get system messages with session info
        var args = new List<string>
        {
            "--print",
            EscapeArgument(prompt),
            "--output-format", "stream-json",
            "--verbose"
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            args.Add("--system-prompt");
            args.Add(EscapeArgument(systemPrompt));
        }

        return string.Join(" ", args);
    }

    private static string EscapeArgument(string arg)
    {
        // Escape for Windows command line
        if (arg.Contains('"') || arg.Contains(' ') || arg.Contains('\n'))
        {
            return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }
        return arg;
    }

    private static ClaudeMessage? ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            if (string.IsNullOrEmpty(type))
                return null;

            return new ClaudeMessage
            {
                Type = type,
                Subtype = root.TryGetProperty("subtype", out var subtypeProp)
                    ? subtypeProp.GetString()
                    : null,
                SessionId = root.TryGetProperty("session_id", out var sessionProp)
                    ? sessionProp.GetString()
                    : null,
                Result = root.TryGetProperty("result", out var resultProp)
                    ? resultProp.GetString()
                    : null,
                RawJson = json
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// Represents a parsed message from Claude Code's stream-json output.
/// </summary>
public class ClaudeMessage
{
    public string Type { get; set; } = "";
    public string? Subtype { get; set; }
    public string? SessionId { get; set; }
    public string? Result { get; set; }
    public string RawJson { get; set; } = "";
}

/// <summary>
/// Result of a Claude Code query execution.
/// </summary>
public class ClaudeQueryResult
{
    public List<ClaudeMessage> Messages { get; } = new();
    public List<string> AssistantMessages { get; } = new();
    public List<string> RawOutput { get; } = new();
    public List<string> ErrorOutput { get; } = new();
    public string? SessionId { get; set; }
    public bool SystemMessageReceived { get; set; }
    public bool IsComplete { get; set; }
    public bool IsSuccess { get; set; }
    public bool TimedOut { get; set; }
    public int? ExitCode { get; set; }
    public string? FinalResult { get; set; }

    /// <summary>
    /// Gets the combined text from all assistant messages.
    /// </summary>
    public string GetAssistantText()
    {
        var texts = new List<string>();
        foreach (var msg in Messages.Where(m => m.Type == "assistant"))
        {
            try
            {
                using var doc = JsonDocument.Parse(msg.RawJson);
                if (doc.RootElement.TryGetProperty("message", out var messageProp) &&
                    messageProp.TryGetProperty("content", out var contentProp) &&
                    contentProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var content in contentProp.EnumerateArray())
                    {
                        if (content.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "text" &&
                            content.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                                texts.Add(text);
                        }
                    }
                }
            }
            catch { }
        }
        return string.Join(" ", texts);
    }
}
