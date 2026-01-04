namespace Homespun.Features.PullRequests;

/// <summary>
/// Result of a PR with its calculated status and time value.
/// </summary>
public record PullRequestWithStatus(PullRequestInfo PullRequest, PullRequestStatus Status, int Time);