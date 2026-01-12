using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Manages dynamic YARP routes for OpenCode agent servers.
/// Routes are added when servers start and removed when they stop.
/// </summary>
public class AgentProxyRouteManager : IProxyConfigProvider
{
    private readonly OpenCodeOptions _options;
    private readonly ILogger<AgentProxyRouteManager> _logger;
    private readonly ConcurrentDictionary<int, (RouteConfig Route, ClusterConfig Cluster)> _routes = new();
    private volatile InMemoryConfig _config;
    private CancellationTokenSource _changeTokenSource = new();

    public AgentProxyRouteManager(
        IOptions<OpenCodeOptions> options,
        ILogger<AgentProxyRouteManager> logger)
    {
        _options = options.Value;
        _logger = logger;
        _config = new InMemoryConfig([], [], _changeTokenSource.Token);
    }

    public IProxyConfig GetConfig() => _config;

    public void AddRoute(int port)
    {
        var routeId = $"agent-{port}";
        var clusterId = $"agent-cluster-{port}";
        var basePath = _options.AgentProxyBasePath;

        var route = new RouteConfig
        {
            RouteId = routeId,
            ClusterId = clusterId,
            Match = new RouteMatch
            {
                // Match /agent/{port}/{**remainder}
                Path = $"{basePath}/{port}/{{**remainder}}"
            },
            Transforms = new List<IReadOnlyDictionary<string, string>>
            {
                // Strip the /agent/{port} prefix when forwarding
                new Dictionary<string, string>
                {
                    { "PathRemovePrefix", $"{basePath}/{port}" }
                }
            }
        };

        var cluster = new ClusterConfig
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                { "default", new DestinationConfig { Address = $"http://127.0.0.1:{port}" } }
            }
        };

        _routes[port] = (route, cluster);
        RebuildConfig();

        _logger.LogInformation("Added proxy route for agent on port {Port}: {BasePath}/{Port}/* -> http://127.0.0.1:{Port}/*",
            port, basePath, port, port);
    }

    public void RemoveRoute(int port)
    {
        if (_routes.TryRemove(port, out _))
        {
            RebuildConfig();
            _logger.LogInformation("Removed proxy route for agent on port {Port}", port);
        }
    }

    private void RebuildConfig()
    {
        // Signal that the old config is changing
        var oldTokenSource = _changeTokenSource;
        _changeTokenSource = new CancellationTokenSource();

        var routes = _routes.Values.Select(r => r.Route).ToList();
        var clusters = _routes.Values.Select(r => r.Cluster).ToList();
        _config = new InMemoryConfig(routes, clusters, _changeTokenSource.Token);

        // Cancel the old token to notify YARP that config has changed
        oldTokenSource.Cancel();
    }

    private class InMemoryConfig(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        CancellationToken changeToken) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(changeToken);
    }
}
