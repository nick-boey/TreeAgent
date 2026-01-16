using System.Text.Json.Serialization;

namespace Homespun.Features.ClaudeCodeUI.Models;

/// <summary>
/// Request body for sending a prompt to Claude Code UI's /api/agent endpoint.
/// </summary>
public class ClaudeCodeUIPromptRequest
{
    /// <summary>
    /// The task/prompt message for the agent.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Path to the project directory.
    /// </summary>
    [JsonPropertyName("projectPath")]
    public required string ProjectPath { get; init; }

    /// <summary>
    /// AI provider: "claude", "cursor", or "codex".
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "claude";

    /// <summary>
    /// Model identifier (e.g., "claude-sonnet-4-20250514").
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Enable SSE streaming for real-time updates.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    /// <summary>
    /// Optional session ID to continue a conversation.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

/// <summary>
/// Response from Claude Code UI prompt.
/// </summary>
public class ClaudeCodeUIResponse
{
    /// <summary>
    /// Session ID for the conversation.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Response text from the agent.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Whether the response completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if any.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Exit code from the agent.
    /// </summary>
    public int? ExitCode { get; set; }
}

/// <summary>
/// Represents a project in Claude Code UI.
/// </summary>
public class ClaudeCodeUIProject
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("lastAccessed")]
    public DateTime? LastAccessed { get; set; }
}

/// <summary>
/// Represents a session in Claude Code UI.
/// </summary>
public class ClaudeCodeUISession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }
}

/// <summary>
/// Represents an event from Claude Code UI SSE stream.
/// </summary>
public class ClaudeCodeUIEvent
{
    /// <summary>
    /// Event type.
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Session ID if applicable.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Event data payload.
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Text content if this is a text message.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Tool name if this is a tool event.
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Error message if any.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Known Claude Code UI event types.
/// </summary>
public static class ClaudeCodeUIEventTypes
{
    public const string SessionCreated = "session-created";
    public const string ClaudeResponse = "claude-response";
    public const string ClaudeComplete = "claude-complete";
    public const string ClaudeError = "claude-error";
    public const string TokenBudget = "token-budget";
}

/// <summary>
/// Represents a running Claude Code UI server.
/// </summary>
public class ClaudeCodeUIServer
{
    /// <summary>
    /// External hostname for generating URLs accessible from outside the container.
    /// Set at application startup from configuration.
    /// </summary>
    public static string? ExternalHostname { get; set; }

    /// <summary>
    /// Unique server ID.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Entity ID this server is associated with.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Working directory.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Server port.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Internal base URL for server-to-server communication (always localhost).
    /// </summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// External base URL for UI links. Uses ExternalHostname if configured, otherwise localhost.
    /// </summary>
    public string ExternalBaseUrl => !string.IsNullOrEmpty(ExternalHostname)
        ? $"https://{ExternalHostname}:{Port}"
        : BaseUrl;

    /// <summary>
    /// Gets the full web view URL including session if available.
    /// Uses external hostname if configured.
    /// </summary>
    public string? WebViewUrl => ActiveSessionId != null
        ? $"{ExternalBaseUrl}/session/{ActiveSessionId}"
        : ExternalBaseUrl;

    /// <summary>
    /// The process running the server.
    /// </summary>
    public System.Diagnostics.Process? Process { get; set; }

    /// <summary>
    /// When the server was started.
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Server status.
    /// </summary>
    public ClaudeCodeUIServerStatus Status { get; set; } = ClaudeCodeUIServerStatus.Starting;

    /// <summary>
    /// Active session ID.
    /// </summary>
    public string? ActiveSessionId { get; set; }
}

/// <summary>
/// Server status.
/// </summary>
public enum ClaudeCodeUIServerStatus
{
    Starting,
    Running,
    Stopped,
    Failed
}
