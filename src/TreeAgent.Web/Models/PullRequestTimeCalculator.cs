namespace TreeAgent.Web.Models;

/// <summary>
/// Calculates the time dimension value (t) for pull requests.
///
/// Time values:
/// - t &lt; 0: Past (merged/closed) PRs. Most recent merge has t = 0, older PRs have negative values.
/// - t = 0: Most recently merged PR (head of main)
/// - t = 1: All currently open PRs
/// - t &gt; 1: Future planned changes (calculated from tree depth in ROADMAP.json)
/// </summary>
public static class PullRequestTimeCalculator
{
    /// <summary>
    /// Calculates time values for all merged PRs based on merge order.
    /// </summary>
    /// <param name="mergedPRs">List of merged PRs (can be unordered)</param>
    /// <returns>Dictionary mapping PR number to time value</returns>
    public static Dictionary<int, int> CalculateTimesForMergedPRs(IEnumerable<PullRequestInfo> mergedPRs)
    {
        var result = new Dictionary<int, int>();

        // Order by merge time descending (most recent first)
        var orderedPRs = mergedPRs
            .Where(pr => pr.MergedAt.HasValue)
            .OrderByDescending(pr => pr.MergedAt!.Value)
            .ToList();

        for (int i = 0; i < orderedPRs.Count; i++)
        {
            // Most recent = 0, second most recent = -1, etc.
            result[orderedPRs[i].Number] = -i;
        }

        return result;
    }

    /// <summary>
    /// All open PRs have time value = 1.
    /// </summary>
    public static int CalculateTimeForOpenPR(PullRequestInfo pr)
    {
        if (!PullRequestStatusExtensions.IsOpen(pr.Status))
        {
            throw new ArgumentException("PR is not open", nameof(pr));
        }

        return 1;
    }

    /// <summary>
    /// Calculates time value for a closed (not merged) PR based on when it was closed
    /// relative to merged PRs.
    /// </summary>
    /// <param name="closedPR">The closed PR</param>
    /// <param name="mergedPRs">All merged PRs to use as reference points</param>
    /// <returns>The calculated time value (always negative)</returns>
    public static int CalculateTimeForClosedPR(PullRequestInfo closedPR, IEnumerable<PullRequestInfo> mergedPRs)
    {
        if (closedPR.Status != PullRequestStatus.Closed)
        {
            throw new ArgumentException("PR is not closed", nameof(closedPR));
        }

        if (closedPR.ClosedAt == null)
        {
            // If no close time, place it at the end
            return int.MinValue + 1;
        }

        var mergedTimes = CalculateTimesForMergedPRs(mergedPRs);
        var orderedMergedPRs = mergedPRs
            .Where(pr => pr.MergedAt.HasValue)
            .OrderByDescending(pr => pr.MergedAt!.Value)
            .ToList();

        // Find the position where the closed PR would fall
        // based on its close time relative to merge times
        for (int i = 0; i < orderedMergedPRs.Count; i++)
        {
            if (closedPR.ClosedAt >= orderedMergedPRs[i].MergedAt)
            {
                // Closed PR was closed after this merge, so it goes just before this position
                return -i - 1;
            }
        }

        // Closed before all merges - put at the end
        return -(orderedMergedPRs.Count + 1);
    }

    /// <summary>
    /// Calculates time values for future changes based on tree depth.
    /// Root-level changes have t=2, their children have t=3, etc.
    /// </summary>
    /// <param name="depth">Depth in the roadmap tree (0 = root level)</param>
    /// <returns>The time value (always >= 2)</returns>
    public static int CalculateTimeForFutureChange(int depth)
    {
        return depth + 2;
    }

    /// <summary>
    /// Calculates the time value for a single PR based on its status.
    /// For merged PRs, you should use CalculateTimesForMergedPRs instead to get accurate ordering.
    /// </summary>
    public static int? CalculateTime(PullRequestInfo pr, IEnumerable<PullRequestInfo>? allMergedPRs = null)
    {
        if (PullRequestStatusExtensions.IsOpen(pr.Status))
        {
            return 1;
        }

        if (pr.Status == PullRequestStatus.Merged)
        {
            if (allMergedPRs != null)
            {
                var times = CalculateTimesForMergedPRs(allMergedPRs);
                return times.TryGetValue(pr.Number, out var time) ? time : null;
            }
            return null; // Cannot calculate without merge order context
        }

        if (pr.Status == PullRequestStatus.Closed)
        {
            if (allMergedPRs != null)
            {
                return CalculateTimeForClosedPR(pr, allMergedPRs);
            }
            return null; // Cannot calculate without merge order context
        }

        return null;
    }
}
