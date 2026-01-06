using System.Text.RegularExpressions;

namespace Homespun.Features.Beads;

/// <summary>
/// Helper methods for working with beads branch names.
/// Branch format: {group}/{type}/{sanitized-title}+{beads-id}
/// Example: core/feature/add-oauth-support+bd-a3f8
/// </summary>
public static partial class BeadsBranchHelper
{
    /// <summary>
    /// Generates a branch name from the given components.
    /// </summary>
    /// <param name="group">The group (e.g., "core", "frontend", "api").</param>
    /// <param name="type">The issue type (e.g., "feature", "bug", "task").</param>
    /// <param name="title">The human-readable title to sanitize.</param>
    /// <param name="beadsId">The beads issue ID (e.g., "bd-a3f8").</param>
    /// <returns>A branch name in the format {group}/{type}/{sanitized-title}+{beads-id}.</returns>
    public static string GenerateBranchName(string group, string type, string title, string beadsId)
    {
        var sanitizedTitle = SanitizeForBranch(title);
        return $"{group.ToLowerInvariant()}/{type.ToLowerInvariant()}/{sanitizedTitle}+{beadsId}";
    }
    
    /// <summary>
    /// Extracts the beads issue ID from a branch name.
    /// </summary>
    /// <param name="branchName">The full branch name.</param>
    /// <returns>The beads issue ID, or null if not found.</returns>
    public static string? ExtractBeadsIdFromBranch(string branchName)
    {
        var lastSlash = branchName.LastIndexOf('/');
        var lastPart = lastSlash >= 0 ? branchName[(lastSlash + 1)..] : branchName;
        var plusIndex = lastPart.LastIndexOf('+');
        return plusIndex >= 0 ? lastPart[(plusIndex + 1)..] : null;
    }
    
    /// <summary>
    /// Extracts the group from a branch name.
    /// </summary>
    /// <param name="branchName">The full branch name.</param>
    /// <returns>The group, or null if the branch name doesn't match the expected format.</returns>
    public static string? ExtractGroupFromBranch(string branchName)
    {
        var parts = branchName.Split('/');
        return parts.Length >= 3 ? parts[0] : null;
    }
    
    /// <summary>
    /// Extracts the type from a branch name.
    /// </summary>
    /// <param name="branchName">The full branch name.</param>
    /// <returns>The type, or null if the branch name doesn't match the expected format.</returns>
    public static string? ExtractTypeFromBranch(string branchName)
    {
        var parts = branchName.Split('/');
        return parts.Length >= 3 ? parts[1] : null;
    }
    
    /// <summary>
    /// Validates that a branch name follows the expected format.
    /// </summary>
    /// <param name="branchName">The branch name to validate.</param>
    /// <returns>True if the branch name is valid.</returns>
    public static bool IsValidBranchName(string branchName)
    {
        return BranchNameRegex().IsMatch(branchName);
    }
    
    /// <summary>
    /// Sanitizes a title for use in a branch name.
    /// Converts to lowercase, replaces spaces and special characters with hyphens,
    /// removes consecutive hyphens, and trims leading/trailing hyphens.
    /// </summary>
    /// <param name="title">The title to sanitize.</param>
    /// <returns>A sanitized string suitable for use in a branch name.</returns>
    public static string SanitizeForBranch(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "untitled";
        
        // Convert to lowercase
        var result = title.ToLowerInvariant();
        
        // Replace spaces and special characters with hyphens
        result = NonAlphanumericRegex().Replace(result, "-");
        
        // Remove consecutive hyphens
        result = ConsecutiveHyphensRegex().Replace(result, "-");
        
        // Trim leading and trailing hyphens
        result = result.Trim('-');
        
        // Ensure we have something left
        if (string.IsNullOrEmpty(result))
            return "untitled";
        
        // Limit length to keep branch names reasonable
        if (result.Length > 50)
            result = result[..50].TrimEnd('-');
        
        return result;
    }
    
    [GeneratedRegex(@"^[a-z0-9-]+/[a-z]+/[a-z0-9-]+\+bd-[a-z0-9]+(\.\d+)*$")]
    private static partial Regex BranchNameRegex();
    
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
    
    [GeneratedRegex(@"-+")]
    private static partial Regex ConsecutiveHyphensRegex();
}
