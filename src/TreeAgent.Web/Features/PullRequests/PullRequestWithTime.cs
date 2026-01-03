namespace TreeAgent.Web.Features.PullRequests;

/// <summary>
/// Result of a PR with its calculated time value.
/// </summary>
public record PullRequestWithTime(PullRequestInfo PullRequest, int Time);