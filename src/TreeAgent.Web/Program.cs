using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Components;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.OpenCode;
using TreeAgent.Web.Features.OpenCode.Hubs;
using TreeAgent.Web.Features.OpenCode.Services;
using TreeAgent.Web.Features.Projects;
using TreeAgent.Web.Features.PullRequests;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.Roadmap;

var builder = WebApplication.CreateBuilder(args);

// Configure console logging with readable output
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Enable verbose EF Core SQL logging if TREEAGENT_VERBOSE_SQL is set
var verboseSql = builder.Configuration["TREEAGENT_VERBOSE_SQL"]
    ?? Environment.GetEnvironmentVariable("TREEAGENT_VERBOSE_SQL");
if (!string.IsNullOrEmpty(verboseSql) && verboseSql.Equals("true", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Information);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
}

// Add services to the container.
var dbPath = builder.Configuration["TREEAGENT_DB_PATH"] ?? "treeagent.db";

// Ensure the database directory exists
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<TreeAgentDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

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

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TreeAgentDbContext>();
    db.Database.Migrate();
}

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
