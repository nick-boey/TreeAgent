using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Features.Testing;

/// <summary>
/// Extension methods for registering mock services.
/// </summary>
public static class MockServiceExtensions
{
    /// <summary>
    /// Adds all mock services to the service collection.
    /// Call this instead of registering production services when mock mode is enabled.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Mock mode configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMockServices(
        this IServiceCollection services,
        MockModeOptions options)
    {
        // Register the mock data store as both concrete and interface type
        services.AddSingleton<MockDataStore>();
        services.AddSingleton<IDataStore>(sp => sp.GetRequiredService<MockDataStore>());

        // Register mock Fleece service (needs to be accessible by transition service)
        services.AddSingleton<MockFleeceService>();
        services.AddSingleton<IFleeceService>(sp => sp.GetRequiredService<MockFleeceService>());

        // Core services
        services.AddSingleton<ICommandRunner, MockCommandRunner>();
        services.AddSingleton<IGitHubEnvironmentService, MockGitHubEnvironmentService>();
        services.AddSingleton<IGitHubClientWrapper, MockGitHubClientWrapper>();

        // GitHub services
        services.AddScoped<IGitHubService, MockGitHubService>();
        services.AddScoped<IIssuePrLinkingService, MockIssuePrLinkingService>();

        // Project service
        services.AddScoped<IProjectService, MockProjectService>();

        // Fleece services (transition service depends on MockFleeceService)
        services.AddScoped<IFleeceIssueTransitionService, MockFleeceIssueTransitionService>();
        services.AddSingleton<IFleeceIssuesSyncService, MockFleeceIssuesSyncService>();

        // Git services
        services.AddSingleton<IGitWorktreeService, MockGitWorktreeService>();

        // Claude Code services - use the real session store (already in-memory)
        services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();
        services.AddSingleton<IClaudeSessionService, MockClaudeSessionService>();
        services.AddSingleton<IRebaseAgentService, MockRebaseAgentService>();
        services.AddSingleton<IAgentPromptService, MockAgentPromptService>();

        // Graph service
        services.AddScoped<IGraphService, MockGraphService>();

        // Seed data service (if enabled)
        if (options.SeedData)
        {
            services.AddHostedService<MockDataSeederService>();
        }

        return services;
    }
}
