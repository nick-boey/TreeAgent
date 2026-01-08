using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.Beads.Data;
using Homespun.Features.Commands;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Beads.Services;

/// <summary>
/// Service for interacting with the beads CLI (bd).
/// Executes bd commands and parses their JSON output.
/// </summary>
public class BeadsService : IBeadsService
{
    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<BeadsService> _logger;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    
    public BeadsService(ICommandRunner commandRunner, ILogger<BeadsService> logger)
    {
        _commandRunner = commandRunner;
        _logger = logger;
    }
    
    #region Issue CRUD
    
    public async Task<BeadsIssue?> GetIssueAsync(string workingDirectory, string issueId)
    {
        var result = await RunBdCommandAsync(workingDirectory, $"show {issueId} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to get issue {IssueId}: {Error}", issueId, result.Error);
            return null;
        }
        
        try
        {
            // bd show returns an array, so we need to deserialize as a list and take the first item
            var issues = JsonSerializer.Deserialize<List<BeadsIssue>>(result.Output, JsonOptions);
            return issues?.FirstOrDefault();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse issue JSON for {IssueId}", issueId);
            return null;
        }
    }
    
    public async Task<List<BeadsIssue>> ListIssuesAsync(string workingDirectory, BeadsListOptions? options = null)
    {
        var args = BuildListArguments(options);
        var result = await RunBdCommandAsync(workingDirectory, $"list {args} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to list issues: {Error}", result.Error);
            return [];
        }
        
        try
        {
            return JsonSerializer.Deserialize<List<BeadsIssue>>(result.Output, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse issues list JSON");
            return [];
        }
    }
    
    public async Task<List<BeadsIssue>> GetReadyIssuesAsync(string workingDirectory)
    {
        var result = await RunBdCommandAsync(workingDirectory, "ready --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to get ready issues: {Error}", result.Error);
            return [];
        }
        
        try
        {
            return JsonSerializer.Deserialize<List<BeadsIssue>>(result.Output, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ready issues JSON");
            return [];
        }
    }
    
    public async Task<BeadsIssue> CreateIssueAsync(string workingDirectory, BeadsCreateOptions options)
    {
        var args = BuildCreateArguments(options);
        var result = await RunBdCommandAsync(workingDirectory, $"create \"{EscapeQuotes(options.Title)}\" {args} --json");
        
        if (!result.Success)
        {
            _logger.LogError("Failed to create issue: {Error}", result.Error);
            throw new InvalidOperationException($"Failed to create beads issue: {result.Error}");
        }
        
        try
        {
            var issue = JsonSerializer.Deserialize<BeadsIssue>(result.Output, JsonOptions);
            if (issue == null)
            {
                throw new InvalidOperationException("Failed to parse created issue from bd output");
            }
            return issue;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse created issue JSON");
            throw new InvalidOperationException($"Failed to parse created issue: {ex.Message}", ex);
        }
    }
    
    public async Task<bool> UpdateIssueAsync(string workingDirectory, string issueId, BeadsUpdateOptions options)
    {
        var success = true;
        
        // Handle main update arguments via bd update command
        var args = BuildUpdateArguments(options);
        if (!string.IsNullOrWhiteSpace(args))
        {
            var result = await RunBdCommandAsync(workingDirectory, $"update {issueId} {args} --json");
            
            if (!result.Success)
            {
                _logger.LogWarning("Failed to update issue {IssueId}: {Error}", issueId, result.Error);
                success = false;
            }
        }
        
        // Handle label additions separately
        if (options.LabelsToAdd?.Count > 0)
        {
            foreach (var label in options.LabelsToAdd)
            {
                var labelResult = await AddLabelAsync(workingDirectory, issueId, label);
                if (!labelResult)
                {
                    success = false;
                }
            }
        }
        
        // Handle label removals separately
        if (options.LabelsToRemove?.Count > 0)
        {
            foreach (var label in options.LabelsToRemove)
            {
                var labelResult = await RemoveLabelAsync(workingDirectory, issueId, label);
                if (!labelResult)
                {
                    success = false;
                }
            }
        }
        
        return success;
    }
    
    public async Task<bool> CloseIssueAsync(string workingDirectory, string issueId, string? reason = null)
    {
        var reasonArg = !string.IsNullOrWhiteSpace(reason) 
            ? $"--reason \"{EscapeQuotes(reason)}\"" 
            : "";
        
        var result = await RunBdCommandAsync(workingDirectory, $"close {issueId} {reasonArg} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to close issue {IssueId}: {Error}", issueId, result.Error);
        }
        
        return result.Success;
    }
    
    public async Task<bool> ReopenIssueAsync(string workingDirectory, string issueId, string? reason = null)
    {
        var reasonArg = !string.IsNullOrWhiteSpace(reason) 
            ? $"--reason \"{EscapeQuotes(reason)}\"" 
            : "";
        
        var result = await RunBdCommandAsync(workingDirectory, $"reopen {issueId} {reasonArg} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to reopen issue {IssueId}: {Error}", issueId, result.Error);
        }
        
        return result.Success;
    }
    
    #endregion
    
    #region Dependencies
    
    public async Task<bool> AddDependencyAsync(string workingDirectory, string childId, string parentId, string type = "blocks")
    {
        var result = await RunBdCommandAsync(workingDirectory, $"dep add {childId} {parentId} --type {type}");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to add dependency {ChildId} -> {ParentId}: {Error}", 
                childId, parentId, result.Error);
        }
        
        return result.Success;
    }
    
    public async Task<List<BeadsDependency>> GetDependencyTreeAsync(string workingDirectory, string issueId)
    {
        var result = await RunBdCommandAsync(workingDirectory, $"dep tree {issueId}");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to get dependency tree for {IssueId}: {Error}", issueId, result.Error);
            return [];
        }
        
        // Note: bd dep tree doesn't have --json output, so we'd need to parse text
        // For now, return empty and implement parsing later if needed
        _logger.LogDebug("Dependency tree parsing not yet implemented");
        return [];
    }
    
    #endregion
    
    #region Labels
    
    public async Task<bool> AddLabelAsync(string workingDirectory, string issueId, string label)
    {
        var result = await RunBdCommandAsync(workingDirectory, $"label add {issueId} {label} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to add label {Label} to {IssueId}: {Error}", 
                label, issueId, result.Error);
        }
        
        return result.Success;
    }
    
    public async Task<bool> RemoveLabelAsync(string workingDirectory, string issueId, string label)
    {
        var result = await RunBdCommandAsync(workingDirectory, $"label remove {issueId} {label} --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to remove label {Label} from {IssueId}: {Error}", 
                label, issueId, result.Error);
        }
        
        return result.Success;
    }
    
    #endregion
    
    #region Sync and Info
    
    public async Task SyncAsync(string workingDirectory)
    {
        var result = await RunBdCommandAsync(workingDirectory, "sync");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to sync beads: {Error}", result.Error);
        }
    }
    
    public async Task<BeadsInfo> GetInfoAsync(string workingDirectory)
    {
        var result = await RunBdCommandAsync(workingDirectory, "info --json");
        
        if (!result.Success)
        {
            _logger.LogWarning("Failed to get beads info: {Error}", result.Error);
            return new BeadsInfo();
        }
        
        try
        {
            return JsonSerializer.Deserialize<BeadsInfo>(result.Output, JsonOptions) ?? new BeadsInfo();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse beads info JSON");
            return new BeadsInfo();
        }
    }
    
    public async Task<bool> IsInitializedAsync(string workingDirectory)
    {
        var info = await GetInfoAsync(workingDirectory);
        return !string.IsNullOrEmpty(info.DatabasePath);
    }
    
    public async Task<List<string>> GetUniqueGroupsAsync(string workingDirectory)
    {
        var issues = await ListIssuesAsync(workingDirectory);
        var allLabels = issues.Select(i => i.Labels);
        return BeadsBranchLabel.ExtractUniqueGroups(allLabels);
    }
    
    #endregion
    
    #region Private Helpers
    
    private async Task<CommandResult> RunBdCommandAsync(string workingDirectory, string arguments)
    {
        return await _commandRunner.RunAsync("bd", arguments, workingDirectory);
    }
    
    private static string BuildListArguments(BeadsListOptions? options)
    {
        if (options == null) return "";
        
        var args = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(options.Status))
            args.Add($"--status {options.Status}");
        
        if (options.Priority.HasValue)
            args.Add($"--priority {options.Priority}");
        
        if (options.Type.HasValue)
            args.Add($"--type {options.Type.Value.ToString().ToLowerInvariant()}");
        
        if (!string.IsNullOrWhiteSpace(options.Assignee))
            args.Add($"--assignee {options.Assignee}");
        
        if (options.Labels?.Count > 0)
            args.Add($"--label {string.Join(",", options.Labels)}");
        
        if (options.LabelAny?.Count > 0)
            args.Add($"--label-any {string.Join(",", options.LabelAny)}");
        
        if (!string.IsNullOrWhiteSpace(options.TitleContains))
            args.Add($"--title-contains \"{EscapeQuotes(options.TitleContains)}\"");
        
        return string.Join(" ", args);
    }
    
    private static string BuildCreateArguments(BeadsCreateOptions options)
    {
        var args = new List<string>
        {
            $"-t {options.Type.ToString().ToLowerInvariant()}"
        };
        
        if (options.Priority.HasValue)
            args.Add($"-p {options.Priority}");
        
        if (!string.IsNullOrWhiteSpace(options.Description))
            args.Add($"-d \"{EscapeQuotes(options.Description)}\"");
        
        if (options.Labels?.Count > 0)
            args.Add($"-l {string.Join(",", options.Labels)}");
        
        if (!string.IsNullOrWhiteSpace(options.ParentId))
            args.Add($"--parent {options.ParentId}");
        
        if (options.BlockedBy?.Count > 0)
        {
            foreach (var blocker in options.BlockedBy)
            {
                args.Add($"--deps blocks:{blocker}");
            }
        }
        
        return string.Join(" ", args);
    }
    
    private static string BuildUpdateArguments(BeadsUpdateOptions options)
    {
        var args = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(options.Title))
            args.Add($"--title \"{EscapeQuotes(options.Title)}\"");
        
        if (!string.IsNullOrWhiteSpace(options.Description))
            args.Add($"--description \"{EscapeQuotes(options.Description)}\"");
        
        if (options.Type.HasValue)
            args.Add($"--type {options.Type.Value.ToString().ToLowerInvariant()}");
        
        if (options.Status.HasValue)
            args.Add($"--status {StatusToString(options.Status.Value)}");
        
        if (options.Priority.HasValue)
            args.Add($"--priority {options.Priority}");
        
        if (!string.IsNullOrWhiteSpace(options.Assignee))
            args.Add($"--assignee {options.Assignee}");
        
        if (!string.IsNullOrWhiteSpace(options.ParentId))
            args.Add($"--parent {options.ParentId}");
        
        return string.Join(" ", args);
    }
    
    private static string StatusToString(BeadsIssueStatus status) => status switch
    {
        BeadsIssueStatus.Open => "open",
        BeadsIssueStatus.InProgress => "in_progress",
        BeadsIssueStatus.Blocked => "blocked",
        BeadsIssueStatus.Deferred => "deferred",
        BeadsIssueStatus.Closed => "closed",
        BeadsIssueStatus.Tombstone => "tombstone",
        BeadsIssueStatus.Pinned => "pinned",
        _ => "open"
    };
    
    private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");
    
    #endregion
}
