using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.OpenCode;
using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.OpenCode.Services;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Roadmap;
using Homespun.Components;

var builder = WebApplication.CreateBuilder(args);

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
var dataPath = builder.Configuration["HOMESPUN_DATA_PATH"] ?? "homespun-data.json";

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
builder.Services.AddScoped<IRoadmapService, RoadmapService>();

// OpenCode services
builder.Services.Configure<OpenCodeOptions>(
    builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.AddHttpClient<IOpenCodeClient, OpenCodeClient>();
builder.Services.AddSingleton<IOpenCodeServerManager, OpenCodeServerManager>();
builder.Services.AddScoped<IOpenCodeConfigGenerator, OpenCodeConfigGenerator>();
builder.Services.AddScoped<IAgentWorkflowService, AgentWorkflowService>();

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

app.Run();
