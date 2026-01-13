using System.Web;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Middleware that redirects agent page requests to include an absolute ?url= parameter.
/// Must run BEFORE YARP to avoid Content-Length mismatches.
///
/// OpenCode's normalizeServerUrl expects an absolute URL (http://...) or a hostname.
/// If ?url= is a relative path like /agent/4096, it becomes http:///agent/4096 (malformed).
/// This middleware redirects to include the proper absolute URL based on the request's Host header.
/// </summary>
public class AgentUrlRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AgentUrlRedirectMiddleware> _logger;

    public AgentUrlRedirectMiddleware(RequestDelegate next, ILogger<AgentUrlRedirectMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only handle agent routes: /agent/{port}/...
        if (!path.StartsWith("/agent/"))
        {
            await _next(context);
            return;
        }

        // Extract port from path
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !int.TryParse(segments[1], out var port))
        {
            await _next(context);
            return;
        }

        var urlParam = context.Request.Query["url"].FirstOrDefault();

        // Only redirect for session page requests (not API calls or assets)
        var isPageRequest = path.Contains("/session/") && !path.Contains("/api/");

        // If ?url= is missing or is a relative path, redirect with absolute URL
        if (isPageRequest && (string.IsNullOrEmpty(urlParam) || urlParam.StartsWith("/")))
        {
            var basePath = $"/agent/{port}";
            var scheme = context.Request.Scheme;
            var host = context.Request.Host.ToString();
            var absoluteBaseUrl = $"{scheme}://{host}{basePath}";

            // Build redirect URL with absolute ?url= parameter
            var queryString = HttpUtility.ParseQueryString(context.Request.QueryString.Value ?? "");
            queryString["url"] = absoluteBaseUrl;

            var redirectUrl = $"{context.Request.Path}?{queryString}";

            _logger.LogDebug("Redirecting to add absolute ?url= parameter: {RedirectUrl}", redirectUrl);

            context.Response.StatusCode = 302;
            context.Response.Headers.Location = redirectUrl;
            return; // Short-circuit - don't call _next (YARP)
        }

        await _next(context);
    }
}
