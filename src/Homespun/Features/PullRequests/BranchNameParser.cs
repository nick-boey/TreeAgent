using System.Text.RegularExpressions;

namespace Homespun.Features.PullRequests;

/// <summary>
/// Utility class for parsing branch names to extract beads issue IDs
/// and managing PR labels.
/// </summary>
public static partial class BranchNameParser
{
    /// <summary>
    /// Label prefix used to link beads issues to GitHub PRs.
    /// Format: hsp:pr-{prNumber}
    /// </summary>
    public const string PrLabelPrefix = "hsp:pr-";

    /// <summary>
    /// Extracts the beads issue ID from a branch name.
    /// Branch format: {group}/{type}/{branch-id}+{beads-id}
    /// Example: "issues/feature/link-issues+hsp-kca" -> "hsp-kca"
    /// </summary>
    /// <param name="branchName">The branch name to parse.</param>
    /// <returns>The extracted issue ID, or null if no issue ID is found.</returns>
    public static string? ExtractIssueId(string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return null;

        var plusIndex = branchName.LastIndexOf('+');
        if (plusIndex < 0 || plusIndex >= branchName.Length - 1)
            return null;

        var issueId = branchName[(plusIndex + 1)..];
        return string.IsNullOrEmpty(issueId) ? null : issueId;
    }

    /// <summary>
    /// Generates the PR label for a given PR number.
    /// </summary>
    /// <param name="prNumber">The GitHub PR number.</param>
    /// <returns>The label in format "hsp:pr-{prNumber}".</returns>
    public static string GetPrLabel(int prNumber) => $"{PrLabelPrefix}{prNumber}";

    /// <summary>
    /// Attempts to parse a PR number from a PR label.
    /// </summary>
    /// <param name="label">The label to parse.</param>
    /// <param name="prNumber">The parsed PR number, or 0 if parsing failed.</param>
    /// <returns>True if the label was a valid PR label and was parsed successfully.</returns>
    public static bool TryParsePrNumber(string? label, out int prNumber)
    {
        prNumber = 0;

        if (string.IsNullOrEmpty(label) || !label.StartsWith(PrLabelPrefix))
            return false;

        var numberPart = label[PrLabelPrefix.Length..];
        return int.TryParse(numberPart, out prNumber) && numberPart == prNumber.ToString();
    }

    /// <summary>
    /// Checks if a label is a PR label (starts with "hsp:pr-").
    /// </summary>
    /// <param name="label">The label to check.</param>
    /// <returns>True if the label is a PR label.</returns>
    public static bool IsPrLabel(string? label)
    {
        return !string.IsNullOrEmpty(label) && label.StartsWith(PrLabelPrefix);
    }

    /// <summary>
    /// Checks if a list of labels contains a PR label.
    /// </summary>
    /// <param name="labels">The list of labels to check.</param>
    /// <returns>True if any label is a PR label.</returns>
    public static bool HasPrLabel(IEnumerable<string>? labels)
    {
        return labels?.Any(IsPrLabel) ?? false;
    }
}
