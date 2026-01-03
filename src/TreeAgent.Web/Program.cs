using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Components;
using TreeAgent.Web.Features.Agents.Hubs;
using TreeAgent.Web.Features.Agents.Services;
using TreeAgent.Web.Features.Commands;
using TreeAgent.Web.Features.Git;
using TreeAgent.Web.Features.GitHub;
using TreeAgent.Web.Features.Projects;
using TreeAgent.Web.Features.PullRequests;
using TreeAgent.Web.Features.PullRequests.Data;
using TreeAgent.Web.Features.Roadmap;
using TreeAgent.Web.HealthChecks;
using SystemPromptService = TreeAgent.Web.Features.Agents.SystemPromptService;

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

builder.Services.AddScoped<ProjectService>();
builder.Services.AddSingleton<IClaudeCodeProcessFactory, ClaudeCodeProcessFactory>();
builder.Services.AddSingleton<ClaudeCodeProcessManager>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
builder.Services.AddScoped<PullRequestDataService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<PullRequestWorkflowService>();
builder.Services.AddScoped<IRoadmapService, RoadmapService>();
builder.Services.AddScoped<SystemPromptService>();
builder.Services.AddSingleton<IAgentHubNotifier, AgentHubNotifier>();

builder.Services.AddSignalR();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<ProcessManagerHealthCheck>("process_manager");
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
app.MapHub<AgentHub>("/hubs/agent");
app.MapHealthChecks("/health");

app.Run();
