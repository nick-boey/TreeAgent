using System.Text;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// YARP transform provider that rewrites absolute paths in HTML responses from OpenCode.
/// This ensures assets load correctly when accessed through the /agent/{port}/ proxy path.
/// </summary>
public partial class AgentProxyResponseTransform : ITransformProvider
{
    private readonly ILogger<AgentProxyResponseTransform> _logger;

    public AgentProxyResponseTransform(ILogger<AgentProxyResponseTransform> logger)
    {
        _logger = logger;
    }

    public void ValidateRoute(TransformRouteValidationContext context)
    {
        // No validation needed
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
        // No validation needed
    }

    public void Apply(TransformBuilderContext context)
    {
        // Only apply to agent routes (e.g., "agent-4096")
        if (context.Route.RouteId?.StartsWith("agent-") != true)
        {
            return;
        }

        var portStr = context.Route.RouteId.Substring(6);
        if (!int.TryParse(portStr, out var port))
        {
            return;
        }

        var basePath = $"/agent/{port}";
        _logger.LogDebug("Applying response transform for route {RouteId} with basePath {BasePath}",
            context.Route.RouteId, basePath);

        context.AddResponseTransform(async transformContext =>
        {
            var response = transformContext.ProxyResponse;
            if (response == null)
            {
                return;
            }

            // Only transform HTML responses
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != "text/html")
            {
                return;
            }

            // Read the original response body
            var originalBody = await response.Content.ReadAsStringAsync();

            // Rewrite absolute paths to include the proxy base path
            var transformedBody = RewriteAbsolutePaths(originalBody, basePath);

            // Write the transformed response
            var bytes = Encoding.UTF8.GetBytes(transformedBody);
            transformContext.HttpContext.Response.ContentLength = bytes.Length;
            transformContext.HttpContext.Response.ContentType = "text/html; charset=utf-8";
            await transformContext.HttpContext.Response.Body.WriteAsync(bytes);

            // Suppress the default body copy since we've written our own
            transformContext.SuppressResponseBody = true;
        });
    }

    /// <summary>
    /// Rewrites absolute paths in HTML content to include the proxy base path.
    /// </summary>
    private static string RewriteAbsolutePaths(string html, string basePath)
    {
        // Rewrite src="/..." attributes (scripts, images, etc.)
        html = SrcHrefPattern().Replace(html, $"$1=\"{basePath}$2");

        // Rewrite url(/...) in inline styles
        html = UrlPattern().Replace(html, $"url($1{basePath}$2");

        // Rewrite action="/..." in forms
        html = ActionPattern().Replace(html, $"action=\"{basePath}$1");

        return html;
    }

    // Regex patterns for rewriting URLs
    // Match src="/..." or href="/..." (but not src="//..." which is protocol-relative)
    [GeneratedRegex(@"(src|href)=""(/(?!/)[^""]*)")]
    private static partial Regex SrcHrefPattern();

    // Match url(/...) or url('/...') or url("/...")
    [GeneratedRegex(@"url\((['""]?)(/(?!/)[^)""']*)")]
    private static partial Regex UrlPattern();

    // Match action="/..."
    [GeneratedRegex(@"action=""(/(?!/)[^""]*)")]
    private static partial Regex ActionPattern();
}
