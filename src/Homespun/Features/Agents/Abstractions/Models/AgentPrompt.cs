namespace Homespun.Features.Agents.Abstractions.Models;

/// <summary>
/// A prompt to send to an agent.
/// </summary>
public class AgentPrompt
{
    /// <summary>
    /// Text content of the prompt.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional model override for this specific prompt.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Optional system prompt override.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Additional file paths to include with the prompt.
    /// </summary>
    public List<string> FilePaths { get; init; } = [];

    /// <summary>
    /// Creates a simple text prompt.
    /// </summary>
    /// <param name="text">The prompt text.</param>
    /// <param name="model">Optional model override.</param>
    /// <returns>A new AgentPrompt instance.</returns>
    public static AgentPrompt FromText(string text, string? model = null)
        => new() { Text = text, Model = model };
}
