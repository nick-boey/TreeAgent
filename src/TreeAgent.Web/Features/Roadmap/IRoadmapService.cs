using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Roadmap;

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
    Task<Feature?> PromoteChangeAsync(string projectId, string changeId);
    Task<bool> IsPlanUpdateOnlyAsync(string featureId);
    Task<bool> ValidateRoadmapAsync(string featureId);
    Task<Feature?> CreatePlanUpdateFeatureAsync(string projectId, string description);
    string GeneratePlanUpdateBranchName(string description);
}
