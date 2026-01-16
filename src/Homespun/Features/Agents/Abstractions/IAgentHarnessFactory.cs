namespace Homespun.Features.Agents.Abstractions;

/// <summary>
/// Factory for creating and accessing agent harness instances.
/// Supports multiple harness types with different configurations.
/// </summary>
public interface IAgentHarnessFactory
{
    /// <summary>
    /// Gets the available harness types.
    /// </summary>
    IReadOnlyList<string> AvailableHarnessTypes { get; }

    /// <summary>
    /// Gets the default harness type.
    /// </summary>
    string DefaultHarnessType { get; }

    /// <summary>
    /// Gets a harness instance by type.
    /// </summary>
    /// <param name="harnessType">The harness type identifier.</param>
    /// <returns>The harness instance.</returns>
    /// <exception cref="ArgumentException">Thrown if the harness type is not available.</exception>
    IAgentHarness GetHarness(string harnessType);

    /// <summary>
    /// Gets the default harness instance.
    /// </summary>
    /// <returns>The default harness instance.</returns>
    IAgentHarness GetDefaultHarness();

    /// <summary>
    /// Checks if a harness type is available.
    /// </summary>
    /// <param name="harnessType">The harness type identifier.</param>
    /// <returns>True if available, false otherwise.</returns>
    bool IsHarnessAvailable(string harnessType);
}
