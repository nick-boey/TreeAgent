using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Navigation;
using Homespun.Features.Notifications;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Shared.Services;
using Homespun.Features.SignalR;
using Homespun.Features.Testing;
using Homespun.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Check for mock mode
var mockModeOptions = new MockModeOptions();
builder.Configuration.GetSection(MockModeOptions.SectionName).Bind(mockModeOptions);

// Allow environment variable override
if (Environment.GetEnvironmentVariable("HOMESPUN_MOCK_MODE") == "true")
{
    mockModeOptions.Enabled = true;
}

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Resolve data path from configuration or use default (used by production and for data protection keys)
var homespunDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun");
var defaultDataPath = Path.Combine(homespunDir, "homespun-data.json");
var dataPath = builder.Configuration["HOMESPUN_DATA_PATH"] ?? defaultDataPath;

// Ensure the data directory exists
var dataDirectory = Path.GetDirectoryName(dataPath);
if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

// Configure Data Protection to persist keys in the data directory
// This ensures keys survive container restarts and prevents antiforgery token errors
var dataProtectionKeysPath = Path.Combine(dataDirectory!, "DataProtection-Keys");
if (!Directory.Exists(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Homespun");

// Register services based on mock mode
if (mockModeOptions.Enabled)
{
    // Mock mode - use in-memory mock services
    builder.Services.AddMockServices(mockModeOptions);

    // Services that are shared between mock and production mode
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();
    builder.Services.AddSingleton<INotificationService, NotificationService>();
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
    builder.Services.AddSingleton<SessionOptionsFactory>();
    builder.Services.AddScoped<PullRequestDataService>();
    builder.Services.AddScoped<PullRequestWorkflowService>();
}
else
{
    // Production mode - use real services with external dependencies

    // Register JSON data store as singleton
    builder.Services.AddSingleton<IDataStore>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
        return new JsonDataStore(dataPath, logger);
    });

    // Core services
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddSingleton<IGitHubEnvironmentService, GitHubEnvironmentService>();
    builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
    builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
    builder.Services.AddScoped<PullRequestDataService>();
    builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
    builder.Services.AddScoped<IGitHubService, GitHubService>();
    builder.Services.AddScoped<PullRequestWorkflowService>();

    // Fleece services (file-based issue tracking)
    builder.Services.AddSingleton<IFleeceService, FleeceService>();
    builder.Services.AddScoped<IFleeceIssueTransitionService, FleeceIssueTransitionService>();
    builder.Services.AddSingleton<IFleeceIssuesSyncService, FleeceIssuesSyncService>();

    // Markdown rendering service
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();

    // Issue PR status service (for getting PR status linked to issues)
    builder.Services.AddScoped<IIssuePrStatusService, IssuePrStatusService>();

    // Gitgraph services
    builder.Services.AddScoped<IGraphService, GraphService>();

    // Issue-PR linking service (must be registered before GitHubService as it depends on it)
    builder.Services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();

    // Notification services
    builder.Services.AddSingleton<INotificationService, NotificationService>();

    // Navigation services
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();

    // Claude Code SDK services
    builder.Services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();
    builder.Services.AddSingleton<SessionOptionsFactory>();

    // Session discovery service - reads from Claude's native session storage at ~/.claude/projects/
    builder.Services.AddSingleton<IClaudeSessionDiscovery>(sp =>
    {
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
        return new ClaudeSessionDiscovery(claudeDir, sp.GetRequiredService<ILogger<ClaudeSessionDiscovery>>());
    });

    // Session metadata store - maps Claude sessions to our entities (PR/issue)
    var metadataPath = Path.Combine(homespunDir, "session-metadata.json");
    builder.Services.AddSingleton<ISessionMetadataStore>(sp =>
        new SessionMetadataStore(metadataPath, sp.GetRequiredService<ILogger<SessionMetadataStore>>()));

    builder.Services.AddSingleton<IToolResultParser, ToolResultParser>();
    builder.Services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
    builder.Services.AddSingleton<IAgentPromptService, AgentPromptService>();
    builder.Services.AddSingleton<IRebaseAgentService, RebaseAgentService>();

    // GitHub sync polling service (PR sync, review polling, issue linking)
    builder.Services.Configure<GitHubSyncPollingOptions>(
        builder.Configuration.GetSection(GitHubSyncPollingOptions.SectionName));
    builder.Services.AddHostedService<GitHubSyncPollingService>();
}

// SignalR URL provider (uses internal URL in Docker, localhost in development)
builder.Services.Configure<SignalROptions>(
    builder.Configuration.GetSection(SignalROptions.SectionName));
builder.Services.AddSingleton<ISignalRUrlProvider, SignalRUrlProvider>();

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddControllers(); // Add API controller support

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Enable Swagger in all environments for API testing
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Homespun API v1");
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Note: HTTPS redirection removed - container runs HTTP-only behind a reverse proxy

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ClaudeCodeHub>("/hubs/claudecode");

// Map health check endpoint
app.MapHealthChecks("/health");

// Map API controllers
app.MapControllers();

// Map test agent endpoint for curl-based testing
app.MapGet("/test-agent", async (ILogger<Program> logger) =>
{
    var testDir = Path.Combine(Directory.GetCurrentDirectory(), "test");

    // Create test directory if it doesn't exist
    if (!Directory.Exists(testDir))
    {
        Directory.CreateDirectory(testDir);
        logger.LogInformation("Created test directory: {TestDir}", testDir);
    }

    var timestamp = DateTime.UtcNow.ToString("O");
    var prompt = $"Create a file named test.txt containing the timestamp: {timestamp}";

    // Run claude CLI directly to avoid SDK permission mode bug
    var processStartInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "claude",
        Arguments = $"-p \"{prompt}\" --dangerously-skip-permissions",
        WorkingDirectory = testDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        logger.LogInformation("Starting test agent in {TestDir} with prompt: {Prompt}", testDir, prompt);

        var sessionId = Guid.NewGuid().ToString();

        // Run the agent in the background
        _ = Task.Run(async () =>
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    logger.LogInformation("Test agent completed. Exit code: {ExitCode}", process.ExitCode);
                    if (!string.IsNullOrEmpty(output))
                        logger.LogInformation("Output: {Output}", output);
                    if (!string.IsNullOrEmpty(error))
                        logger.LogWarning("Stderr: {Error}", error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running test agent");
            }
        });

        return Results.Ok(new
        {
            message = "Test agent started",
            sessionId,
            workingDirectory = testDir,
            prompt
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to start test agent");
        return Results.Problem($"Failed to start test agent: {ex.Message}");
    }
});

app.Run();
