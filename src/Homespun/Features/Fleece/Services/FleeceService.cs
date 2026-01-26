using System.Collections.Concurrent;
using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware implementation of IFleeceService.
/// Caches IIssueService instances per project path for efficient access.
/// </summary>
public sealed class FleeceService : IFleeceService, IDisposable
{
    private readonly ConcurrentDictionary<string, IIssueService> _issueServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly IJsonlSerializer _serializer;
    private readonly IIdGenerator _idGenerator;
    private readonly IGitConfigService _gitConfigService;
    private readonly ILogger<FleeceService> _logger;
    private bool _disposed;

    public FleeceService(ILogger<FleeceService> logger)
    {
        _logger = logger;
        _serializer = new JsonlSerializer();
        _idGenerator = new Sha256IdGenerator();
        _gitConfigService = new GitConfigService();
    }

    private IIssueService GetOrCreateIssueService(string projectPath)
    {
        return _issueServices.GetOrAdd(projectPath, path =>
        {
            _logger.LogDebug("Creating new IIssueService for project: {ProjectPath}", path);

            var storageService = new JsonlStorageService(path, _serializer);
            var changeService = new ChangeService(storageService);
            return new IssueService(storageService, _idGenerator, _gitConfigService, changeService);
        });
    }

    #region Read Operations

    public async Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);
        return await service.GetByIdAsync(issueId, ct);
    }

    public async Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);

        // Use filter if any filters specified, otherwise get all and filter deleted
        if (status.HasValue || type.HasValue || priority.HasValue)
        {
            return await service.FilterAsync(status, type, priority, cancellationToken: ct);
        }

        // Get all issues and exclude deleted/archived/closed/complete
        var issues = await service.GetAllAsync(ct);
        return issues.Where(i => i.Status is not (IssueStatus.Deleted or IssueStatus.Archived or IssueStatus.Closed or IssueStatus.Complete)).ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);

        // Get all open issues
        // Get all issues in open statuses (Idea, Spec, Next, Progress, Review)
        var allIssues = await service.GetAllAsync(ct);
        var openIssues = allIssues.Where(i => i.Status is IssueStatus.Idea or IssueStatus.Spec or IssueStatus.Next or IssueStatus.Progress or IssueStatus.Review).ToList();

        // Filter to issues that have no blocking parent issues (parents that are not Complete/Closed)
        var issueMap = allIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        return openIssues
            .Where(issue =>
            {
                // If no parent issues, it's ready
                if (issue.ParentIssues.Count == 0)
                {
                    return true;
                }

                // Check all parent issues - if all are Complete or Closed, this issue is ready
                return issue.ParentIssues.All(parentId =>
                {
                    if (issueMap.TryGetValue(parentId, out var parent))
                    {
                        return parent.Status is IssueStatus.Complete or IssueStatus.Closed;
                    }
                    // If parent doesn't exist, assume it's done
                    return true;
                });
            })
            .ToList();
    }

    #endregion

    #region Write Operations

    public async Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        string? group = null,
        CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);

        var issue = await service.CreateAsync(
            title: title,
            type: type,
            description: description,
            priority: priority,
            group: group,
            cancellationToken: ct);

        _logger.LogInformation(
            "Created issue '{IssueId}' ({Type}): {Title}{Group}",
            issue.Id,
            type,
            title,
            group != null ? $" [Group: {group}]" : "");

        return issue;
    }

    public async Task<Issue?> UpdateIssueAsync(
        string projectPath,
        string issueId,
        string? title = null,
        IssueStatus? status = null,
        IssueType? type = null,
        string? description = null,
        int? priority = null,
        string? group = null,
        string? workingBranchId = null,
        CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);

        try
        {
            var issue = await service.UpdateAsync(
                id: issueId,
                title: title,
                status: status,
                type: type,
                description: description,
                priority: priority,
                group: group,
                workingBranchId: workingBranchId,
                cancellationToken: ct);

            var changes = new List<string>();
            if (title != null) changes.Add($"title='{title}'");
            if (status != null) changes.Add($"status={status}");
            if (type != null) changes.Add($"type={type}");
            if (description != null) changes.Add("description updated");
            if (priority != null) changes.Add($"priority={priority}");
            if (group != null) changes.Add($"group='{group}'");
            if (workingBranchId != null) changes.Add($"workingBranchId='{workingBranchId}'");

            _logger.LogInformation(
                "Updated issue '{IssueId}': {Changes}",
                issueId,
                string.Join(", ", changes));

            return issue;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            return null;
        }
    }

    public async Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var service = GetOrCreateIssueService(projectPath);
        var deleted = await service.DeleteAsync(issueId, ct);

        if (deleted)
        {
            _logger.LogInformation("Deleted issue '{IssueId}'", issueId);
        }
        else
        {
            _logger.LogWarning("Failed to delete issue '{IssueId}' - not found", issueId);
        }

        return deleted;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clear the cache
        _issueServices.Clear();
    }
}
