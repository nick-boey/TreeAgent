using System.Text.RegularExpressions;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IAgentPromptService using MockDataStore.
/// </summary>
public partial class MockAgentPromptService : IAgentPromptService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockAgentPromptService> _logger;

    public MockAgentPromptService(
        IDataStore dataStore,
        ILogger<MockAgentPromptService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public IReadOnlyList<AgentPrompt> GetAllPrompts()
    {
        _logger.LogDebug("[Mock] GetAllPrompts");
        return _dataStore.AgentPrompts;
    }

    public AgentPrompt? GetPrompt(string id)
    {
        _logger.LogDebug("[Mock] GetPrompt {Id}", id);
        return _dataStore.GetAgentPrompt(id);
    }

    public async Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode)
    {
        _logger.LogDebug("[Mock] CreatePrompt {Name}", name);

        var prompt = new AgentPrompt
        {
            Id = Guid.NewGuid().ToString("N")[..6],
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
        _logger.LogDebug("[Mock] UpdatePrompt {Id}", id);

        var prompt = _dataStore.GetAgentPrompt(id);
        if (prompt == null)
        {
            throw new InvalidOperationException($"Prompt {id} not found");
        }

        prompt.Name = name;
        prompt.InitialMessage = initialMessage;
        prompt.Mode = mode;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _dataStore.UpdateAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(string id)
    {
        _logger.LogDebug("[Mock] DeletePrompt {Id}", id);
        await _dataStore.RemoveAgentPromptAsync(id);
    }

    public string? RenderTemplate(string? template, PromptContext context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;
        result = TemplatePlaceholderRegex().Replace(result, match =>
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

    public async Task EnsureDefaultPromptsAsync()
    {
        _logger.LogDebug("[Mock] EnsureDefaultPromptsAsync");

        var existingPrompts = _dataStore.AgentPrompts;

        // Check if Plan prompt exists
        if (!existingPrompts.Any(p => p.Name == "Plan"))
        {
            await _dataStore.AddAgentPromptAsync(new AgentPrompt
            {
                Id = "plan",
                Name = "Plan",
                InitialMessage = """
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
                    """,
                Mode = SessionMode.Plan,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Check if Build prompt exists
        if (!existingPrompts.Any(p => p.Name == "Build"))
        {
            await _dataStore.AddAgentPromptAsync(new AgentPrompt
            {
                Id = "build",
                Name = "Build",
                InitialMessage = """
                    ## Issue: {{title}}

                    **ID:** {{id}}
                    **Type:** {{type}}
                    **Branch:** {{branch}}

                    ### Description
                    {{description}}

                    ---

                    Please implement this issue. Write the code, create tests as needed, and ensure the implementation is complete and working.
                    """,
                Mode = SessionMode.Build,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TemplatePlaceholderRegex();
}
