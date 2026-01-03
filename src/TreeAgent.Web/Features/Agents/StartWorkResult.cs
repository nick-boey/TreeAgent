using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Agents;

/// <summary>
/// Result of starting work on a future change.
/// </summary>
public record StartWorkResult(Feature? Feature, Agent? Agent);