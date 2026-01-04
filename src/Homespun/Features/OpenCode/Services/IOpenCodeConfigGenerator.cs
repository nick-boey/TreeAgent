using Homespun.Features.OpenCode.Models;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Generates opencode.json configuration files for worktrees.
/// </summary>
public interface IOpenCodeConfigGenerator
{
    /// <summary>
    /// Generates an opencode.json file in the specified worktree directory.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree directory</param>
    /// <param name="config">Configuration to write</param>
    /// <param name="ct">Cancellation token</param>
    Task GenerateConfigAsync(string worktreePath, OpenCodeConfig config, CancellationToken ct = default);

    /// <summary>
    /// Creates a default configuration with allow permissions.
    /// </summary>
    /// <param name="model">Optional model override (e.g., "anthropic/claude-sonnet-4-5")</param>
    /// <returns>A configured OpenCodeConfig object</returns>
    OpenCodeConfig CreateDefaultConfig(string? model = null);
}
