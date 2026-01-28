using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Factory for creating ClaudeAgentOptions based on session mode.
/// </summary>
public class SessionOptionsFactory
{
    /// <summary>
    /// Read-only tools available in Plan mode.
    /// </summary>
    private static readonly string[] PlanModeTools =
    [
        "Read",
        "Glob",
        "Grep",
        "WebFetch",
        "WebSearch",
        "Task",
        "AskUserQuestion"
    ];

    /// <summary>
    /// Creates ClaudeAgentOptions for the specified session mode.
    /// </summary>
    /// <param name="mode">The session mode (Plan or Build).</param>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="model">The Claude model to use.</param>
    /// <param name="systemPrompt">Optional system prompt to include.</param>
    /// <returns>Configured ClaudeAgentOptions.</returns>
    public ClaudeAgentOptions Create(SessionMode mode, string workingDirectory, string model, string? systemPrompt = null)
    {
        var options = new ClaudeAgentOptions
        {
            Cwd = workingDirectory,
            Model = model,
            SystemPrompt = systemPrompt,
            SettingSources = [SettingSource.User]  // Enable loading user-level plugins
        };

        if (mode == SessionMode.Plan)
        {
            // Plan mode: read-only tools only
            options.AllowedTools = PlanModeTools.ToList();
        }
        // Build mode: all tools allowed by default (don't set AllowedTools)

        return options;
    }
}
