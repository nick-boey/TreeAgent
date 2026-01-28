using System.Collections.Concurrent;
using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceService with in-memory issue storage per project.
/// Since Fleece.Core.Models.Issue has init-only properties, this service returns
/// new Issue instances on updates rather than modifying existing ones.
/// </summary>
public class MockFleeceService : IFleeceService
{
    private readonly ConcurrentDictionary<string, List<Issue>> _issuesByProject = new();
    private readonly ILogger<MockFleeceService> _logger;
    private int _nextIssueNumber = 1;

    public MockFleeceService(ILogger<MockFleeceService> logger)
    {
        _logger = logger;
    }

    public Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] GetIssue {IssueId} from {ProjectPath}", issueId, projectPath);

        if (_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            var issue = issues.FirstOrDefault(i => i.Id == issueId);
            return Task.FromResult(issue);
        }

        return Task.FromResult<Issue?>(null);
    }

    public Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] ListIssues from {ProjectPath}", projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        var filtered = issues.AsEnumerable();

        if (status.HasValue)
        {
            filtered = filtered.Where(i => i.Status == status.Value);
        }

        if (type.HasValue)
        {
            filtered = filtered.Where(i => i.Type == type.Value);
        }

        if (priority.HasValue)
        {
            filtered = filtered.Where(i => i.Priority == priority.Value);
        }

        return Task.FromResult<IReadOnlyList<Issue>>(filtered.ToList());
    }

    public Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] GetReadyIssues from {ProjectPath}", projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        // Ready issues are those in Idea, Spec, Next, or Progress status
        var readyIssues = issues
            .Where(i => i.Status is IssueStatus.Idea or IssueStatus.Spec or IssueStatus.Next or IssueStatus.Progress)
            .ToList();

        return Task.FromResult<IReadOnlyList<Issue>>(readyIssues);
    }

    public Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        string? group = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] CreateIssue '{Title}' in {ProjectPath}", title, projectPath);

        var now = DateTime.UtcNow;
        var issue = new Issue
        {
            Id = GenerateIssueId(type),
            Title = title,
            Description = description ?? string.Empty,
            Type = type,
            Status = IssueStatus.Idea,
            Priority = priority ?? 3,
            Group = group ?? string.Empty,
            CreatedAt = now,
            LastUpdate = now
        };

        var issues = _issuesByProject.GetOrAdd(projectPath, _ => []);
        lock (issues)
        {
            issues.Add(issue);
        }

        return Task.FromResult(issue);
    }

    public Task<Issue?> UpdateIssueAsync(
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
        _logger.LogDebug("[Mock] UpdateIssue {IssueId} in {ProjectPath}", issueId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<Issue?>(null);
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == issueId);
            if (existingIndex < 0)
            {
                return Task.FromResult<Issue?>(null);
            }

            var existing = issues[existingIndex];

            // Create a new issue with updated values (Issue has init-only properties)
            var updated = new Issue
            {
                Id = existing.Id,
                Title = title ?? existing.Title,
                Description = description ?? existing.Description,
                Type = type ?? existing.Type,
                Status = status ?? existing.Status,
                Priority = priority ?? existing.Priority,
                Group = group ?? existing.Group,
                WorkingBranchId = workingBranchId ?? existing.WorkingBranchId,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = updated;
            return Task.FromResult<Issue?>(updated);
        }
    }

    public Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] DeleteIssue {IssueId} from {ProjectPath}", issueId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult(false);
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == issueId);
            if (existingIndex < 0)
            {
                return Task.FromResult(false);
            }

            var existing = issues[existingIndex];

            // Create a new issue with Deleted status (Issue has init-only properties)
            var deleted = new Issue
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                Type = existing.Type,
                Status = IssueStatus.Deleted,
                Priority = existing.Priority,
                Group = existing.Group,
                WorkingBranchId = existing.WorkingBranchId,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = deleted;
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Seeds an issue directly for testing/demo purposes.
    /// </summary>
    public void SeedIssue(string projectPath, Issue issue)
    {
        var issues = _issuesByProject.GetOrAdd(projectPath, _ => []);
        lock (issues)
        {
            issues.Add(issue);
        }
    }

    /// <summary>
    /// Clears all issues. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        _issuesByProject.Clear();
    }

    private string GenerateIssueId(IssueType type)
    {
        var prefix = type switch
        {
            IssueType.Task => "task",
            IssueType.Bug => "bug",
            IssueType.Feature => "feat",
            _ => "issue"
        };

        var number = Interlocked.Increment(ref _nextIssueNumber);
        var randomPart = Guid.NewGuid().ToString("N")[..6];
        return $"{prefix}/{randomPart}";
    }
}
