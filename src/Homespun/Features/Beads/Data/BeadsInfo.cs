using System.Text.Json.Serialization;

namespace Homespun.Features.Beads.Data;

/// <summary>
/// Information about the beads installation for a repository.
/// Maps to the JSON output of 'bd info --json'.
/// </summary>
public class BeadsInfo
{
    /// <summary>
    /// Path to the beads database file.
    /// </summary>
    [JsonPropertyName("database_path")]
    public string? DatabasePath { get; set; }
    
    /// <summary>
    /// The issue ID prefix (e.g., "bd").
    /// </summary>
    [JsonPropertyName("issue_prefix")]
    public string? IssuePrefix { get; set; }
    
    /// <summary>
    /// Whether the beads daemon is running.
    /// </summary>
    [JsonPropertyName("daemon_running")]
    public bool DaemonRunning { get; set; }
    
    /// <summary>
    /// Whether agent mail is enabled.
    /// </summary>
    [JsonPropertyName("agent_mail_enabled")]
    public bool AgentMailEnabled { get; set; }
}
