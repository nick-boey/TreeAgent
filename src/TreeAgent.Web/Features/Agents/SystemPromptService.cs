using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.Agents;

/// <summary>
/// Service for managing and processing system prompt templates
/// </summary>
public partial class SystemPromptService(TreeAgentDbContext db, FeatureService featureService)
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
    public async Task<string> ProcessTemplateAsync(string template, string? featureId = null)
    {
        if (string.IsNullOrEmpty(template)) return template;

        Feature? feature = null;
        Project? project = null;

        if (!string.IsNullOrEmpty(featureId))
        {
            feature = await db.Features
                .Include(f => f.Project)
                .FirstOrDefaultAsync(f => f.Id == featureId);

            project = feature?.Project;
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

        // Feature variables
        if (feature != null)
        {
            result = result.Replace("{{FEATURE_TITLE}}", feature.Title);
            result = result.Replace("{{FEATURE_DESCRIPTION}}", feature.Description ?? "");
            result = result.Replace("{{BRANCH_NAME}}", feature.BranchName ?? "");
            result = result.Replace("{{FEATURE_STATUS}}", feature.Status.ToString());
            result = result.Replace("{{WORKTREE_PATH}}", feature.WorktreePath ?? "");

            // Feature tree context
            if (result.Contains("{{FEATURE_TREE}}"))
            {
                var featureTree = await BuildFeatureTreeContextAsync(feature.ProjectId);
                result = result.Replace("{{FEATURE_TREE}}", featureTree);
            }

            // Related features (siblings)
            if (result.Contains("{{RELATED_FEATURES}}"))
            {
                var relatedFeatures = await BuildRelatedFeaturesContextAsync(feature);
                result = result.Replace("{{RELATED_FEATURES}}", relatedFeatures);
            }
        }

        // Remove any remaining unprocessed variables
        result = VariablePattern().Replace(result, "");

        return result;
    }

    /// <summary>
    /// Get the effective system prompt for a feature, considering feature-specific, project default, and template
    /// </summary>
    public async Task<string?> GetEffectivePromptAsync(string featureId)
    {
        var feature = await db.Features
            .Include(f => f.Project)
            .ThenInclude(p => p.DefaultPromptTemplate)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null) return null;

        // Priority: Feature-level agent prompt > Project default prompt > Project default template
        string? rawPrompt = null;

        // Check for project-level default
        if (!string.IsNullOrEmpty(feature.Project.DefaultSystemPrompt))
        {
            rawPrompt = feature.Project.DefaultSystemPrompt;
        }
        else if (feature.Project.DefaultPromptTemplate != null)
        {
            rawPrompt = feature.Project.DefaultPromptTemplate.Content;
        }

        if (string.IsNullOrEmpty(rawPrompt))
        {
            return null;
        }

        return await ProcessTemplateAsync(rawPrompt, featureId);
    }

    private async Task<string> BuildFeatureTreeContextAsync(string projectId)
    {
        var features = await featureService.GetTreeAsync(projectId);
        if (features.Count == 0) return "No features defined.";

        var sb = new StringBuilder();
        sb.AppendLine("Feature Tree:");

        void AppendFeature(Feature f, int level)
        {
            var indent = new string(' ', level * 2);
            var status = f.Status.ToString();
            sb.AppendLine($"{indent}- [{status}] {f.Title}");

            foreach (var child in f.Children.OrderBy(c => c.CreatedAt))
            {
                AppendFeature(child, level + 1);
            }
        }

        foreach (var feature in features)
        {
            AppendFeature(feature, 0);
        }

        return sb.ToString();
    }

    private async Task<string> BuildRelatedFeaturesContextAsync(Feature feature)
    {
        var siblings = await db.Features
            .Where(f => f.ProjectId == feature.ProjectId &&
                       f.ParentId == feature.ParentId &&
                       f.Id != feature.Id)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        if (siblings.Count == 0) return "No related features.";

        var sb = new StringBuilder();
        sb.AppendLine("Related Features (siblings):");

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
            new("{{FEATURE_TITLE}}", "Title of the current feature"),
            new("{{FEATURE_DESCRIPTION}}", "Description of the current feature"),
            new("{{BRANCH_NAME}}", "Git branch name for the feature"),
            new("{{FEATURE_STATUS}}", "Current status of the feature"),
            new("{{WORKTREE_PATH}}", "Path to the git worktree for this feature"),
            new("{{FEATURE_TREE}}", "Hierarchical view of all features in the project"),
            new("{{RELATED_FEATURES}}", "List of sibling features")
        ];
    }

    [GeneratedRegex(@"\{\{[A-Z_]+\}\}")]
    private static partial Regex VariablePattern();

    #endregion
}