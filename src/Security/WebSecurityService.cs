using CL.NetUtils;
using CL.WebLogic.Configuration;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CodeLogic;
using CodeLogic.Framework.Libraries;
using Microsoft.AspNetCore.Http;

namespace CL.WebLogic.Security;

public sealed class WebSecurityService
{
    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private readonly IWebAuthResolver _authResolver;
    private readonly Dictionary<string, RateWindow> _rateLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateLock = new();

    public WebSecurityService(LibraryContext context, WebLogicConfig config, IWebAuthResolver authResolver)
    {
        _context = context;
        _config = config;
        _authResolver = authResolver;
    }

    public string GetClientIp(HttpContext context)
    {
        if (_config.Security.TrustForwardedHeaders)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public Task<WebRequestIdentity> ResolveIdentityAsync(HttpContext httpContext) =>
        _authResolver.ResolveIdentityAsync(httpContext);

    public async Task<WebResult?> ValidateAsync(WebRequestContext request)
    {
        if (!_config.Security.AllowedMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
        {
            PublishBlockedEvent(request, StatusCodes.Status405MethodNotAllowed, "method_blocked");
            return WebResult.Text("Method not allowed", StatusCodes.Status405MethodNotAllowed);
        }

        if (_config.Security.EnforceHttps && !request.HttpContext.Request.IsHttps)
        {
            var target = $"https://{request.HttpContext.Request.Host}{request.HttpContext.Request.Path}{request.HttpContext.Request.QueryString}";
            request.HttpContext.Response.Redirect(target, permanent: false);
            return new WebResult
            {
                StatusCode = StatusCodes.Status307TemporaryRedirect,
                ContentType = "text/plain; charset=utf-8",
                TextBody = "HTTPS required"
            };
        }

        if (!CheckRateLimit(request.ClientIp))
        {
            PublishBlockedEvent(request, StatusCodes.Status429TooManyRequests, "rate_limited");
            return WebResult.Text("Too many requests", StatusCodes.Status429TooManyRequests);
        }

        if (_config.Security.EnableDnsbl)
        {
            var library = Libraries.Get<NetUtilsLibrary>();
            if (library is not null)
            {
                try
                {
                    var result = await library.Dnsbl.CheckIpAsync(request.ClientIp).ConfigureAwait(false);
                    if (result.IsBlacklisted)
                    {
                        PublishBlockedEvent(
                            request,
                            StatusCodes.Status403Forbidden,
                            $"dnsbl:{result.MatchedService ?? "matched"}");
                        return WebResult.Text("Forbidden", StatusCodes.Status403Forbidden);
                    }
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning($"DNSBL check failed: {ex.Message}");
                }
            }
        }

        return null;
    }

    public WebResult? AuthorizeRoute(WebRequestContext request, WebRouteDefinition route)
    {
        if (route.AllowAnonymous || route.RequiredAccessGroups.Length == 0)
            return null;

        if (request.HasAnyAccessGroup(route.RequiredAccessGroups))
            return null;

        PublishBlockedEvent(request, StatusCodes.Status403Forbidden, $"rbac:{string.Join(",", route.RequiredAccessGroups)}");
        return WebResult.Text("Forbidden", StatusCodes.Status403Forbidden);
    }

    private void PublishBlockedEvent(WebRequestContext request, int statusCode, string reason)
    {
        _context.Events.Publish(new WebRequestBlockedEvent(
            request.Method,
            request.Path,
            request.ClientIp,
            statusCode,
            reason));
    }

    private bool CheckRateLimit(string clientIp)
    {
        lock (_rateLock)
        {
            var now = DateTime.UtcNow;
            if (_rateLimits.TryGetValue(clientIp, out var window))
            {
                if ((now - window.WindowStart).TotalSeconds > _config.Security.RateLimit.WindowSeconds)
                    window = new RateWindow(now, 0);

                window = window with { Count = window.Count + 1 };
                _rateLimits[clientIp] = window;
                return window.Count <= _config.Security.RateLimit.RequestsPerWindow;
            }

            _rateLimits[clientIp] = new RateWindow(now, 1);
            return true;
        }
    }

    private sealed record RateWindow(DateTime WindowStart, int Count);
}
