using Homespun.Features.Beads.Services;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.OpenCode.Models;
using Homespun.Features.OpenCode.Services;
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
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
builder.Services.AddScoped<PullRequestDataService>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<PullRequestWorkflowService>();

// Beads services
builder.Services.AddScoped<IBeadsService, BeadsService>();
builder.Services.AddScoped<IBeadsInitializer, BeadsInitializer>();
builder.Services.AddScoped<IBeadsIssueTransitionService, BeadsIssueTransitionService>();

// Gitgraph services
builder.Services.AddScoped<IGraphService, GraphService>();

// Issue-PR linking service (must be registered before GitHubService as it depends on it)
builder.Services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();

// Notification services
builder.Services.AddSingleton<INotificationService, NotificationService>();

// OpenCode services
builder.Services.Configure<OpenCodeOptions>(
    builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.Configure<AgentCompletionOptions>(
    builder.Configuration.GetSection("AgentCompletion"));
builder.Services.AddHttpClient<IOpenCodeClient, OpenCodeClient>();
builder.Services.AddSingleton<IPortAllocationService, PortAllocationService>();
builder.Services.AddSingleton<IOpenCodeServerManager, OpenCodeServerManager>();
builder.Services.AddSingleton<IOpenCodeConfigGenerator, OpenCodeConfigGenerator>();
builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
builder.Services.AddHostedService<AgentStartupBroadcaster>();
builder.Services.AddScoped<IAgentCompletionMonitor, AgentCompletionMonitor>();
builder.Services.AddScoped<IAgentWorkflowService, AgentWorkflowService>();
builder.Services.AddSingleton<ITestAgentService, TestAgentService>();

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
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<NotificationHub>("/hubs/notifications");

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();