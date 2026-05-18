using Fleece.Core.Models;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;

namespace Homespun.Features.Testing;

/// <summary>
/// Hosted service that seeds demo data on application startup when mock mode is enabled.
/// Uses the temporary data folder service for file-based storage.
/// </summary>
public class MockDataSeederService : IHostedService
{
    private readonly IDataStore _dataStore;
    private readonly ITempDataFolderService _tempFolderService;
    private readonly FleeceIssueSeeder _fleeceIssueSeeder;
    private readonly OpenSpecMockSeeder _openSpecMockSeeder;
    private readonly IGitCloneService _gitCloneService;
    private readonly ILogger<MockDataSeederService> _logger;

    /// <summary>
    /// Single source of truth mapping each seeded PR's <see cref="PullRequest.BranchName"/>
    /// (including the <c>+{issueId}</c> suffix required by <see cref="BranchNameParser"/>)
    /// to the Fleece issue id that owns the branch. Drives both <see cref="PullRequest.FleeceIssueId"/>
    /// assignment in <see cref="SeedPullRequestsAsync"/> and the per-branch scenario selection
    /// in <see cref="OpenSpecMockSeeder.SeedBranchAsync"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BranchToFleeceId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["feature/user-auth+ISSUE-001"] = "ISSUE-001",
            ["feature/dark-mode+ISSUE-002"] = "ISSUE-002",
            ["feature/api-v2+ISSUE-006"] = "ISSUE-006",
            ["feature/logging+ISSUE-003"] = "ISSUE-003",
            ["refactor/database-layer+ISSUE-014"] = "ISSUE-014",
        };

    public MockDataSeederService(
        IDataStore dataStore,
        ITempDataFolderService tempFolderService,
        FleeceIssueSeeder fleeceIssueSeeder,
        OpenSpecMockSeeder openSpecMockSeeder,
        IGitCloneService gitCloneService,
        ILogger<MockDataSeederService> logger)
    {
        _dataStore = dataStore;
        _tempFolderService = tempFolderService;
        _fleeceIssueSeeder = fleeceIssueSeeder;
        _openSpecMockSeeder = openSpecMockSeeder;
        _gitCloneService = gitCloneService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding mock data for demo mode...");

        try
        {
            await SeedUserSettingsAsync();
            await SeedProjectsAsync();
            await SeedPullRequestsAsync();
            await SeedIssuesAsync();
            await SeedOpenSpecAsync(cancellationToken);
            await SeedClonesAsync();
            // Agent prompts are now filesystem skills (auto-discovered, no seeding).
            // Session seeding from legacy JSONL ClaudeMessage fixtures was removed along
            // with the JsonlSessionLoader / MessageCacheStore pipeline. Mock mode now
            // relies on the A2A event stream for any session content.

            // Initialize git repos in all project folders after all files are seeded
            InitializeGitRepositories();

            _logger.LogInformation("Mock data seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed mock data");
        }
    }

    private async Task SeedOpenSpecAsync(CancellationToken ct)
    {
        var demoProjectPath = _tempFolderService.GetProjectPath("demo-project");
        await _openSpecMockSeeder.SeedAsync(demoProjectPath, BranchToFleeceId, ct);
        _logger.LogDebug("Seeded openspec content to {ProjectPath}", demoProjectPath);
    }

    /// <summary>
    /// Pre-creates a clone for each seeded PR's branch so the per-branch openspec deltas
    /// are present on disk before the first <c>/taskgraph/data</c> request fires. Each
    /// <see cref="MockGitCloneService.CreateCloneAsync"/> invocation copies the main
    /// project's <c>openspec/</c> + <c>.fleece/</c> trees into the clone and asks the
    /// seeder to apply branch-specific deltas. No-op in live-Claude mode (the clone
    /// service short-circuits to the shared workspace there).
    /// </summary>
    private async Task SeedClonesAsync()
    {
        var demoProjectPath = _tempFolderService.GetProjectPath("demo-project");
        foreach (var branch in BranchToFleeceId.Keys)
        {
            await _gitCloneService.CreateCloneAsync(demoProjectPath, branch);
        }
        _logger.LogDebug("Seeded {Count} clone directories for demo PRs", BranchToFleeceId.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Cleanup is handled by TempDataFolderService.Dispose()
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes git repositories in all project directories under the temp folder.
    /// Must be called after all files are seeded so the initial commit includes everything.
    /// </summary>
    private void InitializeGitRepositories()
    {
        var projectsDir = Path.Combine(_tempFolderService.RootPath, "projects");
        if (!Directory.Exists(projectsDir))
            return;

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            _fleeceIssueSeeder.InitializeGitRepository(projectDir);
        }
    }

    /// <summary>
    /// Gets the local path for a mock project using the temp folder service.
    /// Creates the project directory with minimal structure if it doesn't exist.
    /// </summary>
    private string GetMockProjectPath(string projectId, string projectName)
    {
        var projectPath = _tempFolderService.GetProjectPath(projectId);

        // Create project structure if it doesn't exist
        if (!Directory.Exists(projectPath))
        {
            _fleeceIssueSeeder.CreateMinimalProjectStructure(projectPath, projectName);
        }

        return projectPath;
    }

    private async Task SeedUserSettingsAsync()
    {
        await _dataStore.SetUserEmailAsync("demo@example.com");
        _logger.LogDebug("Seeded user email: demo@example.com");
    }

    private async Task SeedProjectsAsync()
    {
        var now = DateTime.UtcNow;

        // Demo Project 1: Main demo project (most recently updated - appears first)
        var demoProject = new Project
        {
            Id = "demo-project",
            Name = "Demo Project",
            LocalPath = GetMockProjectPath("demo-project", "Demo Project"),
            GitHubOwner = "demo-org",
            GitHubRepo = "demo-project",
            DefaultBranch = "main",
            DefaultModel = "sonnet",
            UpdatedAt = now
        };
        await _dataStore.AddProjectAsync(demoProject);
        _logger.LogDebug("Seeded demo project: {ProjectName} at {Path}", demoProject.Name, demoProject.LocalPath);
    }

    private async Task SeedPullRequestsAsync()
    {
        var now = DateTime.UtcNow;

        // PR 1: In Development — inherited-change scenario
        var pr1 = new PullRequest
        {
            Id = "pr-feature-auth",
            ProjectId = "demo-project",
            Title = "Add user authentication",
            Description = "Implement JWT-based authentication for the API",
            BranchName = "feature/user-auth+ISSUE-001",
            FleeceIssueId = BranchToFleeceId["feature/user-auth+ISSUE-001"],
            Status = OpenPullRequestStatus.InDevelopment,
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddHours(-2)
        };
        await _dataStore.AddPullRequestAsync(pr1);

        // PR 2: Ready for Review — multi-orphan scenario
        var pr2 = new PullRequest
        {
            Id = "pr-dark-mode",
            ProjectId = "demo-project",
            Title = "Implement dark mode",
            Description = "Add dark mode support with theme switching",
            BranchName = "feature/dark-mode+ISSUE-002",
            FleeceIssueId = BranchToFleeceId["feature/dark-mode+ISSUE-002"],
            Status = OpenPullRequestStatus.ReadyForReview,
            GitHubPRNumber = 42,
            CreatedAt = now.AddDays(-5),
            UpdatedAt = now.AddDays(-1)
        };
        await _dataStore.AddPullRequestAsync(pr2);

        // PR 3: Approved — in-progress linked change scenario
        var pr3 = new PullRequest
        {
            Id = "pr-api-v2",
            ProjectId = "demo-project",
            Title = "API v2 endpoints",
            Description = "New versioned API endpoints with improved response format",
            BranchName = "feature/api-v2+ISSUE-006",
            FleeceIssueId = BranchToFleeceId["feature/api-v2+ISSUE-006"],
            Status = OpenPullRequestStatus.Approved,
            GitHubPRNumber = 45,
            CreatedAt = now.AddDays(-7),
            UpdatedAt = now.AddHours(-6)
        };
        await _dataStore.AddPullRequestAsync(pr3);

        // PR 4: Has review comments — no-change branch scenario
        var pr4 = new PullRequest
        {
            Id = "pr-logging",
            ProjectId = "demo-project",
            Title = "Improve logging infrastructure",
            Description = "Add structured logging with correlation IDs",
            BranchName = "feature/logging+ISSUE-003",
            FleeceIssueId = BranchToFleeceId["feature/logging+ISSUE-003"],
            Status = OpenPullRequestStatus.HasReviewComments,
            GitHubPRNumber = 38,
            CreatedAt = now.AddDays(-14),
            UpdatedAt = now.AddDays(-10)
        };
        await _dataStore.AddPullRequestAsync(pr4);

        // PR 5: In development — default-case branch (no branch-specific deltas)
        var pr5 = new PullRequest
        {
            Id = "pr-refactor-db",
            ProjectId = "demo-project",
            Title = "Refactor database layer",
            Description = "Migrate to repository pattern",
            BranchName = "refactor/database-layer+ISSUE-014",
            FleeceIssueId = BranchToFleeceId["refactor/database-layer+ISSUE-014"],
            Status = OpenPullRequestStatus.InDevelopment,
            GitHubPRNumber = 41,
            CreatedAt = now.AddDays(-6),
            UpdatedAt = now.AddDays(-2)
        };
        await _dataStore.AddPullRequestAsync(pr5);

        _logger.LogDebug("Seeded {Count} pull requests", 5);
    }

    private async Task SeedIssuesAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Issues for Demo Project - realistic project with diverse statuses, types, and hierarchy
        // Note: Issue has init-only properties, so we use object initializers
        var demoIssues = new List<Issue>
        {
            // ── Standalone issues (no parents) ──────────────────────────────

            // Completed bug fix
            new()
            {
                Id = "ISSUE-001",
                Title = "Fix session token refresh race condition",
                Description = "Users intermittently get 401 errors when their JWT expires during concurrent API calls. " +
                    "The token refresh logic doesn't queue pending requests while a refresh is in flight, causing some " +
                    "requests to use the stale token. Need to implement a request queue that holds outgoing calls until " +
                    "the refresh completes, then retries them with the new token.",
                Type = IssueType.Bug,
                Status = IssueStatus.Complete,
                Priority = 1,
                Tags = ["auth", "critical"],
                CreatedAt = now.AddDays(-21),
                LastUpdate = now.AddDays(-14)
            },

            // Feature in review
            new()
            {
                Id = "ISSUE-002",
                Title = "Add dark mode support with system preference detection",
                Description = "Implement a dark mode theme that respects the user's OS-level preference via " +
                    "prefers-color-scheme media query. Include a manual toggle in the settings panel that overrides " +
                    "the system default. Store the user's preference in localStorage and sync it to their profile " +
                    "settings on the server. Use CSS custom properties for theme tokens to minimize style duplication.",
                Type = IssueType.Feature,
                Status = IssueStatus.Review,
                Priority = 2,
                Tags = ["ui", "accessibility"],
                CreatedAt = now.AddDays(-14),
                LastUpdate = now.AddDays(-2)
            },

            // Bug in progress
            new()
            {
                Id = "ISSUE-003",
                Title = "WebSocket reconnection drops queued messages",
                Description = "When the SignalR connection drops and reconnects, any messages sent during the " +
                    "disconnection window are silently lost. The client needs a local outbox queue that persists " +
                    "pending messages and replays them after reconnection. Also need server-side idempotency keys " +
                    "to prevent duplicate processing if a message was partially delivered before the drop.",
                Type = IssueType.Bug,
                Status = IssueStatus.Progress,
                Priority = 1,
                Tags = ["signalr", "reliability"],
                CreatedAt = now.AddDays(-7),
                LastUpdate = now.AddHours(-6)
            },

            // Chore - completed
            new()
            {
                Id = "ISSUE-014",
                Title = "Upgrade to .NET 9 and update all NuGet packages",
                Description = "Migrate the solution from .NET 8 to .NET 9 LTS. Update all NuGet packages to their " +
                    "latest compatible versions. Run the full test suite after each major package update to catch " +
                    "breaking changes early. Update the Dockerfile base images and CI pipeline to use the .NET 9 SDK.",
                Type = IssueType.Chore,
                Status = IssueStatus.Complete,
                Priority = 3,
                Tags = ["infrastructure", "maintenance", "openspec=2026-01-15-old-feature"],
                CreatedAt = now.AddDays(-30),
                LastUpdate = now.AddDays(-18)
            },

            // ── API v2 epic (parent/child hierarchy, series execution) ──────

            // Parent: API v2 migration (verify issue)
            new()
            {
                Id = "ISSUE-004",
                Title = "API v2 migration",
                Description = "Migrate all public API endpoints from v1 to v2 with improved response envelope, " +
                    "consistent error codes, cursor-based pagination, and OpenAPI 3.1 spec generation. The v1 " +
                    "endpoints should remain available but marked as deprecated with sunset headers.",
                Type = IssueType.Verify,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "migration"],
                ExecutionMode = ExecutionMode.Series,
                CreatedAt = now.AddDays(-20),
                LastUpdate = now.AddDays(-1)
            },

            // Child 1: Design API schema (completed)
            new()
            {
                Id = "ISSUE-005",
                Title = "Design v2 API response envelope and error format",
                Description = "Define the standard JSON response wrapper for all v2 endpoints:\n" +
                    "- `data` field for successful responses\n" +
                    "- `error` object with `code`, `message`, and `details` array for failures\n" +
                    "- `meta` object with pagination cursors and rate limit info\n\n" +
                    "Document the error code taxonomy (AUTH_*, VALIDATION_*, RESOURCE_*, RATE_LIMIT_*) and " +
                    "get sign-off from the frontend team before implementation begins.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 2,
                Tags = ["api", "design"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "a" }],
                CreatedAt = now.AddDays(-19),
                LastUpdate = now.AddDays(-12)
            },

            // Child 2: Implement endpoints (parent of sub-tasks, in progress)
            new()
            {
                Id = "ISSUE-006",
                Title = "Implement v2 REST endpoints",
                Description = "Build all v2 endpoints following the approved response envelope. Each endpoint should " +
                    "include request validation with FluentValidation, cursor-based pagination for list endpoints, " +
                    "and ETag support for conditional requests. Map v1 routes to v2 handlers with deprecation warnings.",
                Type = IssueType.Task,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "backend", "openspec=api-v2-design", "openspec=api-v2-impl"],
                ExecutionMode = ExecutionMode.Parallel,
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "b" }],
                CreatedAt = now.AddDays(-12),
                LastUpdate = now.AddDays(-1)
            },

            // Sub-tasks of ISSUE-006
            new()
            {
                Id = "ISSUE-007",
                Title = "Implement v2 projects endpoints",
                Description = "Create GET /api/v2/projects (list with cursor pagination), GET /api/v2/projects/{id}, " +
                    "POST /api/v2/projects, PUT /api/v2/projects/{id}, DELETE /api/v2/projects/{id}. " +
                    "Include ETag headers and If-None-Match support on GET endpoints.",
                Type = IssueType.Task,
                Status = IssueStatus.Complete,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "a" }],
                CreatedAt = now.AddDays(-11),
                LastUpdate = now.AddDays(-5)
            },
            new()
            {
                Id = "ISSUE-008",
                Title = "Implement v2 pull requests endpoints",
                Description = "Create GET /api/v2/projects/{id}/pulls (list with status filter and cursor pagination), " +
                    "GET /api/v2/pulls/{id}, POST /api/v2/pulls (create from branch), PATCH /api/v2/pulls/{id}/status. " +
                    "Include webhook event forwarding for PR status changes.",
                Type = IssueType.Task,
                Status = IssueStatus.Review,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "b" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-2)
            },
            new()
            {
                Id = "ISSUE-009",
                Title = "Implement v2 issues endpoints",
                Description = "Create GET /api/v2/projects/{id}/issues (with tree/flat view toggle, status filter, " +
                    "type filter), GET /api/v2/issues/{id}, PATCH /api/v2/issues/{id} for status transitions and " +
                    "parent assignment. Support bulk status updates via POST /api/v2/issues/bulk-update.",
                Type = IssueType.Task,
                Status = IssueStatus.Progress,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "c" }],
                CreatedAt = now.AddDays(-9),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "ISSUE-010",
                Title = "Implement v2 agent sessions endpoints",
                Description = "Create GET /api/v2/sessions (list with project filter), GET /api/v2/sessions/{id} " +
                    "(with message history), POST /api/v2/sessions (launch new agent), DELETE /api/v2/sessions/{id} " +
                    "(graceful shutdown). Include SSE endpoint for real-time session streaming.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                Tags = ["api", "backend"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "d" }],
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-3)
            },
            new()
            {
                Id = "ISSUE-011",
                Title = "Add request validation middleware for v2",
                Description = "Implement a global validation filter using FluentValidation that automatically validates " +
                    "request DTOs and returns standardized error responses. Register validators via assembly scanning. " +
                    "Add validation for: required fields, string length limits, enum values, pagination cursor format, " +
                    "and cross-field constraints (e.g., date ranges).",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                Tags = ["api", "validation"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-006", SortOrder = "e" }],
                CreatedAt = now.AddDays(-8),
                LastUpdate = now.AddDays(-4)
            },

            // Child 3: Rate limiting (blocked on endpoints)
            new()
            {
                Id = "ISSUE-012",
                Title = "Add rate limiting to v2 API",
                Description = "Implement per-user rate limiting using a sliding window algorithm. Configure limits: " +
                    "100 req/min for read endpoints, 30 req/min for write endpoints. Return 429 with Retry-After " +
                    "header and remaining quota in X-RateLimit-* headers. Use Redis for distributed rate limit state " +
                    "to support horizontal scaling.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                Tags = ["api", "security", "openspec=rate-limiting"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "c" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-5)
            },

            // Child 4: API docs (blocked on endpoints)
            new()
            {
                Id = "ISSUE-013",
                Title = "Generate OpenAPI 3.1 spec and API documentation",
                Description = "Configure Swashbuckle to generate an OpenAPI 3.1 spec for all v2 endpoints. " +
                    "Include XML doc comments as descriptions, request/response examples, and authentication " +
                    "requirements. Set up a Redoc-powered documentation page at /api/docs. Add a CI check that " +
                    "fails if the generated spec has breaking changes compared to the published version.",
                Type = IssueType.Chore,
                Status = IssueStatus.Open,
                Priority = 4,
                Tags = ["api", "documentation"],
                ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "d" }],
                CreatedAt = now.AddDays(-10),
                LastUpdate = now.AddDays(-5)
            },

            // ── Issues for testing keyboard hierarchy creation (E2E tests) ──

            new()
            {
                Id = "e2Prt1",
                Title = "E2E Test: Parent Issue 1",
                Description = "A parent issue for testing keyboard hierarchy controls.",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                ExecutionMode = ExecutionMode.Parallel,
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2Chd1",
                Title = "E2E Test: Child Issue 1",
                Description = "A child issue of parent1 for testing hierarchy.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2Prt1", SortOrder = "0" }],
                CreatedAt = now.AddDays(-3),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2Chd2",
                Title = "E2E Test: Child Issue 2",
                Description = "Another child issue of parent1 for testing hierarchy.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2Prt1", SortOrder = "1" }],
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2Orph",
                Title = "E2E Test: Orphan Issue",
                Description = "An issue with no parent for testing hierarchy creation.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 3,
                CreatedAt = now.AddHours(-12),
                LastUpdate = now.AddHours(-6)
            },
            new()
            {
                Id = "e2SPrt",
                Title = "E2E Test: Series Parent",
                Description = "A parent issue with series execution mode.",
                Type = IssueType.Feature,
                Status = IssueStatus.Open,
                Priority = 2,
                ExecutionMode = ExecutionMode.Series,
                CreatedAt = now.AddDays(-4),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2SCh1",
                Title = "E2E Test: Series Child 1",
                Description = "First child in a series execution parent.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2SPrt", SortOrder = "0" }],
                CreatedAt = now.AddDays(-3),
                LastUpdate = now.AddDays(-1)
            },
            new()
            {
                Id = "e2SCh2",
                Title = "E2E Test: Series Child 2",
                Description = "Second child in a series execution parent.",
                Type = IssueType.Task,
                Status = IssueStatus.Open,
                Priority = 2,
                ParentIssues = [new ParentIssueRef { ParentIssue = "e2SPrt", SortOrder = "1" }],
                CreatedAt = now.AddDays(-2),
                LastUpdate = now.AddDays(-1)
            }
        };

        // Seed issues for demo-project using FleeceIssueSeeder
        var demoProjectPath = _tempFolderService.GetProjectPath("demo-project");
        await _fleeceIssueSeeder.SeedIssuesAsync(demoProjectPath, demoIssues);
        _logger.LogDebug("Seeded {Count} issues to {ProjectPath}", demoIssues.Count, demoProjectPath);

    }

}
