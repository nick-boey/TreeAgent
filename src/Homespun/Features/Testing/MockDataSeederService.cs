using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing;

/// <summary>
/// Hosted service that seeds demo data on application startup when mock mode is enabled.
/// </summary>
public class MockDataSeederService : IHostedService
{
    private readonly MockDataStore _dataStore;
    private readonly MockFleeceService _fleeceService;
    private readonly IAgentPromptService _agentPromptService;
    private readonly ILogger<MockDataSeederService> _logger;

    public MockDataSeederService(
        MockDataStore dataStore,
        MockFleeceService fleeceService,
        IAgentPromptService agentPromptService,
        ILogger<MockDataSeederService> logger)
    {
        _dataStore = dataStore;
        _fleeceService = fleeceService;
        _agentPromptService = agentPromptService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding mock data for demo mode...");

        try
        {
            await SeedProjectsAsync();
            await SeedPullRequestsAsync();
            await SeedIssuesAsync();
            await SeedAgentPromptsAsync();

            _logger.LogInformation("Mock data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed mock data");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task SeedProjectsAsync()
    {
        // Demo Project 1: Main demo project
        var demoProject = new Project
        {
            Id = "demo-project",
            Name = "Demo Project",
            LocalPath = "/mock/projects/demo-project",
            GitHubOwner = "demo-org",
            GitHubRepo = "demo-project",
            DefaultBranch = "main",
            DefaultModel = "sonnet"
        };
        await _dataStore.AddProjectAsync(demoProject);
        _logger.LogDebug("Seeded demo project: {ProjectName}", demoProject.Name);

        // Demo Project 2: A sample app
        var sampleApp = new Project
        {
            Id = "sample-app",
            Name = "Sample Application",
            LocalPath = "/mock/projects/sample-app",
            GitHubOwner = "demo-org",
            GitHubRepo = "sample-app",
            DefaultBranch = "main",
            DefaultModel = "sonnet"
        };
        await _dataStore.AddProjectAsync(sampleApp);
        _logger.LogDebug("Seeded sample app project: {ProjectName}", sampleApp.Name);
    }

    private async Task SeedPullRequestsAsync()
    {
        var now = DateTime.UtcNow;

        // PR 1: In Development
        var pr1 = new PullRequest
        {
            Id = "pr-feature-auth",
            ProjectId = "demo-project",
            Title = "Add user authentication",
            Description = "Implement JWT-based authentication for the API",
            BranchName = "feature/user-auth",
            Status = OpenPullRequestStatus.InDevelopment,
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddHours(-2)
        };
        await _dataStore.AddPullRequestAsync(pr1);

        // PR 2: Ready for Review (has GitHub PR number)
        var pr2 = new PullRequest
        {
            Id = "pr-dark-mode",
            ProjectId = "demo-project",
            Title = "Implement dark mode",
            Description = "Add dark mode support with theme switching",
            BranchName = "feature/dark-mode",
            Status = OpenPullRequestStatus.ReadyForReview,
            GitHubPRNumber = 42,
            CreatedAt = now.AddDays(-5),
            UpdatedAt = now.AddDays(-1)
        };
        await _dataStore.AddPullRequestAsync(pr2);

        // PR 3: Approved
        var pr3 = new PullRequest
        {
            Id = "pr-api-v2",
            ProjectId = "demo-project",
            Title = "API v2 endpoints",
            Description = "New versioned API endpoints with improved response format",
            BranchName = "feature/api-v2",
            Status = OpenPullRequestStatus.Approved,
            GitHubPRNumber = 45,
            CreatedAt = now.AddDays(-7),
            UpdatedAt = now.AddHours(-6)
        };
        await _dataStore.AddPullRequestAsync(pr3);

        // PR 4: Has review comments
        var pr4 = new PullRequest
        {
            Id = "pr-logging",
            ProjectId = "demo-project",
            Title = "Improve logging infrastructure",
            Description = "Add structured logging with correlation IDs",
            BranchName = "feature/logging",
            Status = OpenPullRequestStatus.HasReviewComments,
            GitHubPRNumber = 38,
            CreatedAt = now.AddDays(-14),
            UpdatedAt = now.AddDays(-10)
        };
        await _dataStore.AddPullRequestAsync(pr4);

        // PR 5: In development (refactor work)
        var pr5 = new PullRequest
        {
            Id = "pr-refactor-db",
            ProjectId = "demo-project",
            Title = "Refactor database layer",
            Description = "Migrate to repository pattern",
            BranchName = "refactor/database-layer",
            Status = OpenPullRequestStatus.InDevelopment,
            GitHubPRNumber = 41,
            CreatedAt = now.AddDays(-6),
            UpdatedAt = now.AddDays(-2)
        };
        await _dataStore.AddPullRequestAsync(pr5);

        // Sample App PRs
        var pr6 = new PullRequest
        {
            Id = "pr-sample-feature",
            ProjectId = "sample-app",
            Title = "Add sample feature",
            Description = "A sample feature for demonstration",
            BranchName = "feature/sample",
            Status = OpenPullRequestStatus.ReadyForReview,
            GitHubPRNumber = 5,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddHours(-5)
        };
        await _dataStore.AddPullRequestAsync(pr6);

        _logger.LogDebug("Seeded {Count} pull requests", 6);
    }

    private Task SeedIssuesAsync()
    {
        var now = DateTime.UtcNow;

        // Issues for Demo Project
        // Note: Issue has init-only properties, so we use object initializers
        var issues = new List<Issue>
        {
            new()
            {
                Id = "task/abc123",
                Title = "Create service mocks",
                Description = "Create mocks for all services with fake data sets to enable testing without production data.",
                Type = IssueType.Task,
                Status = IssueStatus.Next,
                Priority = 2,
                Group = "infrastructure",
                CreatedAt = now.AddDays(-5),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "feat/def456",
                Title = "Add dashboard analytics",
                Description = "Implement analytics dashboard with charts showing project metrics.",
                Type = IssueType.Feature,
                Status = IssueStatus.Idea,
                Priority = 3,
                Group = "features",
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-3)
            },
            new()
            {
                Id = "bug/ghi789",
                Title = "Fix navigation breadcrumb",
                Description = "Breadcrumb shows incorrect path when navigating from issue to PR.",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                Group = "ui",
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddHours(-12)
            },
            new()
            {
                Id = "task/jkl012",
                Title = "Update dependencies",
                Description = "Update all npm and NuGet dependencies to latest stable versions.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 4,
                Group = "maintenance",
                CreatedAt = now.AddDays(-15),
                LastUpdate = now.AddDays(-8)
            },
            new()
            {
                Id = "task/mno345",
                Title = "Simplify state management",
                Description = "Refactor component state to use simpler patterns.",
                Type = IssueType.Task,
                Status = IssueStatus.Spec,
                Priority = 3,
                Group = "technical-debt",
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddDays(-4)
            }
        };

        foreach (var issue in issues)
        {
            _fleeceService.SeedIssue("/mock/projects/demo-project", issue);
        }

        _logger.LogDebug("Seeded {Count} issues", issues.Count);
        return Task.CompletedTask;
    }

    private async Task SeedAgentPromptsAsync()
    {
        // Ensure default prompts exist (Plan and Build)
        await _agentPromptService.EnsureDefaultPromptsAsync();

        // Add a custom prompt
        var customPrompt = new AgentPrompt
        {
            Id = "review",
            Name = "Code Review",
            InitialMessage = """
                ## Code Review: {{title}}

                **Branch:** {{branch}}

                Please review this code change and provide feedback on:
                - Code quality and best practices
                - Potential bugs or edge cases
                - Performance considerations
                - Test coverage
                """,
            Mode = SessionMode.Plan,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(customPrompt);
        _logger.LogDebug("Seeded custom agent prompt: {PromptName}", customPrompt.Name);
    }
}
