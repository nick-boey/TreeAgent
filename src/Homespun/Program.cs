using Homespun.Features.Beads.Services;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Notifications;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Hubs;
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

// Log startup configuration for debugging
Console.WriteLine("=== Homespun Startup Configuration ===");
Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"HOMESPUN_DATA_PATH from config: {builder.Configuration["HOMESPUN_DATA_PATH"] ?? "(not set)"}");
Console.WriteLine($"HOMESPUN_BASE_PATH from config: {builder.Configuration["HOMESPUN_BASE_PATH"] ?? "(not set)"}");
Console.WriteLine($"User profile directory: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");

// Add services to the container.
var homespunDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun");
var defaultDataPath = Path.Combine(homespunDir, "homespun-data.json");
var dataPath = builder.Configuration["HOMESPUN_DATA_PATH"] ?? defaultDataPath;

Console.WriteLine($"Data path (resolved): {dataPath}");

// Ensure the data directory exists
var dataDirectory = Path.GetDirectoryName(dataPath);
Console.WriteLine($"Data directory: {dataDirectory}");

if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
{
    Console.WriteLine($"Creating data directory: {dataDirectory}");
    Directory.CreateDirectory(dataDirectory);
}
else
{
    Console.WriteLine($"Data directory exists: {Directory.Exists(dataDirectory)}");
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
Console.WriteLine($"Data Protection keys path: {dataProtectionKeysPath}");

if (!Directory.Exists(dataProtectionKeysPath))
{
    Console.WriteLine($"Creating Data Protection keys directory: {dataProtectionKeysPath}");
    Directory.CreateDirectory(dataProtectionKeysPath);
}
else
{
    Console.WriteLine($"Data Protection keys directory exists: {Directory.Exists(dataProtectionKeysPath)}");
    // List existing keys
    try
    {
        var keyFiles = Directory.GetFiles(dataProtectionKeysPath, "*.xml");
        Console.WriteLine($"Existing key files: {keyFiles.Length}");
        foreach (var keyFile in keyFiles)
        {
            Console.WriteLine($"  - {Path.GetFileName(keyFile)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error listing key files: {ex.Message}");
    }
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Homespun");

Console.WriteLine("=== End Startup Configuration ===");

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
#if DEBUG
builder.Services.AddSingleton<ITestAgentService, TestAgentService>();
#endif

// GitHub sync polling service (PR sync, review polling, issue linking)
builder.Services.Configure<GitHubSyncPollingOptions>(
    builder.Configuration.GetSection(GitHubSyncPollingOptions.SectionName));
builder.Services.AddHostedService<GitHubSyncPollingService>();

builder.Services.AddSignalR();
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
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Note: HTTPS redirection removed - container runs HTTP-only behind a reverse proxy

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
