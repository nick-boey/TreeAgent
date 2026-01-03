using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Agents;

/// <summary>
/// Service for managing and processing system prompt templates
/// </summary>
public partial class SystemPromptService(TreeAgentDbContext db, PullRequestDataService pullRequestDataService)
{
    #region Template CRUD

    public async Task<List<SystemPromptTemplate>> GetGlobalTemplatesAsync()
    {
        return await db.SystemPromptTemplates
            .Where(t => t.IsGlobal)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<SystemPromptTemplate>> GetProjectTemplatesAsync(string projectId)
    {
        return await db.SystemPromptTemplates
            .Where(t => t.ProjectId == projectId || t.IsGlobal)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<SystemPromptTemplate?> GetByIdAsync(string id)
    {
        return await db.SystemPromptTemplates.FindAsync(id);
    }

    public async Task<SystemPromptTemplate> CreateAsync(
        string name,
        string content,
        string? description = null,
        string? projectId = null,
        bool isGlobal = false)
    {
        var template = new SystemPromptTemplate
        {
            Name = name,
            Content = content,
            Description = description,
            ProjectId = projectId,
            IsGlobal = isGlobal
        };

        db.SystemPromptTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    public async Task<SystemPromptTemplate?> UpdateAsync(
        string id,
        string name,
        string content,
        string? description,
        bool isGlobal)
    {
        var template = await db.SystemPromptTemplates.FindAsync(id);
        if (template == null) return null;

        template.Name = name;
        template.Content = content;
        template.Description = description;
        template.IsGlobal = isGlobal;
        template.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return template;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var template = await db.SystemPromptTemplates.FindAsync(id);
        if (template == null) return false;

        db.SystemPromptTemplates.Remove(template);
        await db.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Template Processing

    /// <summary>
    /// Process a template and substitute variables with actual values
    /// </summary>
    public async Task<string> ProcessTemplateAsync(string template, string? pullRequestId = null)
    {
        if (string.IsNullOrEmpty(template)) return template;

        PullRequest? pullRequest = null;
        Project? project = null;

        if (!string.IsNullOrEmpty(pullRequestId))
        {
            pullRequest = await db.PullRequests
                .Include(pr => pr.Project)
                .FirstOrDefaultAsync(pr => pr.Id == pullRequestId);

            project = pullRequest?.Project;
        }

        var result = template;

        // Project variables
        if (project != null)
        {
            result = result.Replace("{{PROJECT_NAME}}", project.Name);
            result = result.Replace("{{PROJECT_PATH}}", project.LocalPath);
            result = result.Replace("{{DEFAULT_BRANCH}}", project.DefaultBranch);
            result = result.Replace("{{GITHUB_OWNER}}", project.GitHubOwner ?? "");
            result = result.Replace("{{GITHUB_REPO}}", project.GitHubRepo ?? "");
        }

        // Pull request variables
        if (pullRequest != null)
        {
            result = result.Replace("{{FEATURE_TITLE}}", pullRequest.Title);
            result = result.Replace("{{FEATURE_DESCRIPTION}}", pullRequest.Description ?? "");
            result = result.Replace("{{BRANCH_NAME}}", pullRequest.BranchName ?? "");
            result = result.Replace("{{FEATURE_STATUS}}", pullRequest.Status.ToString());
            result = result.Replace("{{WORKTREE_PATH}}", pullRequest.WorktreePath ?? "");

            // Also support new naming convention
            result = result.Replace("{{PR_TITLE}}", pullRequest.Title);
            result = result.Replace("{{PR_DESCRIPTION}}", pullRequest.Description ?? "");
            result = result.Replace("{{PR_STATUS}}", pullRequest.Status.ToString());

            // Pull request tree context
            if (result.Contains("{{FEATURE_TREE}}") || result.Contains("{{PR_TREE}}"))
            {
                var prTree = await BuildPullRequestTreeContextAsync(pullRequest.ProjectId);
                result = result.Replace("{{FEATURE_TREE}}", prTree);
                result = result.Replace("{{PR_TREE}}", prTree);
            }

            // Related pull requests (siblings)
            if (result.Contains("{{RELATED_FEATURES}}") || result.Contains("{{RELATED_PRS}}"))
            {
                var relatedPRs = await BuildRelatedPullRequestsContextAsync(pullRequest);
                result = result.Replace("{{RELATED_FEATURES}}", relatedPRs);
                result = result.Replace("{{RELATED_PRS}}", relatedPRs);
            }
        }

        // Remove any remaining unprocessed variables
        result = VariablePattern().Replace(result, "");

        return result;
    }

    /// <summary>
    /// Get the effective system prompt for a pull request, considering project defaults and templates
    /// </summary>
    public async Task<string?> GetEffectivePromptAsync(string pullRequestId)
    {
        var pullRequest = await db.PullRequests
            .Include(pr => pr.Project)
            .ThenInclude(p => p.DefaultPromptTemplate)
            .FirstOrDefaultAsync(pr => pr.Id == pullRequestId);

        if (pullRequest == null) return null;

        // Priority: Project default prompt > Project default template
        string? rawPrompt = null;

        // Check for project-level default
        if (!string.IsNullOrEmpty(pullRequest.Project.DefaultSystemPrompt))
        {
            rawPrompt = pullRequest.Project.DefaultSystemPrompt;
        }
        else if (pullRequest.Project.DefaultPromptTemplate != null)
        {
            rawPrompt = pullRequest.Project.DefaultPromptTemplate.Content;
        }

        if (string.IsNullOrEmpty(rawPrompt))
        {
            return null;
        }

        return await ProcessTemplateAsync(rawPrompt, pullRequestId);
    }

    private async Task<string> BuildPullRequestTreeContextAsync(string projectId)
    {
        var pullRequests = await pullRequestDataService.GetTreeAsync(projectId);
        if (pullRequests.Count == 0) return "No pull requests defined.";

        var sb = new StringBuilder();
        sb.AppendLine("Pull Request Tree:");

        void AppendPullRequest(PullRequest pr, int level)
        {
            var indent = new string(' ', level * 2);
            var status = pr.Status.ToString();
            sb.AppendLine($"{indent}- [{status}] {pr.Title}");

            foreach (var child in pr.Children.OrderBy(c => c.CreatedAt))
            {
                AppendPullRequest(child, level + 1);
            }
        }

        foreach (var pullRequest in pullRequests)
        {
            AppendPullRequest(pullRequest, 0);
        }

        return sb.ToString();
    }

    private async Task<string> BuildRelatedPullRequestsContextAsync(PullRequest pullRequest)
    {
        var siblings = await db.PullRequests
            .Where(pr => pr.ProjectId == pullRequest.ProjectId &&
                       pr.ParentId == pullRequest.ParentId &&
                       pr.Id != pullRequest.Id)
            .OrderBy(pr => pr.CreatedAt)
            .ToListAsync();

        if (siblings.Count == 0) return "No related pull requests.";

        var sb = new StringBuilder();
        sb.AppendLine("Related Pull Requests (siblings):");

        foreach (var sibling in siblings)
        {
            sb.AppendLine($"- [{sibling.Status}] {sibling.Title}");
        }

        return sb.ToString();
    }

    #endregion

    #region Available Variables

    public static IReadOnlyList<TemplateVariable> GetAvailableVariables()
    {
        return
        [
            new("{{PROJECT_NAME}}", "Name of the current project"),
            new("{{PROJECT_PATH}}", "Local filesystem path of the project"),
            new("{{DEFAULT_BRANCH}}", "Default git branch (e.g., main)"),
            new("{{GITHUB_OWNER}}", "GitHub repository owner"),
            new("{{GITHUB_REPO}}", "GitHub repository name"),
            new("{{FEATURE_TITLE}}", "Title of the current pull request (legacy)"),
            new("{{FEATURE_DESCRIPTION}}", "Description of the current pull request (legacy)"),
            new("{{PR_TITLE}}", "Title of the current pull request"),
            new("{{PR_DESCRIPTION}}", "Description of the current pull request"),
            new("{{BRANCH_NAME}}", "Git branch name for the pull request"),
            new("{{FEATURE_STATUS}}", "Current status of the pull request (legacy)"),
            new("{{PR_STATUS}}", "Current status of the pull request"),
            new("{{WORKTREE_PATH}}", "Path to the git worktree for this pull request"),
            new("{{FEATURE_TREE}}", "Hierarchical view of all pull requests in the project (legacy)"),
            new("{{PR_TREE}}", "Hierarchical view of all pull requests in the project"),
            new("{{RELATED_FEATURES}}", "List of sibling pull requests (legacy)"),
            new("{{RELATED_PRS}}", "List of sibling pull requests")
        ];
    }

    [GeneratedRegex(@"\{\{[A-Z_]+\}\}")]
    private static partial Regex VariablePattern();

    #endregion
}
