using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Features.Commands;
using Homespun.Features.Containers.Services;
using Fleece.Core.Services.GraphLayout;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Navigation;
using Homespun.Features.Notifications;
using Homespun.Features.Observability;
using Homespun.Features.Observability.HealthChecks;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Plans;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.Search;
using Homespun.Features.Secrets;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Shared;
using Homespun.Features.Shared.Services;
using Homespun.Features.SignalR;
using Homespun.Features.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// ClearProviders MUST run before AddServiceDefaults so the OTLP logger provider
// wired by ServiceDefaults survives to the runtime. Calling ClearProviders after
// AddServiceDefaults wipes the OTLP log pipeline and breaks
// `aspire otel logs server`; traces/metrics still work but logs go missing.
builder.Logging.ClearProviders();

builder.AddServiceDefaults();

// Enable static web assets resolution for non-production environments (e.g. Mock)
// By default, only the Development environment activates this automatically
if (!builder.Environment.IsProduction())
{
    builder.WebHost.UseStaticWebAssets();
}

// Check for mock mode
var mockModeOptions = new MockModeOptions();
builder.Configuration.GetSection(MockModeOptions.SectionName).Bind(mockModeOptions);

// Allow environment variable override
if (Environment.GetEnvironmentVariable("HOMESPUN_MOCK_MODE") == "true")
{
    mockModeOptions.Enabled = true;
}

// Register custom Homespun activity sources for tracing
builder.Services.AddHomespunInstrumentation();

// SessionEventContent — content-preview gating for session-pipeline spans.
// The new `SessionEventContent` section is authoritative; the legacy
// `SessionEventLog` section is read as a fallback for one release so existing
// deployments keep working through the hop-log → span migration.
builder.Services.Configure<SessionEventContentOptions>(options =>
{
    var primary = builder.Configuration.GetSection(SessionEventContentOptions.SectionName);
    var legacy = builder.Configuration.GetSection(SessionEventContentOptions.LegacySectionName);
    var source = primary.Exists() ? primary : legacy;
    source.Bind(options);
});
builder.Services.AddSingleton<IContentPreviewGate, ContentPreviewGate>();

// SessionDebugLogging — HOMESPUN_DEBUG_FULL_MESSAGES umbrella flag. Opts the
// session pipeline into full-body A2A / AG-UI / envelope log emission.
builder.Services.Configure<SessionDebugLoggingOptions>(options =>
{
    var raw = Environment.GetEnvironmentVariable(SessionDebugLoggingOptions.EnvVarName);
    options.FullMessages = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
});

// OTLP receiver proxy (POST /api/otlp/v1/{logs,traces}): worker + client ship
// OTLP to the server, which scrubs + fans out to Seq + the Aspire dashboard.
// Registered at top level so mock-mode integration tests can exercise the
// endpoints the same way production does.
builder.Services.Configure<OtlpFanoutOptions>(
    builder.Configuration.GetSection(OtlpFanoutOptions.SectionName));
builder.Services.Configure<OtlpScrubberOptions>(
    builder.Configuration.GetSection(OtlpScrubberOptions.SectionName));
builder.Services.AddHttpClient(OtlpFanout.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IOtlpScrubber, OtlpScrubber>();
builder.Services.AddSingleton<IOtlpFanout, OtlpFanout>();

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
    builder.Services.AddMockServices(mockModeOptions, builder.Configuration);

    // Services that are shared between mock and production mode
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();
    builder.Services.AddSingleton<INotificationService, NotificationService>();
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
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
    builder.Services.AddScoped<ISecretsService, SecretsService>();
    builder.Services.AddScoped<IContainerQueryService, ContainerQueryService>();
    builder.Services.AddSingleton<IGitHubEnvironmentService, GitHubEnvironmentService>();
    builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
    builder.Services.AddSingleton<IGitCloneService, GitCloneService>();
    builder.Services.AddSingleton<IMergeStatusCacheService, MergeStatusCacheService>();
    builder.Services.AddScoped<ICloneEnrichmentService, CloneEnrichmentService>();
    builder.Services.AddScoped<PullRequestDataService>();
    builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
    builder.Services.AddScoped<IGitHubService, GitHubService>();
    builder.Services.AddScoped<PullRequestWorkflowService>();

    // Search services (for @ and # mention autocomplete)
    builder.Services.AddScoped<IProjectFileService, ProjectFileService>();
    builder.Services.AddScoped<IPrDataProvider, PrDataProvider>();
    builder.Services.AddScoped<ISearchablePrService, SearchablePrService>();

    // Fleece services (event-sourced issue tracking)
    builder.Services.AddSingleton<IssueSerializationQueueService>();
    builder.Services.AddSingleton<IIssueSerializationQueue>(sp => sp.GetRequiredService<IssueSerializationQueueService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<IssueSerializationQueueService>());
    builder.Services.AddSingleton<global::Fleece.Core.Services.Interfaces.IGraphLayoutService, GraphLayoutService>();
    builder.Services.AddSingleton<global::Fleece.Core.Services.Interfaces.IIssueLayoutService, IssueLayoutService>();
    builder.Services.AddSingleton<IProjectFleeceService, ProjectFleeceService>();
    builder.Services.AddSingleton<IIssueAncestorTraversalService, IssueAncestorTraversalService>();
    builder.Services.AddScoped<IFleeceIssueTransitionService, FleeceIssueTransitionService>();
    builder.Services.AddSingleton<IFleeceIssuesSyncService, FleeceIssuesSyncService>();
    builder.Services.AddScoped<IIssueBranchResolverService, IssueBranchResolverService>();
    builder.Services.AddScoped<IFleeceIssueDiffService, FleeceIssueDiffService>();
    builder.Services.AddScoped<IFleeceChangeDetectionService, FleeceChangeDetectionService>();
    builder.Services.AddScoped<IFleeceConflictDetectionService, FleeceConflictDetectionService>();
    builder.Services.AddScoped<IFleeceChangeApplicationService, FleeceChangeApplicationService>();
    builder.Services.AddScoped<IFleecePostMergeService, FleecePostMergeService>();

    // Markdown rendering service
    builder.Services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();

    // Issue PR status service (for getting PR status linked to issues)
    builder.Services.AddScoped<IIssuePrStatusService, IssuePrStatusService>();

    // Gitgraph services - cache stored as JSONL files alongside project data
    builder.Services.AddSingleton<IGraphCacheService>(sp =>
        new GraphCacheService(sp.GetRequiredService<ILogger<GraphCacheService>>()));
    builder.Services.AddScoped<IGraphService, GraphService>();

    // PR status resolver (for resolving merged/closed PR statuses in the graph cache)
    builder.Services.AddScoped<IPRStatusResolver, PRStatusResolver>();

    // Issue-PR linking service (must be registered before GitHubService as it depends on it)
    builder.Services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();

    // Notification services
    builder.Services.AddSingleton<INotificationService, NotificationService>();

    // Navigation services
    builder.Services.AddScoped<IBreadcrumbService, BreadcrumbService>();

    // Claude Code SDK services
    builder.Services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();

    // Anthropic model catalog (live path): authoritative list of available
    // Claude models fetched via HTTP against /v1/models and cached in-process.
    // We bypass the SDK here because its Models.List path rejects OAuth tokens
    // ("OAuth authentication is currently not supported") while the same token
    // is accepted verbatim as `x-api-key` by the REST endpoint.
    builder.Services.AddMemoryCache();
    builder.Services.AddHttpClient(AnthropicModelSource.HttpClientName);
    builder.Services.AddSingleton<IAnthropicModelSource, AnthropicModelSource>();
    builder.Services.AddSingleton<IModelCatalogService>(sp => new ModelCatalogService(
        sp.GetRequiredService<IAnthropicModelSource>(),
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
        sp.GetRequiredService<ILogger<ModelCatalogService>>()));

    // Agent Execution service - mode-gated:
    //   "Docker" (default): container-per-issue with discovery/recovery
    //   "SingleContainer" (Development only): forwards every session to a
    //   pre-running docker-compose worker at AgentExecution:SingleContainer:WorkerUrl
    var agentMode = builder.Configuration["AgentExecution:Mode"] ?? "Docker";
    if (agentMode == "SingleContainer")
    {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "AgentExecution:Mode=SingleContainer is only permitted when ASPNETCORE_ENVIRONMENT=Development.");
        }

        builder.Services.Configure<SingleContainerAgentExecutionOptions>(
            builder.Configuration.GetSection(SingleContainerAgentExecutionOptions.SectionName));
        var workerUrl = builder.Configuration[$"{SingleContainerAgentExecutionOptions.SectionName}:WorkerUrl"];
        if (string.IsNullOrWhiteSpace(workerUrl))
        {
            throw new InvalidOperationException(
                "AgentExecution:SingleContainer:WorkerUrl must be set when AgentExecution:Mode=SingleContainer.");
        }

        builder.Services.AddSingleton<IAgentExecutionService, SingleContainerAgentExecutionService>();

        // Development's default DI scope validation surfaces pre-existing Singleton →
        // Scoped consumption issues across the real service graph that block startup.
        // Those are not in scope for this dev-only shim; disable validation to match
        // Production-mode behaviour so the --with-worker path actually boots.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });
    }
    else
    {
        builder.Services.Configure<DockerAgentExecutionOptions>(
            builder.Configuration.GetSection(DockerAgentExecutionOptions.SectionName));
        builder.Services.PostConfigure<DockerAgentExecutionOptions>(options =>
        {
            var hostPath = Environment.GetEnvironmentVariable("HSP_HOST_DATA_PATH");
            if (!string.IsNullOrEmpty(hostPath))
                options.HostDataPath = hostPath;
        });
        builder.Services.AddSingleton<IAgentExecutionService, DockerAgentExecutionService>();
        builder.Services.AddSingleton<IContainerDiscoveryService, ContainerDiscoveryService>();
        builder.Services.AddHostedService(sp =>
        {
            var discoveryService = sp.GetRequiredService<IContainerDiscoveryService>();
            var executionService = sp.GetRequiredService<IAgentExecutionService>() as DockerAgentExecutionService;
            var logger = sp.GetRequiredService<ILogger<ContainerRecoveryHostedService>>();
            return new ContainerRecoveryHostedService(
                discoveryService,
                (container, ct) => executionService?.RegisterDiscoveredContainerAsync(container, ct)
                    ?? Task.CompletedTask,
                logger);
        });
    }

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

    // A2A event store — append-only JSONL of raw A2A events per session,
    // the source of truth for both live broadcast and replay.
    // Legacy MessageCacheStore (ClaudeMessage JSONL) has been retired;
    // SessionCachePurgeHostedService below wipes its residue on startup.
    var messageCacheDir = Path.Combine(dataDirectory!, "sessions");
    builder.Services.AddSingleton<IA2AEventStore>(sp =>
        new A2AEventStore(
            messageCacheDir,
            sp.GetRequiredService<ILogger<A2AEventStore>>()));

    // Pure A2A → AG-UI translator used by both the live ingestion path and the replay endpoint.
    builder.Services.AddSingleton<IA2AToAGUITranslator, A2AToAGUITranslator>();

    // SessionEventIngestor — orchestrates worker A2A event → store append → translate →
    // envelope broadcast. Single point where the append-before-broadcast invariant lives.
    builder.Services.AddSingleton<ISessionEventIngestor, SessionEventIngestor>();

    // PerSessionEventStream — long-lived background reader that consumes the worker's
    // GET /api/sessions/{id}/events endpoint for the full session lifetime and drives
    // the ingestor on every A2A event. This is what keeps post-result background events
    // (task_notification / task_updated / task_started) flowing to the client after a
    // turn has ended. MUST be a singleton (the service holds a per-session reader
    // dictionary) — see PerSessionEventStreamServiceCollectionExtensions for the
    // named-HttpClient wiring that keeps the service a clean singleton while
    // IHttpClientFactory manages handler lifetime.
    builder.Services.AddPerSessionEventStream();

    // Pending-tool-call registry + result appender — bridge between the translator's
    // input-required → TOOL_CALL_* emission and the hub's AnswerQuestion / ApprovePlan
    // handlers. The translator registers the toolCallId; the hub dequeues it and feeds a
    // synthetic tool_result message back through the ingestor so live + replay see the
    // completed tool call identically.
    builder.Services.AddSingleton<IPendingToolCallRegistry, PendingToolCallRegistry>();
    builder.Services.AddSingleton<IToolCallResultAppender, ToolCallResultAppender>();

    // Replay-endpoint default mode (Incremental vs Full) + any future session-event options.
    builder.Services.Configure<SessionEventsOptions>(
        builder.Configuration.GetSection(SessionEventsOptions.SectionName));

    // One-shot startup purge of legacy MessageCacheStore JSONL files. The new A2A
    // event log (*.events.jsonl) is left untouched. See `docs/a2a-native-migration.md`
    // and `HOMESPUN_SKIP_CACHE_PURGE=true` to opt out.
    builder.Services.AddHostedService(sp =>
        new SessionCachePurgeHostedService(
            messageCacheDir,
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<SessionCachePurgeHostedService>>()));

    // Issue workspace service - manages per-issue folder structure for agent isolation
    var projectsBaseDir = builder.Configuration["HOMESPUN_PROJECTS_PATH"]
        ?? Path.Combine(dataDirectory!, "projects");
    builder.Services.AddSingleton<IIssueWorkspaceService>(sp =>
        new IssueWorkspaceService(
            projectsBaseDir,
            sp.GetRequiredService<ICommandRunner>(),
            sp.GetRequiredService<IFleeceIssuesSyncService>(),
            sp.GetRequiredService<ILogger<IssueWorkspaceService>>()));

    builder.Services.AddSingleton<IToolResultParser, ToolResultParser>();
    builder.Services.AddSingleton<IHooksService, HooksService>();
    builder.Services.AddSingleton<IAGUIEventService, AGUIEventService>();

    // Session state and decomposed services (registered before facade)
    builder.Services.AddSingleton<ISessionStateManager, SessionStateManager>();
    builder.Services.AddSingleton<IPlanArtefactStore, PlanArtefactStore>();
    builder.Services.AddSingleton<IToolInteractionService, ToolInteractionService>();
    builder.Services.AddSingleton<ISessionLifecycleService, SessionLifecycleService>();
    builder.Services.AddSingleton<IMessageProcessingService, MessageProcessingService>();
    // Lazy wrappers for circular dependency resolution
    builder.Services.AddSingleton(sp =>
        new Lazy<IMessageProcessingService>(() => sp.GetRequiredService<IMessageProcessingService>()));
    builder.Services.AddSingleton(sp =>
        new Lazy<ISessionLifecycleService>(() => sp.GetRequiredService<ISessionLifecycleService>()));
    builder.Services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();
    builder.Services.AddSingleton<IAgentStartupTracker, AgentStartupTracker>();
    builder.Services.AddSingleton<ISkillDiscoveryService, SkillDiscoveryService>();
    builder.Services.AddSingleton<IRebaseAgentService, RebaseAgentService>();

    // Agent Orchestration services (mini-prompts, branch ID generation, agent startup)
    builder.Services.Configure<MiniPromptOptions>(
        builder.Configuration.GetSection(MiniPromptOptions.SectionName));
    builder.Services.AddHttpClient("MiniPrompt");
    builder.Services.AddSingleton<IMiniPromptService, MiniPromptService>();
    builder.Services.AddHostedService<MiniPromptHealthCheckHostedService>();
    builder.Services.AddSingleton<IBranchIdGeneratorService, BranchIdGeneratorService>();
    builder.Services.AddSingleton<IBranchIdBackgroundService, BranchIdBackgroundService>();
    builder.Services.AddScoped<IBaseBranchResolver, BaseBranchResolver>();
    builder.Services.AddSingleton<IAgentStartBackgroundService, AgentStartBackgroundService>();
    builder.Services.AddSingleton<IActionQueueCoordinator, ActionQueueCoordinator>();

    // GitHub sync polling service (PR sync, review polling, issue linking)
    builder.Services.Configure<GitHubSyncPollingOptions>(
        builder.Configuration.GetSection(GitHubSyncPollingOptions.SectionName));
    builder.Services.AddHostedService<GitHubSyncPollingService>();
}

// SignalR URL provider (uses internal URL in Docker, localhost in development)
builder.Services.Configure<SignalROptions>(
    builder.Configuration.GetSection(SignalROptions.SectionName));
builder.Services.AddSingleton<ISignalRUrlProvider, SignalRUrlProvider>();

// Plans service (reads plan files from .claude/plans directory)
builder.Services.AddSingleton<IPlansService, PlansService>();

// TimeProvider is required by OpenSpec services and may not be registered under mock mode.
builder.Services.AddSingleton(TimeProvider.System);

// OpenSpec services (change-to-issue linkage via Fleece openspec= tags)
builder.Services.AddScoped<IChangeScannerService, ChangeScannerService>();
builder.Services.AddScoped<IChangeReconciliationService, ChangeReconciliationService>();
builder.Services.AddSingleton<IBranchStateCacheService, BranchStateCacheService>();
builder.Services.AddScoped<IBranchStateResolverService, BranchStateResolverService>();
builder.Services.AddScoped<IIssueGraphOpenSpecEnricher, IssueGraphOpenSpecEnricher>();
builder.Services.AddScoped<IPhaseDispatchGuard, PhaseDispatchGuard>();

builder.Services.AddSignalR(o =>
    {
        // TraceparentHubFilter extracts the W3C traceparent the client
        // passes as the first argument of every hub method and starts a
        // server-side activity parented to it. See
        // Homespun.Features.Observability.TraceparentHubFilter.
        o.AddFilter<TraceparentHubFilter>();
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddHomespunHealthChecks(dataDirectory!);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SchemaFilter<EnumSchemaFilter>();
});

// Configure CORS for Blazor WASM client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    // Named policy for SignalR (requires credentials)
    options.AddPolicy("SignalR", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// SessionEventContent: warn if content previews are enabled in Production —
// this ships raw event text to Seq span attributes, which may leak sensitive
// content. Also log a deprecation warning if the legacy config section was
// consulted.
var sessionEventContentOptions = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionEventContentOptions>>().Value;
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Homespun.SessionEvents");

var primarySection = builder.Configuration.GetSection(SessionEventContentOptions.SectionName);
var legacySection = builder.Configuration.GetSection(SessionEventContentOptions.LegacySectionName);
if (!primarySection.Exists() && legacySection.Exists())
{
    startupLogger.LogWarning(
        "Config section '{Legacy}' is deprecated; rename to '{Primary}'. The legacy name will be removed in the next release.",
        SessionEventContentOptions.LegacySectionName,
        SessionEventContentOptions.SectionName);
}

if (app.Environment.IsProduction() && sessionEventContentOptions.ContentPreviewChars > 0)
{
    startupLogger.LogWarning(
        "SessionEventContent:ContentPreviewChars is set to {Chars} in Production. Event content previews will be attached to span attributes.",
        sessionEventContentOptions.ContentPreviewChars);
}
else if (sessionEventContentOptions.ContentPreviewChars == -1)
{
    startupLogger.LogInformation(
        "SessionEventContent:ContentPreviewChars is -1. Full-body content previews enabled on span attributes and OTLP log records.");
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Enable Swagger in all environments for API testing
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Homespun API v1");
});

app.UseCors();

// Map SignalR hubs
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ClaudeCodeHub>("/hubs/claudecode");

// Map health check endpoints (/health for readiness, /alive for liveness)
app.MapDefaultEndpoints();

// Map API controllers
app.MapControllers();

// MapStaticAssets serves static files via endpoint routing
app.MapStaticAssets();

app.Run();

// Make the implicit Program class public so WebApplicationFactory<Program> can reference it
public partial class Program { }
