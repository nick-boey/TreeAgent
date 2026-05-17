using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Implementation of change application service.
/// Under Fleece 3.1 event-sourced storage, applying an agent's changes is a
/// <c>git merge</c> of the agent branch into main: each clone owns one
/// <c>.fleece/changes/change_{guid}.jsonl</c> event log, the GUIDs guarantee no
/// file-level conflicts, and replay over the merged event set produces the
/// field-level last-writer-wins state for free. No <c>IssueMerger</c> step
/// is needed.
/// </summary>
public class FleeceChangeApplicationService : IFleeceChangeApplicationService
{
    private readonly IProjectService _projectService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IProjectFleeceService _fleeceService;
    private readonly IFleeceChangeDetectionService _changeDetectionService;
    private readonly IFleeceConflictDetectionService _conflictDetectionService;
    private readonly IGitCloneService _cloneService;
    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<FleeceChangeApplicationService> _logger;

    // Store pending conflicts for manual resolution
    private readonly Dictionary<string, List<IssueConflictDto>> _pendingConflicts = new();

    public FleeceChangeApplicationService(
        IProjectService projectService,
        IClaudeSessionService sessionService,
        IProjectFleeceService fleeceService,
        IFleeceChangeDetectionService changeDetectionService,
        IFleeceConflictDetectionService conflictDetectionService,
        IGitCloneService cloneService,
        ICommandRunner commandRunner,
        ILogger<FleeceChangeApplicationService> logger)
    {
        _projectService = projectService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _changeDetectionService = changeDetectionService;
        _conflictDetectionService = conflictDetectionService;
        _cloneService = cloneService;
        _commandRunner = commandRunner;
        _logger = logger;
    }

    public async Task<ApplyAgentChangesResponse> ApplyChangesAsync(
        string projectId,
        string sessionId,
        ConflictResolutionStrategy conflictStrategy,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var project = await _projectService.GetByIdAsync(projectId);
            if (project == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Project {projectId} not found"
                };
            }

            var session = _sessionService.GetSession(sessionId);
            if (session == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Session {sessionId} not found"
                };
            }

            if (session.Status == ClaudeSessionStatus.Running || session.Status == ClaudeSessionStatus.RunningHooks)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = "Cannot apply changes while session is active. Stop the session first."
                };
            }

            var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId, cancellationToken);
            if (!changes.Any())
            {
                return new ApplyAgentChangesResponse
                {
                    Success = true,
                    Message = "No changes detected",
                    Changes = [],
                    WouldApply = false
                };
            }

            var conflicts = await _conflictDetectionService.DetectConflictsAsync(
                projectId, sessionId, changes, cancellationToken);

            if (conflicts.Any())
            {
                switch (conflictStrategy)
                {
                    case ConflictResolutionStrategy.Abort:
                        return new ApplyAgentChangesResponse
                        {
                            Success = false,
                            Message = $"Aborted due to {conflicts.Count} conflicts",
                            Changes = changes,
                            Conflicts = conflicts,
                            WouldApply = false
                        };

                    case ConflictResolutionStrategy.Manual:
                        if (!dryRun)
                        {
                            _pendingConflicts[$"{projectId}:{sessionId}"] = conflicts;
                        }
                        return new ApplyAgentChangesResponse
                        {
                            Success = false,
                            Message = $"Manual resolution required for {conflicts.Count} conflicts",
                            Changes = changes,
                            Conflicts = conflicts,
                            WouldApply = false
                        };

                    case ConflictResolutionStrategy.AgentWins:
                    case ConflictResolutionStrategy.MainWins:
                        break;
                }
            }

            if (dryRun)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = true,
                    Message = $"Would apply {changes.Count} changes" + (conflicts.Any() ? $" with {conflicts.Count} conflicts" : ""),
                    Changes = changes,
                    Conflicts = conflicts,
                    WouldApply = true
                };
            }

            if (conflictStrategy == ConflictResolutionStrategy.MainWins)
            {
                _logger.LogInformation(
                    "Apply with MainWins strategy: skipping agent branch merge for session {SessionId}",
                    sessionId);

                await _fleeceService.ReloadFromDiskAsync(project.LocalPath, cancellationToken);

                return new ApplyAgentChangesResponse
                {
                    Success = true,
                    Message = "Main wins: agent's changes were not merged.",
                    Changes = changes,
                    Conflicts = conflicts
                };
            }

            // AgentWins or no conflicts: git merge the agent's branch into main.
            return await ApplyChangesViaGitMergeAsync(
                project.LocalPath, session.WorkingDirectory, changes, conflicts, sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes from session {SessionId}", sessionId);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Error applying changes: {ex.Message}"
            };
        }
    }

    public async Task<ApplyAgentChangesResponse> ResolveConflictsAsync(
        string projectId,
        string sessionId,
        List<ConflictResolution> resolutions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{projectId}:{sessionId}";
            if (!_pendingConflicts.TryGetValue(key, out var conflicts))
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = "No pending conflicts found for this session"
                };
            }

            var project = await _projectService.GetByIdAsync(projectId);
            if (project == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Project {projectId} not found"
                };
            }

            var appliedChanges = new List<IssueChangeDto>();
            var errors = new List<string>();

            foreach (var resolution in resolutions)
            {
                var conflict = conflicts.FirstOrDefault(c => c.IssueId == resolution.IssueId);
                if (conflict == null)
                    continue;

                try
                {
                    var mergedIssue = await ApplyResolutionAsync(
                        project.LocalPath, conflict, resolution, cancellationToken);

                    appliedChanges.Add(new IssueChangeDto
                    {
                        IssueId = conflict.IssueId,
                        ChangeType = ChangeType.Updated,
                        Title = mergedIssue.Title,
                        OriginalIssue = conflict.MainIssue,
                        ModifiedIssue = mergedIssue.ToDto()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying resolution to issue {IssueId}", resolution.IssueId);
                    errors.Add($"Failed to resolve {resolution.IssueId}: {ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                _pendingConflicts.Remove(key);
            }

            var success = errors.Count == 0;
            var message = success
                ? $"Resolved {appliedChanges.Count} conflicts successfully"
                : $"Resolved {appliedChanges.Count} conflicts with {errors.Count} errors";

            return new ApplyAgentChangesResponse
            {
                Success = success,
                Message = message,
                Changes = appliedChanges
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving conflicts for session {SessionId}", sessionId);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Error resolving conflicts: {ex.Message}"
            };
        }
    }

    private async Task<ApplyAgentChangesResponse> ApplyChangesViaGitMergeAsync(
        string mainPath,
        string agentClonePath,
        List<IssueChangeDto> changes,
        List<IssueConflictDto> conflicts,
        string sessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Applying changes via git merge: main={MainPath}, agent={AgentPath}",
            mainPath, agentClonePath);

        var branchName = await _cloneService.GetCurrentBranchAsync(agentClonePath);
        if (string.IsNullOrEmpty(branchName))
        {
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Could not resolve agent branch name from clone at {agentClonePath}"
            };
        }

        // The agent's working tree may still hold uncommitted .fleece/changes/ events;
        // commit them inside the clone first so the merge has something to bring across.
        // The `fleece install` pre-commit hook auto-stages .fleece/changes/, so a plain
        // `git commit` is sufficient.
        var commitMsg = $"chore(fleece): pending agent changes for session {sessionId}";
        await _commandRunner.RunAsync("git", $"commit -m \"{commitMsg}\" --allow-empty", agentClonePath);

        var fetchResult = await _commandRunner.RunAsync(
            "git", $"fetch \"{agentClonePath}\" \"{branchName}\"", mainPath);
        if (!fetchResult.Success)
        {
            _logger.LogWarning("git fetch from agent clone failed: {Error}", fetchResult.Error);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Failed to fetch agent branch into main: {fetchResult.Error}"
            };
        }

        var mergeResult = await _commandRunner.RunAsync(
            "git",
            $"merge FETCH_HEAD --no-ff --no-edit -m \"chore(fleece): apply agent changes from session {sessionId}\"",
            mainPath);

        if (!mergeResult.Success)
        {
            _logger.LogWarning("git merge of agent branch failed: {Error}", mergeResult.Error);
            await _commandRunner.RunAsync("git", "merge --abort", mainPath);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Failed to merge agent branch into main: {mergeResult.Error}"
            };
        }

        await _fleeceService.ReloadFromDiskAsync(mainPath, cancellationToken);

        _logger.LogInformation("Merged agent branch '{Branch}' into main and reloaded cache", branchName);
        return new ApplyAgentChangesResponse
        {
            Success = true,
            Message = $"Applied {changes.Count} changes via git merge",
            Changes = changes,
            Conflicts = conflicts
        };
    }

    private async Task<Issue> ApplyResolutionAsync(
        string projectPath,
        IssueConflictDto conflict,
        ConflictResolution resolution,
        CancellationToken cancellationToken)
    {
        var currentIssue = await _fleeceService.GetIssueAsync(projectPath, conflict.IssueId, cancellationToken);
        if (currentIssue == null)
        {
            throw new InvalidOperationException($"Issue {conflict.IssueId} not found");
        }

        var updates = new Dictionary<string, object?>();

        foreach (var fieldResolution in resolution.FieldResolutions)
        {
            var fieldConflict = conflict.FieldConflicts.FirstOrDefault(f => f.FieldName == fieldResolution.FieldName);
            if (fieldConflict == null)
                continue;

            var value = fieldResolution.Choice switch
            {
                ConflictChoice.UseMain => fieldConflict.MainValue,
                ConflictChoice.UseAgent => fieldConflict.AgentValue,
                ConflictChoice.Custom => fieldResolution.CustomValue,
                _ => fieldConflict.MainValue
            };

            updates[fieldResolution.FieldName] = value;
        }

        await _fleeceService.UpdateIssueAsync(
            projectPath,
            conflict.IssueId,
            updates.GetValueOrDefault("Title")?.ToString(),
            ParseEnum<IssueStatus>(updates.GetValueOrDefault("Status")),
            ParseEnum<IssueType>(updates.GetValueOrDefault("Type")),
            updates.GetValueOrDefault("Description")?.ToString(),
            ParseInt(updates.GetValueOrDefault("Priority")),
            ParseEnum<ExecutionMode>(updates.GetValueOrDefault("ExecutionMode")),
            updates.GetValueOrDefault("WorkingBranchId")?.ToString(),
            updates.GetValueOrDefault("AssignedTo")?.ToString(),
            cancellationToken);

        return (await _fleeceService.GetIssueAsync(projectPath, conflict.IssueId, cancellationToken))!;
    }

    private static T? ParseEnum<T>(object? value) where T : struct, Enum
    {
        if (value == null)
            return null;
        if (Enum.TryParse<T>(value.ToString(), out var result))
            return result;
        return null;
    }

    private static int? ParseInt(object? value)
    {
        if (value == null)
            return null;
        if (int.TryParse(value.ToString(), out var result))
            return result;
        return null;
    }
}
