using System.Text.RegularExpressions;

namespace Homespun.Features.Beads;

/// <summary>
/// Helper for parsing and creating Homespun branch labels.
/// Label format: hsp:{group}/-/{branch-id}
/// Example: hsp:frontend/-/update-page
/// The /-/ placeholder is replaced with the issue type when generating the full branch name.
/// Full branch example: frontend/feature/update-page+bd-hj4
/// </summary>
public static partial class BeadsBranchLabel
{
    /// <summary>
    /// The prefix for Homespun branch labels.
    /// </summary>
    public const string LabelPrefix = "hsp:";
    
    /// <summary>
    /// The placeholder for the issue type in the label.
    /// </summary>
    public const string TypePlaceholder = "/-/";
    
    /// <summary>
    /// Parses the first Homespun label from a collection of labels.
    /// </summary>
    /// <param name="labels">The labels to search.</param>
    /// <returns>A tuple of (group, branchId) if found, null otherwise.</returns>
    public static (string Group, string BranchId)? Parse(IEnumerable<string> labels)
    {
        var hspLabel = labels.FirstOrDefault(IsHomespunLabel);
        if (hspLabel == null)
            return null;
        
        return ParseLabel(hspLabel);
    }
    
    /// <summary>
    /// Parses a single Homespun label.
    /// </summary>
    /// <param name="label">The label to parse (e.g., "hsp:frontend/-/update-page").</param>
    /// <returns>A tuple of (group, branchId) if valid, null otherwise.</returns>
    public static (string Group, string BranchId)? ParseLabel(string label)
    {
        if (!IsHomespunLabel(label))
            return null;
        
        // Remove prefix: "frontend/-/update-page"
        var content = label[LabelPrefix.Length..];
        
        // Split by type placeholder
        var placeholderIndex = content.IndexOf(TypePlaceholder, StringComparison.Ordinal);
        if (placeholderIndex < 0)
            return null;
        
        var group = content[..placeholderIndex];
        var branchId = content[(placeholderIndex + TypePlaceholder.Length)..];
        
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(branchId))
            return null;
        
        return (group, branchId);
    }
    
    /// <summary>
    /// Creates a Homespun branch label from group and branch ID.
    /// </summary>
    /// <param name="group">The group (e.g., "frontend", "core", "api").</param>
    /// <param name="branchId">The branch identifier (e.g., "update-page").</param>
    /// <returns>The label string (e.g., "hsp:frontend/-/update-page").</returns>
    public static string Create(string group, string branchId)
    {
        return $"{LabelPrefix}{group.ToLowerInvariant()}{TypePlaceholder}{branchId.ToLowerInvariant()}";
    }
    
    /// <summary>
    /// Checks if a label is a Homespun branch label.
    /// </summary>
    /// <param name="label">The label to check.</param>
    /// <returns>True if the label starts with the Homespun prefix.</returns>
    public static bool IsHomespunLabel(string label)
    {
        return label.StartsWith(LabelPrefix, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Validates that a branch ID is in the correct format.
    /// Must be lowercase alphanumeric with hyphens, no leading/trailing hyphens,
    /// no consecutive hyphens.
    /// </summary>
    /// <param name="branchId">The branch ID to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidBranchId(string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
            return false;
        
        return BranchIdRegex().IsMatch(branchId);
    }
    
    /// <summary>
    /// Validates that a group name is in the correct format.
    /// Must be lowercase alphanumeric with hyphens, no leading/trailing hyphens.
    /// </summary>
    /// <param name="group">The group name to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return false;
        
        return GroupRegex().IsMatch(group);
    }
    
    /// <summary>
    /// Extracts unique groups from a collection of labels.
    /// </summary>
    /// <param name="allLabels">All labels from multiple issues.</param>
    /// <returns>Sorted list of unique group names.</returns>
    public static List<string> ExtractUniqueGroups(IEnumerable<IEnumerable<string>> allLabels)
    {
        return allLabels
            .SelectMany(labels => labels)
            .Where(IsHomespunLabel)
            .Select(label => ParseLabel(label)?.Group)
            .Where(group => group != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }
    
    // Regex: lowercase alphanumeric, hyphens allowed but not at start/end, no consecutive hyphens
    // Examples: "update-page", "user-dashboard", "api", "fix-bug-123"
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex BranchIdRegex();
    
    // Same pattern for group names
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex GroupRegex();
}
