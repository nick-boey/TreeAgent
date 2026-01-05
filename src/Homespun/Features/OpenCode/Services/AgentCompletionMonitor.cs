using System.Text.RegularExpressions;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.Roadmap;
using Microsoft.Extensions.Options;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Monitors agent SSE events to detect PR creation and handle completion workflow.
/// </summary>
public partial class AgentCompletionMonitor : IAgentCompletionMonitor
{
    private readonly IOpenCodeClient _client;
    private readonly IGitHubService _gitHubService;
    private readonly IRoadmapService _roadmapService;
    private readonly AgentCompletionOptions _options;
    private readonly ILogger<AgentCompletionMonitor> _logger;

    public AgentCompletionMonitor(
        IOpenCodeClient client,
        IGitHubService gitHubService,
        IRoadmapService roadmapService,
        IOptions<AgentCompletionOptions> options,
        ILogger<AgentCompletionMonitor> logger)
    {
        _client = client;
        _gitHubService = gitHubService;
        _roadmapService = roadmapService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Checks if an SSE event represents a PR creation command.
    /// </summary>
    public static bool IsPrCreationEvent(OpenCodeEvent evt)
    {
        if (evt.Type != OpenCodeEventTypes.ToolComplete)
            return false;

        if (evt.Properties == null)
            return false;

        var toolName = evt.Properties.ToolName;
        var content = evt.Properties.Content;

        if (string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(content))
            return false;

        // Check if it's a bash tool that executed gh pr create
        return toolName.Equals("bash", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("gh pr create", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses the PR URL from gh pr create output.
    /// </summary>
    public static PrUrlParseResult? ParsePrUrl(string content)
    {
        // GitHub PR URLs look like: https://github.com/owner/repo/pull/123
        var match = GitHubPrUrlRegex().Match(content);
        if (!match.Success)
            return null;

        var url = match.Value;
        var prNumberStr = match.Groups["number"].Value;
        
        if (!int.TryParse(prNumberStr, out var prNumber))
            return null;

        return new PrUrlParseResult
        {
            Url = url,
            PrNumber = prNumber
        };
    }

    /// <summary>
    /// Monitors SSE events and waits for PR creation completion.
    /// Runs until the SSE stream ends (server stops) or cancellation is requested.
    /// </summary>
    public async Task<AgentCompletionResult> MonitorForPrCreationAsync(
        string baseUrl,
        string projectId,
        string branchName,
        CancellationToken ct = default)
    {
        try
        {
            // Monitor SSE events until server stops (no timeout)
            await foreach (var evt in _client.SubscribeToEventsAsync(baseUrl, ct))
            {
                if (!IsPrCreationEvent(evt))
                    continue;

                _logger.LogInformation("Detected PR creation event for branch {Branch}", branchName);

                // Try to parse PR URL from the event content
                var parseResult = ParsePrUrl(evt.Properties?.Content ?? "");
                if (parseResult != null)
                {
                    _logger.LogInformation("PR URL found: {Url} (PR #{Number})", parseResult.Url, parseResult.PrNumber);
                    return new AgentCompletionResult
                    {
                        Success = true,
                        PrNumber = parseResult.PrNumber,
                        PrUrl = parseResult.Url,
                        BranchName = branchName
                    };
                }

                // PR URL not in output, try to find it via GitHub API
                return await TryFindPrByBranchAsync(projectId, branchName, ct);
            }

            // Stream ended without PR creation (server stopped)
            return new AgentCompletionResult
            {
                Success = false,
                Error = "Agent server stopped without creating a PR",
                BranchName = branchName
            };
        }
        catch (OperationCanceledException)
        {
            // Monitoring was cancelled - this is expected when StopAgentAsync is called
            return new AgentCompletionResult
            {
                Success = false,
                Error = "Monitoring cancelled",
                BranchName = branchName
            };
        }
    }

    /// <summary>
    /// Attempts to find a PR by branch name with retries.
    /// </summary>
    private async Task<AgentCompletionResult> TryFindPrByBranchAsync(
        string projectId,
        string branchName,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < _options.PrDetectionRetryCount; attempt++)
        {
            if (attempt > 0)
            {
                _logger.LogDebug("Retrying PR detection (attempt {Attempt}/{Total})", 
                    attempt + 1, _options.PrDetectionRetryCount);
                await Task.Delay(_options.PrDetectionRetryDelayMs, ct);
            }

            var prs = await _gitHubService.GetOpenPullRequestsAsync(projectId);
            var matchingPr = prs.FirstOrDefault(pr => 
                pr.BranchName?.Equals(branchName, StringComparison.OrdinalIgnoreCase) == true);

            if (matchingPr != null)
            {
                _logger.LogInformation("Found PR #{Number} for branch {Branch}", 
                    matchingPr.Number, branchName);
                return new AgentCompletionResult
                {
                    Success = true,
                    PrNumber = matchingPr.Number,
                    PrUrl = matchingPr.HtmlUrl,
                    BranchName = branchName
                };
            }
        }

        return new AgentCompletionResult
        {
            Success = false,
            Error = $"PR not found after {_options.PrDetectionRetryCount} retries",
            BranchName = branchName
        };
    }

    [GeneratedRegex(@"https://github\.com/[^/]+/[^/]+/pull/(?<number>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPrUrlRegex();
}

/// <summary>
/// Result of monitoring for agent completion.
/// </summary>
public class AgentCompletionResult
{
    public bool Success { get; init; }
    public int? PrNumber { get; init; }
    public string? PrUrl { get; init; }
    public string? BranchName { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Result of parsing a PR URL.
/// </summary>
public class PrUrlParseResult
{
    public required string Url { get; init; }
    public required int PrNumber { get; init; }
}

/// <summary>
/// Interface for the agent completion monitor.
/// </summary>
public interface IAgentCompletionMonitor
{
    /// <summary>
    /// Monitors SSE events and waits for PR creation completion.
    /// </summary>
    Task<AgentCompletionResult> MonitorForPrCreationAsync(
        string baseUrl,
        string projectId,
        string branchName,
        CancellationToken ct = default);
}
