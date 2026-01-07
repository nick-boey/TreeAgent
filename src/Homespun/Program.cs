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

var builder = WebApplication.CreateBuilder(args);

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
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
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hubs
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
