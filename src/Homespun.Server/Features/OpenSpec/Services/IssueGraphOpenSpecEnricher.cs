using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Wires the branch-state resolver into the issue graph response and projects scan
/// results onto the five-state graph indicator model.
/// </summary>
public class IssueGraphOpenSpecEnricher(
    IIssueBranchResolverService branchResolver,
    IBranchStateResolverService stateResolver,
    IDataStore dataStore,
    IGitCloneService cloneService,
    IChangeScannerService scanner,
    ILogger<IssueGraphOpenSpecEnricher> logger) : IIssueGraphOpenSpecEnricher
{
    /// <inheritdoc />
    public async Task EnrichAsync(
        string projectId,
        TaskGraphResponse response,
        BranchResolutionContext? branchContext = null,
        CancellationToken ct = default)
    {
        using var enrichActivity = OpenSpecActivitySource.Instance.StartActivity("openspec.enrich");
        enrichActivity?.SetTag("project.id", projectId);
        enrichActivity?.SetTag("graph.node.count", response.Nodes.Count);

        var context = branchContext ?? await BuildFallbackContextAsync(projectId);

        foreach (var node in response.Nodes)
        {
            var issueId = node.Issue?.Id;
            if (string.IsNullOrWhiteSpace(issueId))
            {
                continue;
            }

            using var nodeActivity = OpenSpecActivitySource.Instance.StartActivity("openspec.enrich.node");
            nodeActivity?.SetTag("issue.id", issueId);

            try
            {
                var state = await ResolveForIssueAsync(projectId, issueId, context, ct);
                if (state is not null)
                {
                    response.OpenSpecStates[issueId] = state;
                    nodeActivity?.SetTag(
                        "branch.source",
                        state.BranchState == BranchPresence.None ? "none" : "resolved");
                }
                else
                {
                    nodeActivity?.SetTag("branch.source", "none");
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to enrich OpenSpec state for issue {IssueId}", issueId);
            }
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, IssueOpenSpecState>> GetOpenSpecStatesAsync(
        string projectId,
        IReadOnlyCollection<string> issueIds,
        BranchResolutionContext? branchContext = null,
        CancellationToken ct = default)
    {
        using var enrichActivity = OpenSpecActivitySource.Instance.StartActivity("openspec.states");
        enrichActivity?.SetTag("project.id", projectId);
        enrichActivity?.SetTag("issue.count", issueIds.Count);

        var context = branchContext ?? await BuildFallbackContextAsync(projectId);
        var result = new Dictionary<string, IssueOpenSpecState>(issueIds.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var issueId in issueIds)
        {
            if (string.IsNullOrWhiteSpace(issueId))
            {
                continue;
            }

            try
            {
                var state = await ResolveForIssueAsync(projectId, issueId, context, ct);
                if (state is not null)
                {
                    result[issueId] = state;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to resolve OpenSpec state for issue {IssueId}", issueId);
            }
        }

        return result;
    }

    private async Task<BranchResolutionContext> BuildFallbackContextAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project is null || string.IsNullOrEmpty(project.LocalPath))
        {
            return new BranchResolutionContext(
                Array.Empty<Homespun.Shared.Models.Git.CloneInfo>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var prBranches = dataStore.GetPullRequestsByProject(projectId)
            .Where(pr => !string.IsNullOrEmpty(pr.FleeceIssueId) && !string.IsNullOrEmpty(pr.BranchName))
            .GroupBy(pr => pr.FleeceIssueId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().BranchName!, StringComparer.Ordinal);

        var clones = await cloneService.ListClonesAsync(project.LocalPath);
        return new BranchResolutionContext(clones, prBranches);
    }

    internal async Task<IssueOpenSpecState?> ResolveForIssueAsync(
        string projectId,
        string issueId,
        BranchResolutionContext context,
        CancellationToken ct)
    {
        var branch = await branchResolver.ResolveIssueBranchAsync(projectId, issueId, context);
        if (string.IsNullOrEmpty(branch))
        {
            return new IssueOpenSpecState
            {
                BranchState = BranchPresence.None,
                ChangeState = ChangePhase.None
            };
        }

        var snapshot = await stateResolver.GetOrScanAsync(projectId, branch, ct);
        if (snapshot is null || snapshot.Changes.Count == 0)
        {
            return new IssueOpenSpecState
            {
                BranchState = BranchPresence.Exists,
                ChangeState = ChangePhase.None
            };
        }

        // Pick the most advanced linked change (archived > ready-to-archive > ready-to-apply > incomplete).
        var primary = snapshot.Changes
            .OrderByDescending(MapPhaseRank)
            .First();

        var phases = primary.Phases
            .Select(p => new PhaseSummary
            {
                Name = p.Name,
                Done = p.Done,
                Total = p.Total,
                Tasks = p.Tasks
                    .Select(t => new PhaseTaskSummary { Description = t.Description, Done = t.Done })
                    .ToList()
            })
            .ToList();

        return new IssueOpenSpecState
        {
            BranchState = BranchPresence.WithChange,
            ChangeState = MapPhase(primary),
            ChangeName = primary.Name,
            SchemaName = primary.ArtifactState?.SchemaName,
            Phases = phases
        };
    }

    internal static ChangePhase MapPhase(SnapshotChange change)
    {
        if (change.IsArchived) return ChangePhase.Archived;
        if (change.TasksTotal > 0 && change.TasksDone >= change.TasksTotal) return ChangePhase.ReadyToArchive;
        if (change.ArtifactState?.IsComplete == true) return ChangePhase.ReadyToApply;
        return ChangePhase.Incomplete;
    }

    private static int MapPhaseRank(SnapshotChange change) => MapPhase(change) switch
    {
        ChangePhase.Archived => 4,
        ChangePhase.ReadyToArchive => 3,
        ChangePhase.ReadyToApply => 2,
        ChangePhase.Incomplete => 1,
        _ => 0
    };
}
