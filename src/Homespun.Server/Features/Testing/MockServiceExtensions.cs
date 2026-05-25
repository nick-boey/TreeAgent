using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.OpenSpec.Services;
using Homespun.Features.Containers.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Observability;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Search;
using Homespun.Features.Secrets;
using Homespun.Features.Commands;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing;

/// <summary>
/// Extension methods for registering mock services.
/// </summary>
public static class MockServiceExtensions
{
    /// <summary>
    /// Adds all mock services to the service collection.
    /// Call this instead of registering production services when mock mode is enabled.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Mock mode configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMockServices(
        this IServiceCollection services,
        MockModeOptions options,
        IConfiguration configuration)
    {
        // Register Homespun ActivitySources (Gitgraph/OpenSpec/Commands + existing)
        // so dev-mock emits task-graph spans identically to production.
        services.AddHomespunInstrumentation();

        // Register the temporary data folder service - creates temp folder structure
        services.AddSingleton<TempDataFolderService>();
        services.AddSingleton<ITempDataFolderService>(sp => sp.GetRequiredService<TempDataFolderService>());

        // Register the FleeceIssueSeeder for seeding issues to JSONL files
        services.AddSingleton<FleeceIssueSeeder>();

        // Register the OpenSpecMockSeeder for seeding openspec/ content and per-branch deltas
        services.AddSingleton<OpenSpecMockSeeder>();

        // Register real JsonDataStore with temp file path
        services.AddSingleton<IDataStore>(sp =>
        {
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<JsonDataStore>>();
            return new JsonDataStore(tempFolder.DataFilePath, logger);
        });

        // Register real FleeceService with supporting services
        // Issue serialization queue (background service for persistence)
        services.AddSingleton<IssueSerializationQueueService>();
        services.AddSingleton<IIssueSerializationQueue>(sp => sp.GetRequiredService<IssueSerializationQueueService>());
        services.AddHostedService(sp => sp.GetRequiredService<IssueSerializationQueueService>());

        // Register Fleece.Core layout services (required by ProjectFleeceService)
        services.AddSingleton<global::Fleece.Core.Services.Interfaces.IGraphLayoutService,
            global::Fleece.Core.Services.GraphLayout.GraphLayoutService>();
        services.AddSingleton<global::Fleece.Core.Services.Interfaces.IIssueLayoutService,
            global::Fleece.Core.Services.GraphLayout.IssueLayoutService>();

        // Register real FleeceService (reads/writes to temp .fleece directories)
        services.AddSingleton<IProjectFleeceService, ProjectFleeceService>();
        services.AddSingleton<IIssueUndoRedoService, IssueUndoRedoService>();
        services.AddSingleton<IIssueAncestorTraversalService, IssueAncestorTraversalService>();

        // Core services
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IGitHubEnvironmentService, MockGitHubEnvironmentService>();
        services.AddSingleton<IGitHubClientWrapper, MockGitHubClientWrapper>();

        // GitHub services
        services.AddScoped<IGitHubService, MockGitHubService>();
        services.AddScoped<IIssuePrLinkingService, IssuePrLinkingService>();
        services.AddScoped<IPRStatusResolver, MockPRStatusResolver>();

        // Project service - use real ProjectService with temp folder path
        services.AddScoped<IProjectService>(sp =>
        {
            var dataStore = sp.GetRequiredService<IDataStore>();
            var gitHubService = sp.GetRequiredService<IGitHubService>();
            var commandRunner = sp.GetRequiredService<ICommandRunner>();
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<ProjectService>>();

            var projectsPath = Path.Combine(tempFolder.RootPath, "projects");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HOMESPUN_BASE_PATH"] = projectsPath
                })
                .Build();

            return new ProjectService(dataStore, gitHubService, commandRunner, config, logger);
        });

        // Secrets service (real implementation - uses secrets.env files, works with temp folder structure)
        services.AddScoped<ISecretsService, SecretsService>();

        // Container query service
        services.AddScoped<IContainerQueryService, MockContainerQueryService>();

        // Fleece services
        // Transition service depends on IProjectFleeceService (now using real FleeceService)
        services.AddScoped<IFleeceIssueTransitionService, FleeceIssueTransitionService>();
        services.AddSingleton<IFleeceIssuesSyncService, MockFleeceIssuesSyncService>();
        services.AddScoped<IIssueBranchResolverService, IssueBranchResolverService>();
        services.AddScoped<IFleeceIssueDiffService, FleeceIssueDiffService>();
        services.AddScoped<IPhaseDispatchGuard, PhaseDispatchGuard>();
        services.AddScoped<IFleeceChangeDetectionService, FleeceChangeDetectionService>();
        services.AddScoped<IFleeceConflictDetectionService, FleeceConflictDetectionService>();
        services.AddScoped<IFleeceChangeApplicationService, FleeceChangeApplicationService>();
        services.AddScoped<IFleecePostMergeService, FleecePostMergeService>();

        // Git services
        services.AddSingleton<IGitCloneService, MockGitCloneService>();

        // Search services (for @ and # mention autocomplete)
        services.AddScoped<IProjectFileService, ProjectFileService>();
        services.AddScoped<IPrDataProvider, PrDataProvider>();
        services.AddScoped<ISearchablePrService, SearchablePrService>();

        // Claude Code services - use the real session store (already in-memory)
        services.AddSingleton<IClaudeSessionStore, ClaudeSessionStore>();
        services.AddSingleton<IToolResultParser, ToolResultParser>();

        // Model catalog — dev-mock keeps MockModelCatalogService (no network).
        // Any mock profile with UseLiveClaudeSessions=true (dev-live, dev-windows,
        // dev-container) uses the live ModelCatalogService so /api/models reflects
        // the real Anthropic catalogue instead of the static fallback list.
        services.AddMemoryCache();
        if (options.UseLiveClaudeSessions)
        {
            services.AddHttpClient(AnthropicModelSource.HttpClientName);
            services.AddSingleton<IAnthropicModelSource, AnthropicModelSource>();
            services.AddSingleton<IModelCatalogService>(sp => new ModelCatalogService(
                sp.GetRequiredService<IAnthropicModelSource>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<ILogger<ModelCatalogService>>()));
        }
        else
        {
            services.AddSingleton<IModelCatalogService, MockModelCatalogService>();
        }

        // Always use the real session pipeline; IAgentExecutionService picks between the
        // Docker/SingleContainer/Mock executors based on AgentExecution:Mode.
        services.AddClaudeSessionServices(options, configuration);

        services.AddSingleton<IRebaseAgentService, MockRebaseAgentService>();
        services.AddSingleton<ISkillDiscoveryService, SkillDiscoveryService>();

        // Agent Orchestration services - configure options and HTTP client first
        services.Configure<MiniPromptOptions>(options => { });  // Empty config - uses defaults
        services.AddHttpClient("MiniPrompt");  // Register named HTTP client

        // Then register the services
        services.AddSingleton<IMiniPromptService, MiniPromptService>();
        services.AddSingleton<IBranchIdGeneratorService, BranchIdGeneratorService>();
        services.AddSingleton<IBranchIdBackgroundService, BranchIdBackgroundService>();
        services.AddScoped<IBaseBranchResolver, BaseBranchResolver>();
        services.AddSingleton<IAgentStartBackgroundService, MockAgentStartBackgroundService>();
        services.AddSingleton<IActionQueueCoordinator, ActionQueueCoordinator>();

        // A2A event store + translator — shared between mock and production modes.
        services.AddSingleton<IA2AEventStore>(sp =>
        {
            var tempFolder = sp.GetRequiredService<ITempDataFolderService>();
            var logger = sp.GetRequiredService<ILogger<A2AEventStore>>();
            return new A2AEventStore(tempFolder.SessionsPath, logger);
        });
        services.AddSingleton<IPendingToolCallRegistry, PendingToolCallRegistry>();
        services.AddSingleton<IA2AToAGUITranslator, A2AToAGUITranslator>();
        services.AddSingleton<ISessionEventIngestor, SessionEventIngestor>();
        services.AddSingleton<IToolCallResultAppender, ToolCallResultAppender>();
        services.Configure<Homespun.Features.ClaudeCode.Settings.SessionEventsOptions>(
            configuration.GetSection(Homespun.Features.ClaudeCode.Settings.SessionEventsOptions.SectionName));

        // Pull request workflow service (needed by GraphService)
        services.AddScoped<PullRequestWorkflowService>();

        // Graph services
        services.AddSingleton<IGraphCacheService>(sp =>
            new GraphCacheService(sp.GetRequiredService<ILogger<GraphCacheService>>()));
        services.AddScoped<IGraphService, GraphService>();

        // TimeProvider needed by OpenSpec services.
        services.TryAddSingleton(TimeProvider.System);

        // Clone enrichment service
        services.AddScoped<ICloneEnrichmentService, CloneEnrichmentService>();

        // Issue PR status service
        services.AddScoped<IIssuePrStatusService, IssuePrStatusService>();

        // JSONL session loader for loading real session data

        // Seed data service (if enabled)
        if (options.SeedData)
        {
            services.AddHostedService<MockDataSeederService>();
        }

        return services;
    }

    /// <summary>
    /// Registers the real Claude session pipeline (SessionLifecycleService, MessageProcessingService,
    /// ClaudeSessionService) over a Docker / SingleContainer / Mock executor backend.
    /// </summary>
    private static IServiceCollection AddClaudeSessionServices(
        this IServiceCollection services,
        MockModeOptions options,
        IConfiguration configuration)
    {
        // Agent execution selection mirrors the non-mock path in Program.cs, but unless
        // UseLiveClaudeSessions is explicitly set we always pick the mock executor —
        // otherwise dev-mock / Api.Tests would try to spawn real Docker containers.
        //   Docker            → DockerAgentExecutionService + discovery/recovery
        //   SingleContainer   → SingleContainerAgentExecutionService shim
        //   unset / other     → MockAgentExecutionService
        var agentMode = options.UseLiveClaudeSessions ? configuration["AgentExecution:Mode"] : null;
        if (string.Equals(agentMode, "Docker", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[AgentExecution] Docker: Registering DockerAgentExecutionService");
            services.Configure<DockerAgentExecutionOptions>(
                configuration.GetSection(DockerAgentExecutionOptions.SectionName));
            services.PostConfigure<DockerAgentExecutionOptions>(opts =>
            {
                var hostPath = Environment.GetEnvironmentVariable("HSP_HOST_DATA_PATH");
                if (!string.IsNullOrEmpty(hostPath))
                    opts.HostDataPath = hostPath;
            });
            // DockerAgentExecutionService depends on the IPerSessionEventStream singleton
            // for its rewired /events consumption path (task 8 of the
            // fix-post-result-events plan). The mock DI graph must register it too.
            services.AddPerSessionEventStream();
            services.AddSingleton<IAgentExecutionService, DockerAgentExecutionService>();
            services.AddSingleton<IContainerDiscoveryService, ContainerDiscoveryService>();
            services.AddHostedService(sp =>
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
        else if (string.Equals(agentMode, "SingleContainer", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[AgentExecution] SingleContainer: Registering SingleContainerAgentExecutionService");
            services.Configure<SingleContainerAgentExecutionOptions>(
                configuration.GetSection(SingleContainerAgentExecutionOptions.SectionName));
            // SingleContainerAgentExecutionService depends on the IPerSessionEventStream
            // singleton for its rewired /events consumption path (task 9 of the
            // fix-post-result-events plan). The mock DI graph must register it too.
            services.AddPerSessionEventStream();
            services.AddSingleton<IAgentExecutionService, SingleContainerAgentExecutionService>();
        }
        else
        {
            Console.WriteLine("[AgentExecution] Mock mode: Registering MockAgentExecutionService");
            services.AddSingleton<IAgentExecutionService, MockAgentExecutionService>();
        }

        // Determine working directory for live sessions. Only relevant in live mode —
        // in pure mock mode we leave TestWorkingDirectory unset so MockGitCloneService
        // gives each clone a unique path instead of routing them all through a single
        // test workspace (which would collide across concurrent integration tests).
        string? workingDirectory = null;
        if (options.UseLiveClaudeSessions)
        {
            var dataPath2 = Environment.GetEnvironmentVariable("HOMESPUN_DATA_PATH");
            string defaultWorkspace;
            if (!string.IsNullOrEmpty(dataPath2))
            {
                var dataDirectory = Path.GetDirectoryName(dataPath2);
                defaultWorkspace = Path.Combine(dataDirectory!, "test-workspace");
            }
            else
            {
                defaultWorkspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "test-workspace");
            }
            workingDirectory = options.LiveClaudeSessionsWorkingDirectory ?? defaultWorkspace;

            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }
        }

        // Session discovery service - reads from Claude's native session storage
        var homespunDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homespun");
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        services.AddSingleton<IClaudeSessionDiscovery>(sp =>
            new ClaudeSessionDiscovery(claudeDir, sp.GetRequiredService<ILogger<ClaudeSessionDiscovery>>()));

        // Session metadata store - maps Claude sessions to our entities
        if (!Directory.Exists(homespunDir))
        {
            Directory.CreateDirectory(homespunDir);
        }
        var metadataPath = Path.Combine(homespunDir, "session-metadata-test.json");
        services.AddSingleton<ISessionMetadataStore>(sp =>
            new SessionMetadataStore(metadataPath, sp.GetRequiredService<ILogger<SessionMetadataStore>>()));

        // Tool result parser for rich display
        services.AddSingleton<IToolResultParser, ToolResultParser>();

        // Hooks service for session startup hooks
        services.AddSingleton<IHooksService, HooksService>();

        // AG-UI event service for translating A2A events
        services.AddSingleton<IAGUIEventService, AGUIEventService>();

        // Session state and decomposed services
        services.AddSingleton<ISessionStateManager, SessionStateManager>();
        services.AddSingleton<IPlanArtefactStore, PlanArtefactStore>();
        services.AddSingleton<IToolInteractionService, ToolInteractionService>();
        services.AddSingleton<ISessionLifecycleService, SessionLifecycleService>();
        services.AddSingleton<IMessageProcessingService, MessageProcessingService>();
        services.AddSingleton(sp =>
            new Lazy<IMessageProcessingService>(() => sp.GetRequiredService<IMessageProcessingService>()));
        services.AddSingleton(sp =>
            new Lazy<ISessionLifecycleService>(() => sp.GetRequiredService<ISessionLifecycleService>()));
        services.AddSingleton<IClaudeSessionService, ClaudeSessionService>();

        // Store the test working directory in configuration for the MockGitCloneService
        // (only populated when running against a live Claude agent).
        services.Configure<LiveClaudeTestOptions>(opts =>
        {
            opts.TestWorkingDirectory = workingDirectory ?? "";
        });

        return services;
    }
}

/// <summary>
/// Options for live Claude testing in mock mode.
/// </summary>
public class LiveClaudeTestOptions
{
    /// <summary>
    /// The working directory used for live Claude test sessions.
    /// </summary>
    public string TestWorkingDirectory { get; set; } = "";
}
