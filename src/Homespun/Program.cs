using Homespun.Features.Agents;
using Homespun.Features.Agents.Abstractions;
using Homespun.Features.Agents.Hubs;
using Homespun.Features.Agents.Services;
using Homespun.Features.Beads.Services;
using Homespun.Features.ClaudeCodeUI;
using Homespun.Features.ClaudeCodeUI.Models;
using Homespun.Features.ClaudeCodeUI.Services;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Models;
using OpenCodeServices = Homespun.Features.OpenCode.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Resolve data path from configuration or use default
var homespunDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun");
var defaultDataPath = Path.Combine(homespunDir, "homespun-data.json");
var dataPath = builder.Configuration["HOMESPUN_DATA_PATH"] ?? defaultDataPath;

// Ensure the data directory exists
var dataDirectory = Path.GetDirectoryName(dataPath);
if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

// Register JSON data store as singleton
builder.Services.AddSingleton<IDataStore>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
    return new JsonDataStore(dataPath, logger);
});

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

// Core services
builder.Services.AddScoped<ProjectService>();
builder.Services.AddSingleton<IGitHubEnvironmentService, GitHubEnvironmentService>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
builder.Services.AddScoped<PullRequestDataService>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<PullRequestWorkflowService>();

// Beads services (CLI-based - kept as fallback)
builder.Services.AddScoped<IBeadsService, BeadsService>();
builder.Services.AddScoped<IBeadsInitializer, BeadsInitializer>();
builder.Services.AddScoped<IBeadsIssueTransitionService, BeadsIssueTransitionService>();

// Beads direct database access services (high-performance)
builder.Services.Configure<BeadsDatabaseOptions>(
    builder.Configuration.GetSection(BeadsDatabaseOptions.SectionName));
builder.Services.AddSingleton<IBeadsQueueService, BeadsQueueService>();
builder.Services.AddSingleton<IBeadsDatabaseService, BeadsDatabaseService>();
builder.Services.AddHostedService<BeadsQueueProcessorService>();

// Gitgraph services
builder.Services.AddScoped<IGraphService, GraphService>();

// Issue-PR linking service (must be registered before GitHubService as it depends on it)
builder.Services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();

// Notification services
builder.Services.AddSingleton<INotificationService, NotificationService>();

// Agent harness abstractions
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

// OpenCode harness services
builder.Services.Configure<OpenCodeOptions>(
    builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.Configure<AgentCompletionOptions>(
    builder.Configuration.GetSection("AgentCompletion"));
builder.Services.AddHttpClient<OpenCodeServices.IOpenCodeClient, OpenCodeServices.OpenCodeClient>();
builder.Services.AddSingleton<OpenCodeServices.IPortAllocationService, OpenCodeServices.PortAllocationService>();
builder.Services.AddSingleton<OpenCodeServices.IOpenCodeServerManager, OpenCodeServices.OpenCodeServerManager>();
builder.Services.AddSingleton<OpenCodeServices.IOpenCodeConfigGenerator, OpenCodeServices.OpenCodeConfigGenerator>();
builder.Services.AddSingleton<OpenCodeServices.IAgentStartupTracker, OpenCodeServices.AgentStartupTracker>();
builder.Services.AddHostedService<OpenCodeServices.AgentStartupBroadcaster>();
builder.Services.AddScoped<OpenCodeServices.IAgentCompletionMonitor, OpenCodeServices.AgentCompletionMonitor>();
builder.Services.AddSingleton<OpenCodeServices.ITestAgentService, OpenCodeServices.TestAgentService>();
builder.Services.AddSingleton<OpenCodeHarness>();
builder.Services.AddSingleton<IAgentHarness>(sp => sp.GetRequiredService<OpenCodeHarness>());

// Claude Code UI harness services
builder.Services.Configure<ClaudeCodeUIOptions>(
    builder.Configuration.GetSection(ClaudeCodeUIOptions.SectionName));
builder.Services.AddHttpClient<IClaudeCodeUIClient, ClaudeCodeUIClient>();
builder.Services.AddSingleton<ClaudeCodeUIServerManager>();
builder.Services.AddSingleton<ClaudeCodeUIHarness>();
builder.Services.AddSingleton<IAgentHarness>(sp => sp.GetRequiredService<ClaudeCodeUIHarness>());

// Harness factory and workflow service
builder.Services.AddSingleton<IAgentHarnessFactory, AgentHarnessFactory>();
builder.Services.AddScoped<IAgentWorkflowService, Homespun.Features.Agents.Services.AgentWorkflowService>();

// GitHub sync polling service (PR sync, review polling, issue linking)
builder.Services.Configure<GitHubSyncPollingOptions>(
    builder.Configuration.GetSection(GitHubSyncPollingOptions.SectionName));
builder.Services.AddHostedService<GitHubSyncPollingService>();

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure external hostname for agent URLs (for container/Tailscale mode)
// Check multiple sources in priority order with detailed logging
var configExternalHostname = app.Configuration["OpenCode:ExternalHostname"];
var configHspHostname = app.Configuration["HSP_EXTERNAL_HOSTNAME"];
var envHspHostname = Environment.GetEnvironmentVariable("HSP_EXTERNAL_HOSTNAME");

// Log all hostname-related environment variables for debugging
var envVars = Environment.GetEnvironmentVariables();
var hostnameEnvVars = new List<string>();
foreach (var key in envVars.Keys)
{
    var keyStr = key.ToString() ?? "";
    if (keyStr.Contains("HOSTNAME", StringComparison.OrdinalIgnoreCase) ||
        keyStr.StartsWith("HSP_", StringComparison.OrdinalIgnoreCase) ||
        keyStr.Contains("OPENCODE", StringComparison.OrdinalIgnoreCase))
    {
        hostnameEnvVars.Add($"{keyStr}={envVars[key]}");
    }
}
app.Logger.LogInformation(
    "Environment variables (hostname/HSP/OpenCode related): {EnvVars}",
    hostnameEnvVars.Count > 0 ? string.Join(", ", hostnameEnvVars) : "(none found)");

app.Logger.LogInformation(
    "External hostname resolution - OpenCode:ExternalHostname={ConfigHostname}, HSP_EXTERNAL_HOSTNAME(config)={ConfigHsp}, HSP_EXTERNAL_HOSTNAME(env)={EnvHsp}",
    configExternalHostname ?? "(null)",
    configHspHostname ?? "(null)",
    envHspHostname ?? "(null)");

// Priority: OpenCode:ExternalHostname > HSP_EXTERNAL_HOSTNAME (config) > HSP_EXTERNAL_HOSTNAME (env)
var externalHostname = configExternalHostname;
if (string.IsNullOrEmpty(externalHostname))
{
    externalHostname = configHspHostname;
    if (string.IsNullOrEmpty(externalHostname))
    {
        externalHostname = envHspHostname;
    }
}

if (!string.IsNullOrEmpty(externalHostname))
{
    OpenCodeServer.ExternalHostname = externalHostname;
    ClaudeCodeUIServer.ExternalHostname = externalHostname;
    app.Logger.LogInformation("External hostname configured for agent URLs: {Hostname}", externalHostname);
}
else
{
    app.Logger.LogWarning("No external hostname configured - agent URLs will use localhost. Set HSP_EXTERNAL_HOSTNAME or OpenCode:ExternalHostname to configure.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Note: HTTPS redirection removed - container runs HTTP-only behind a reverse proxy

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<Homespun.Features.Agents.Hubs.AgentHub>("/hubs/agent");
app.MapHub<NotificationHub>("/hubs/notifications");

// Map health check endpoint
app.MapHealthChecks("/health");

// Parse CLI arguments for test agent auto-start
var startTestAgentProject = args
    .FirstOrDefault(a => a.StartsWith("--start-test-agent="))
    ?.Split('=', 2).ElementAtOrDefault(1);

if (!string.IsNullOrEmpty(startTestAgentProject))
{
    app.Logger.LogInformation("Test agent auto-start requested for project: {ProjectId}", startTestAgentProject);

    // Start the test agent in a background task after the app is ready
    _ = Task.Run(async () =>
    {
        // Wait for the application to be fully ready
        await Task.Delay(3000);

        try
        {
            using var scope = app.Services.CreateScope();
            var testAgentService = scope.ServiceProvider.GetRequiredService<OpenCodeServices.ITestAgentService>();

            app.Logger.LogInformation("Starting test agent for project: {ProjectId}", startTestAgentProject);
            var result = await testAgentService.StartTestAgentAsync(startTestAgentProject);

            if (result.Success)
            {
                app.Logger.LogInformation(
                    "Test agent started successfully. ServerUrl: {ServerUrl}, WorktreePath: {WorktreePath}",
                    result.ServerUrl, result.WorktreePath);
            }
            else
            {
                app.Logger.LogError("Test agent failed to start: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Exception starting test agent for project: {ProjectId}", startTestAgentProject);
        }
    });
}

app.Run();