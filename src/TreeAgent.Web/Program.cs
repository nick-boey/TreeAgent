using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Components;
using TreeAgent.Web.Data;
using TreeAgent.Web.HealthChecks;
using TreeAgent.Web.Hubs;
using TreeAgent.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure structured logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "json";
});

// Add services to the container.
var dbPath = builder.Configuration["TREEAGENT_DB_PATH"] ?? "treeagent.db";
builder.Services.AddDbContext<TreeAgentDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<ProjectService>();
builder.Services.AddSingleton<IClaudeCodeProcessFactory, ClaudeCodeProcessFactory>();
builder.Services.AddSingleton<ClaudeCodeProcessManager>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<GitWorktreeService>();
builder.Services.AddScoped<FeatureService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
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
