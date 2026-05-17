using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for detecting changes between agent and main branch Fleece issues.
/// </summary>
public interface IFleeceChangeDetectionService
{
    /// <summary>
    /// Detects changes made by an agent session compared to the main branch.
    /// </summary>
    Task<List<IssueChangeDto>> DetectChangesAsync(
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of change detection service. Reads main- and clone-side issues
/// through <see cref="IProjectFleeceService.GetAllIssuesFromPathAsync"/> (which under
/// the Fleece 3.1 event-sourced storage replays the snapshot + all change files),
/// then diffs field-by-field. Field-level conflict resolution happens at apply time
/// via `git merge` of the agent branch — no manual <c>IssueMerger</c> step is needed.
/// </summary>
public class FleeceChangeDetectionService : IFleeceChangeDetectionService
{
    private readonly IProjectService _projectService;
    private readonly IGitCloneService _cloneService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IProjectFleeceService _fleeceService;
    private readonly ILogger<FleeceChangeDetectionService> _logger;

    public FleeceChangeDetectionService(
        IProjectService projectService,
        IGitCloneService cloneService,
        IClaudeSessionService sessionService,
        IProjectFleeceService fleeceService,
        ILogger<FleeceChangeDetectionService> logger)
    {
        _projectService = projectService;
        _cloneService = cloneService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _logger = logger;
    }

    public async Task<List<IssueChangeDto>> DetectChangesAsync(
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            throw new ArgumentException($"Project {projectId} not found");
        }

        var session = _sessionService.GetSession(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        if (string.IsNullOrEmpty(session.EntityId))
        {
            throw new InvalidOperationException("Session is not linked to an issue");
        }

        var clonePath = session.WorkingDirectory;
        if (string.IsNullOrEmpty(clonePath))
        {
            throw new InvalidOperationException("Session does not have a working directory");
        }

        _logger.LogInformation(
            "Detecting changes for session {SessionId}: main branch path={MainPath}, clone path={ClonePath}",
            sessionId, project.LocalPath, clonePath);

        var mainIssues = await _fleeceService.GetAllIssuesFromPathAsync(project.LocalPath, cancellationToken);
        var cloneIssues = await _fleeceService.GetAllIssuesFromPathAsync(clonePath, cancellationToken);

        var mainIssueMap = mainIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var cloneIssueMap = cloneIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug("Loaded {MainCount} main issues and {CloneCount} clone issues",
            mainIssues.Count, cloneIssues.Count);

        var changes = new List<IssueChangeDto>();

        // Created: in clone only.
        foreach (var cloneIssue in cloneIssues)
        {
            if (!mainIssueMap.ContainsKey(cloneIssue.Id))
            {
                changes.Add(new IssueChangeDto
                {
                    IssueId = cloneIssue.Id,
                    ChangeType = ChangeType.Created,
                    Title = cloneIssue.Title,
                    ModifiedIssue = cloneIssue.ToDto(),
                    FieldChanges = GetAllFieldsAsChanges(cloneIssue)
                });
            }
        }

        // Updated: in both; field-level diff against main.
        foreach (var cloneIssue in cloneIssues)
        {
            if (!mainIssueMap.TryGetValue(cloneIssue.Id, out var mainIssue))
                continue;

            var fieldChanges = GetFieldChanges(mainIssue, cloneIssue);
            if (fieldChanges.Any())
            {
                changes.Add(new IssueChangeDto
                {
                    IssueId = cloneIssue.Id,
                    ChangeType = ChangeType.Updated,
                    Title = cloneIssue.Title,
                    OriginalIssue = mainIssue.ToDto(),
                    ModifiedIssue = cloneIssue.ToDto(),
                    FieldChanges = fieldChanges
                });
            }

            if (cloneIssue.Status == IssueStatus.Deleted && mainIssue.Status != IssueStatus.Deleted)
            {
                changes.Add(new IssueChangeDto
                {
                    IssueId = mainIssue.Id,
                    ChangeType = ChangeType.Deleted,
                    Title = mainIssue.Title,
                    OriginalIssue = mainIssue.ToDto(),
                    FieldChanges = [new() { FieldName = "Status", OldValue = mainIssue.Status.ToString(), NewValue = "Deleted" }]
                });
            }
        }

        foreach (var mainIssue in mainIssues)
        {
            if (!cloneIssueMap.ContainsKey(mainIssue.Id))
            {
                _logger.LogWarning("Issue {IssueId} exists in main but not in agent clone", mainIssue.Id);
            }
        }

        _logger.LogInformation("Detected {Count} changes from session {SessionId}",
            changes.Count, sessionId);
        return changes;
    }

    private List<FieldChangeDto> GetFieldChanges(Issue original, Issue modified)
    {
        var changes = new List<FieldChangeDto>();

        if (original.Title != modified.Title)
            changes.Add(new FieldChangeDto { FieldName = "Title", OldValue = original.Title, NewValue = modified.Title });

        if (original.Description != modified.Description)
            changes.Add(new FieldChangeDto { FieldName = "Description", OldValue = original.Description, NewValue = modified.Description });

        if (original.Status != modified.Status)
            changes.Add(new FieldChangeDto { FieldName = "Status", OldValue = original.Status.ToString(), NewValue = modified.Status.ToString() });

        if (original.Type != modified.Type)
            changes.Add(new FieldChangeDto { FieldName = "Type", OldValue = original.Type.ToString(), NewValue = modified.Type.ToString() });

        if (original.Priority != modified.Priority)
            changes.Add(new FieldChangeDto { FieldName = "Priority", OldValue = original.Priority?.ToString(), NewValue = modified.Priority?.ToString() });

        if (!AreIntListsEqual(original.LinkedPRs, modified.LinkedPRs))
            changes.Add(new FieldChangeDto { FieldName = "LinkedPRs", OldValue = string.Join(", ", original.LinkedPRs), NewValue = string.Join(", ", modified.LinkedPRs) });

        if (original.WorkingBranchId != modified.WorkingBranchId)
            changes.Add(new FieldChangeDto { FieldName = "WorkingBranchId", OldValue = original.WorkingBranchId, NewValue = modified.WorkingBranchId });

        if (original.ExecutionMode != modified.ExecutionMode)
            changes.Add(new FieldChangeDto { FieldName = "ExecutionMode", OldValue = original.ExecutionMode.ToString(), NewValue = modified.ExecutionMode.ToString() });

        if (original.AssignedTo != modified.AssignedTo)
            changes.Add(new FieldChangeDto { FieldName = "AssignedTo", OldValue = original.AssignedTo, NewValue = modified.AssignedTo });

        if (!AreParentIssuesEqual(original.ParentIssues, modified.ParentIssues))
            changes.Add(new FieldChangeDto { FieldName = "ParentIssues", OldValue = SerializeParentIssues(original.ParentIssues), NewValue = SerializeParentIssues(modified.ParentIssues) });

        if (!AreStringListsEqual(original.LinkedIssues, modified.LinkedIssues))
            changes.Add(new FieldChangeDto { FieldName = "LinkedIssues", OldValue = string.Join(", ", original.LinkedIssues), NewValue = string.Join(", ", modified.LinkedIssues) });

        if (!AreStringListsEqual(original.Tags, modified.Tags))
            changes.Add(new FieldChangeDto { FieldName = "Tags", OldValue = string.Join(", ", original.Tags), NewValue = string.Join(", ", modified.Tags) });

        return changes;
    }

    private List<FieldChangeDto> GetAllFieldsAsChanges(Issue issue)
    {
        return new List<FieldChangeDto>
        {
            new() { FieldName = "Title", OldValue = null, NewValue = issue.Title },
            new() { FieldName = "Description", OldValue = null, NewValue = issue.Description },
            new() { FieldName = "Status", OldValue = null, NewValue = issue.Status.ToString() },
            new() { FieldName = "Type", OldValue = null, NewValue = issue.Type.ToString() },
            new() { FieldName = "Priority", OldValue = null, NewValue = issue.Priority?.ToString() },
            new() { FieldName = "ExecutionMode", OldValue = null, NewValue = issue.ExecutionMode.ToString() },
            new() { FieldName = "WorkingBranchId", OldValue = null, NewValue = issue.WorkingBranchId },
            new() { FieldName = "AssignedTo", OldValue = null, NewValue = issue.AssignedTo }
        };
    }

    private bool AreParentIssuesEqual(IReadOnlyList<ParentIssueRef> list1, IReadOnlyList<ParentIssueRef> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();
        var set2 = list2.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();

        return set1.SequenceEqual(set2);
    }

    private bool AreStringListsEqual(IReadOnlyList<string> list1, IReadOnlyList<string> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.OrderBy(s => s).ToList();
        var set2 = list2.OrderBy(s => s).ToList();

        return set1.SequenceEqual(set2);
    }

    private bool AreIntListsEqual(IReadOnlyList<int> list1, IReadOnlyList<int> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.OrderBy(x => x).ToList();
        var set2 = list2.OrderBy(x => x).ToList();

        return set1.SequenceEqual(set2);
    }

    private string SerializeParentIssues(IReadOnlyList<ParentIssueRef> parents)
    {
        return string.Join(", ", parents.Select(p => $"{p.ParentIssue}:{p.SortOrder ?? "0"}"));
    }
}
