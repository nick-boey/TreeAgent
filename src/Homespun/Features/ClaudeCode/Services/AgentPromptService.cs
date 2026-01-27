using System.Text.RegularExpressions;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing custom agent prompts.
/// </summary>
public partial class AgentPromptService : IAgentPromptService
{
    private readonly IDataStore _dataStore;

    public AgentPromptService(IDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public IReadOnlyList<AgentPrompt> GetAllPrompts()
    {
        return _dataStore.AgentPrompts;
    }

    public AgentPrompt? GetPrompt(string id)
    {
        return _dataStore.GetAgentPrompt(id);
    }

    public async Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode)
    {
        var prompt = new AgentPrompt
        {
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task<AgentPrompt> UpdatePromptAsync(string id, string name, string? initialMessage, SessionMode mode)
    {
        var prompt = _dataStore.GetAgentPrompt(id)
            ?? throw new InvalidOperationException($"Agent prompt with ID '{id}' not found.");

        prompt.Name = name;
        prompt.InitialMessage = initialMessage;
        prompt.Mode = mode;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _dataStore.UpdateAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(string id)
    {
        await _dataStore.RemoveAgentPromptAsync(id);
    }

    public string? RenderTemplate(string? template, PromptContext context)
    {
        if (template == null)
            return null;

        var result = template;

        // Replace placeholders (case-insensitive)
        result = PlaceholderRegex().Replace(result, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
            return placeholder switch
            {
                "title" => context.Title,
                "id" => context.Id,
                "description" => context.Description ?? string.Empty,
                "branch" => context.Branch,
                "type" => context.Type,
                _ => match.Value // Keep unknown placeholders as-is
            };
        });

        return result;
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    public async Task EnsureDefaultPromptsAsync()
    {
        var existingPrompts = GetAllPrompts();

        // Create Plan prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name == "Plan"))
        {
            await CreatePromptAsync(
                "Plan",
                GetDefaultPlanMessage(),
                SessionMode.Plan);
        }

        // Create Build prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name == "Build"))
        {
            await CreatePromptAsync(
                "Build",
                GetDefaultBuildMessage(),
                SessionMode.Build);
        }

        // Create Rebase prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name.Equals("Rebase", StringComparison.OrdinalIgnoreCase)))
        {
            await CreatePromptAsync(
                "Rebase",
                GetDefaultRebaseMessage(),
                SessionMode.Build);
        }
    }

    private static string GetDefaultPlanMessage()
    {
        return """
            ## Issue: {{title}}

            **ID:** {{id}}
            **Type:** {{type}}
            **Branch:** {{branch}}

            ### Description
            {{description}}

            ---

            Please analyze this issue and create a detailed implementation plan. Consider:
            - What files need to be modified or created
            - The approach and architecture
            - Any potential challenges or risks
            - Test requirements
            """;
    }

    private static string GetDefaultBuildMessage()
    {
        return """
            ## Issue: {{title}}

            **ID:** {{id}}
            **Type:** {{type}}
            **Branch:** {{branch}}

            ### Description
            {{description}}

            ---

            Please implement this issue. Start by understanding the requirements and exploring the codebase, then proceed with the implementation.
            """;
    }

    private static string GetDefaultRebaseMessage()
    {
        return """
            ## Rebase Request

            Please rebase branch `{{branch}}` onto the latest default branch.

            Follow the workflow in your system prompt:
            1. Fetch the latest changes
            2. Analyze the commits to be rebased
            3. Perform the rebase
            4. Resolve any conflicts using the context provided
            5. Run tests to verify no regressions
            6. Push with --force-with-lease when ready
            """;
    }
}
