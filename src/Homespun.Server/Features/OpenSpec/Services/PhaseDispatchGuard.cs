using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Reads <c>openspec/changes/{changeName}/tasks.md</c> from the clone and checks
/// that all phases before the requested phase are fully complete.
/// </summary>
public class PhaseDispatchGuard(ILogger<PhaseDispatchGuard> logger) : IPhaseDispatchGuard
{
    private const string ChangesRelativePath = "openspec/changes";
    private const string TasksFileName = "tasks.md";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetBlockingPhasesAsync(
        string clonePath,
        string changeName,
        string phaseName,
        CancellationToken ct = default)
    {
        var tasksPath = Path.Combine(clonePath, ChangesRelativePath, changeName, TasksFileName);

        if (!File.Exists(tasksPath))
        {
            logger.LogDebug(
                "tasks.md not found at {Path}; skipping phase pre-flight check", tasksPath);
            return Array.Empty<string>();
        }

        var content = await File.ReadAllTextAsync(tasksPath, ct);
        var summary = TasksParser.Parse(content);

        if (summary.Phases.Count == 0)
            return Array.Empty<string>();

        var phaseIndex = summary.Phases.FindIndex(
            p => string.Equals(p.Name, phaseName, StringComparison.OrdinalIgnoreCase));

        // First phase or phase not found — nothing prior can block
        if (phaseIndex <= 0)
            return Array.Empty<string>();

        var blocking = summary.Phases
            .Take(phaseIndex)
            .Where(p => p.Done < p.Total)
            .Select(p => p.Name)
            .ToList();

        if (blocking.Count > 0)
        {
            logger.LogInformation(
                "Phase dispatch of '{Phase}' in change '{Change}' blocked by incomplete phases: {BlockingPhases}",
                phaseName, changeName, string.Join(", ", blocking));
        }

        return blocking;
    }
}
