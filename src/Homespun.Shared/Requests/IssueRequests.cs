using Fleece.Core.Models;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating an issue.
/// </summary>
public class CreateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType Type { get; set; } = IssueType.Task;

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Execution mode for child issues (Series or Parallel).
    /// </summary>
    public ExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Optional working branch ID. If not provided, the client can trigger AI generation on the edit page.
    /// </summary>
    public string? WorkingBranchId { get; set; }

    /// <summary>
    /// Optional parent issue ID. If provided, the new issue will have this issue as its parent.
    /// </summary>
    public string? ParentIssueId { get; set; }

    /// <summary>
    /// Optional sibling issue ID for positioning within the parent's children.
    /// Used with InsertBefore to control where the new issue is placed relative to an existing sibling.
    /// </summary>
    public string? SiblingIssueId { get; set; }

    /// <summary>
    /// If true, insert before the sibling; if false (default), insert after.
    /// Only used when SiblingIssueId is provided.
    /// </summary>
    public bool InsertBefore { get; set; }

    /// <summary>
    /// Optional child issue ID. If provided, the new issue will become the parent of this issue.
    /// </summary>
    public string? ChildIssueId { get; set; }
}

/// <summary>
/// Request model for updating an issue.
/// </summary>
public class UpdateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Issue status.
    /// </summary>
    public IssueStatus? Status { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType? Type { get; set; }

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Execution mode for child issues (Series or Parallel).
    /// </summary>
    public ExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Optional working branch ID update.
    /// </summary>
    public string? WorkingBranchId { get; set; }

    /// <summary>
    /// Optional email of the user to assign the issue to.
    /// </summary>
    public string? AssignedTo { get; set; }
}

/// <summary>
/// Response model for resolved branch lookup.
/// </summary>
public class ResolvedBranchResponse
{
    /// <summary>
    /// The resolved branch name, or null if no existing branch was found.
    /// </summary>
    public string? BranchName { get; set; }
}

/// <summary>
/// Response model for the project assignees lookup.
/// </summary>
public class ProjectAssigneesResponse
{
    /// <summary>
    /// Unique assignee email addresses for the project, sorted with the current
    /// user first (if configured) and the rest alphabetically.
    /// </summary>
    public List<string> Assignees { get; set; } = [];
}

/// <summary>
/// Request model for setting an issue's parent.
/// </summary>
public class SetParentRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The ID of the issue that will become the parent.
    /// </summary>
    public required string ParentIssueId { get; set; }

    /// <summary>
    /// If true, adds the parent to existing parents. If false (default), replaces all existing parents.
    /// </summary>
    public bool AddToExisting { get; set; } = false;
}

/// <summary>
/// Direction for moving a sibling issue within a series.
/// </summary>
public enum MoveDirection
{
    /// <summary>Move the issue up (lower sort order / earlier in series).</summary>
    Up,
    /// <summary>Move the issue down (higher sort order / later in series).</summary>
    Down
}

/// <summary>
/// Request model for moving a sibling issue up or down in the series order.
/// </summary>
public class MoveSeriesSiblingRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Direction to move the issue (Up or Down).
    /// </summary>
    public MoveDirection Direction { get; set; }
}

/// <summary>
/// Request model for removing a specific parent from an issue.
/// </summary>
public class RemoveParentRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The ID of the parent issue to remove.
    /// </summary>
    public required string ParentIssueId { get; set; }
}

/// <summary>
/// Request model for removing all parents from an issue.
/// </summary>
public class RemoveAllParentsRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }
}

/// <summary>
/// Request model for running an agent on an issue.
/// </summary>
public class RunAgentRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session mode to use (Plan or Build).
    /// If not specified, defaults to Plan.
    /// </summary>
    public SessionMode? Mode { get; set; }

    /// <summary>
    /// The Claude model to use (e.g., "sonnet").
    /// If not specified, defaults to project's default model.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Base branch to create the working branch from.
    /// If not specified, defaults to project's default branch.
    /// </summary>
    public string? BaseBranch { get; set; }

    /// <summary>
    /// Optional user instructions to send as the initial message.
    /// </summary>
    public string? UserInstructions { get; set; }

    /// <summary>
    /// Optional skill name (directory under <c>.claude/skills/</c>) to dispatch with.
    /// When set, the skill's SKILL.md body is composed into the initial message and
    /// the skill's declared mode is applied when <see cref="Mode"/> is not set.
    /// </summary>
    public string? SkillName { get; set; }

    /// <summary>
    /// Optional named arguments to append to the skill body when composing the
    /// initial message. Each entry becomes an <c>arg-name: value</c> line.
    /// </summary>
    public Dictionary<string, string>? SkillArgs { get; set; }
}

/// <summary>
/// Response model when a phase-specific dispatch is rejected because prior phases are incomplete.
/// </summary>
public class PhaseDispatchBlockedResponse
{
    /// <summary>
    /// Names of the incomplete phases that must be completed first.
    /// </summary>
    public required List<string> BlockingPhases { get; set; }

    /// <summary>
    /// A human-readable message explaining why the dispatch was rejected.
    /// </summary>
    public required string Message { get; set; }
}

/// <summary>
/// Response model when an agent is already running on the issue.
/// </summary>
public class AgentAlreadyRunningResponse
{
    /// <summary>
    /// The existing session ID.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// The status of the existing session.
    /// </summary>
    public required ClaudeSessionStatus Status { get; set; }

    /// <summary>
    /// A human-readable message explaining why the request was rejected.
    /// </summary>
    public required string Message { get; set; }
}

/// <summary>
/// Response model for accepted agent start request (202 Accepted).
/// The agent startup will proceed in the background.
/// </summary>
public class RunAgentAcceptedResponse
{
    /// <summary>
    /// The issue ID the agent is starting on.
    /// </summary>
    public required string IssueId { get; set; }

    /// <summary>
    /// The branch name being used for the agent session.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// A human-readable message about the status.
    /// </summary>
    public string Message { get; set; } = "Agent is starting";
}
