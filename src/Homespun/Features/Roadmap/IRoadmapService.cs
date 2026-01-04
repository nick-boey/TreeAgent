using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Interface for ROADMAP.json operations.
/// </summary>
public interface IRoadmapService
{
    Task<string?> GetRoadmapPathAsync(string projectId);
    Task<Roadmap?> LoadRoadmapAsync(string projectId);
    Task<List<FutureChangeWithTime>> GetFutureChangesAsync(string projectId);
    Task<Dictionary<string, List<FutureChangeWithTime>>> GetFutureChangesByGroupAsync(string projectId);
    Task<RoadmapChange?> FindChangeByIdAsync(string projectId, string changeId);
    Task<PullRequest?> PromoteChangeAsync(string projectId, string changeId);
    Task<bool> IsPlanUpdateOnlyAsync(string pullRequestId);
    Task<bool> ValidateRoadmapAsync(string pullRequestId);
    Task<PullRequest?> CreatePlanUpdatePullRequestAsync(string projectId, string description);
    string GeneratePlanUpdateBranchName(string description);
}
