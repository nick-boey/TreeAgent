using Homespun.Features.Agents.Abstractions;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Agents.Services;

/// <summary>
/// Factory for creating and accessing agent harness instances.
/// Manages multiple harness types with different configurations.
/// </summary>
public class AgentHarnessFactory : IAgentHarnessFactory
{
    private readonly Dictionary<string, IAgentHarness> _harnesses;
    private readonly AgentOptions _options;

    public AgentHarnessFactory(
        IEnumerable<IAgentHarness> harnesses,
        IOptions<AgentOptions> options)
    {
        _options = options.Value;
        _harnesses = harnesses.ToDictionary(
            h => h.HarnessType,
            h => h,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableHarnessTypes => _harnesses.Keys.ToList();

    /// <inheritdoc />
    public string DefaultHarnessType => _options.DefaultHarnessType;

    /// <inheritdoc />
    public IAgentHarness GetHarness(string harnessType)
    {
        if (_harnesses.TryGetValue(harnessType, out var harness))
            return harness;

        throw new ArgumentException(
            $"Unknown harness type: {harnessType}. Available types: {string.Join(", ", _harnesses.Keys)}",
            nameof(harnessType));
    }

    /// <inheritdoc />
    public IAgentHarness GetDefaultHarness() => GetHarness(DefaultHarnessType);

    /// <inheritdoc />
    public bool IsHarnessAvailable(string harnessType) =>
        _harnesses.ContainsKey(harnessType);
}
