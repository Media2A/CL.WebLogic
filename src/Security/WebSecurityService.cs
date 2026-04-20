using System.Security.Cryptography;
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
        // Anonymous-allowed routes skip every check below.
        if (route.AllowAnonymous) return null;

        // A route is "protected" when the author wrote AllowAnonymous = false.
        // Require authentication even if no specific access groups were set.
        // Historically this branch returned null (i.e. "allowed") when the
        // RequiredAccessGroups list was empty, which meant AllowAnonymous =
        // false was silently meaningless unless groups were also populated.
        // That footgun made the previously-exposed /api/weblogic/auth/demo-signin
        // into an auth-bypass primitive and would do the same for any future
        // route that relied on route metadata alone.
        if (!request.IsAuthenticated)
        {
            PublishBlockedEvent(request, StatusCodes.Status401Unauthorized, "auth:required");
            return WebResult.Text("Unauthorized", StatusCodes.Status401Unauthorized);
        }

        if (route.RequiredAccessGroups.Length == 0)
            return null; // authenticated is enough; no group restrictions set

        if (request.HasAnyAccessGroup(route.RequiredAccessGroups))
            return null;

        PublishBlockedEvent(request, StatusCodes.Status403Forbidden, $"rbac:{string.Join(",", route.RequiredAccessGroups)}");
        return WebResult.Text("Forbidden", StatusCodes.Status403Forbidden);
    }

    private const string CsrfSessionKey = "weblogic.csrf_token";
    private const string CsrfFormField = "_csrf";
    private const string CsrfHeader = "X-CSRF-Token";

    private static readonly HashSet<string> CsrfSafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public string GetOrCreateCsrfToken(HttpContext httpContext)
    {
        var existing = httpContext.Session.GetString(CsrfSessionKey);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        httpContext.Session.SetString(CsrfSessionKey, token);
        return token;
    }

    public WebResult? ValidateCsrf(WebRequestContext request)
    {
        if (!_config.Security.EnableCsrf)
            return null;

        if (CsrfSafeMethods.Contains(request.Method))
            return null;

        var sessionToken = request.HttpContext.Session.GetString(CsrfSessionKey);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        var submittedToken = request.HttpContext.Request.Headers[CsrfHeader].FirstOrDefault();
        // Only read the form body when the content-type actually advertises it.
        // ASP.NET's Request.Form accessor throws InvalidOperationException on
        // bodyless POSTs and on JSON/other content-types, which used to bubble
        // up as an unhandled 500 for any endpoint that skipped the X-CSRF header.
        if (string.IsNullOrWhiteSpace(submittedToken) && request.HttpContext.Request.HasFormContentType)
            submittedToken = request.HttpContext.Request.Form.TryGetValue(CsrfFormField, out var formValue) ? formValue.ToString() : null;

        if (string.Equals(sessionToken, submittedToken, StringComparison.Ordinal))
            return null;

        PublishBlockedEvent(request, StatusCodes.Status403Forbidden, "csrf_invalid");
        return WebResult.Text("CSRF validation failed", StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Writes configured security response headers (CSP, HSTS, X-Content-Type-Options, etc.)
    /// to the HTTP response. Safe to call multiple times — headers are set idempotently.
    /// </summary>
    public void ApplySecurityHeaders(HttpContext context)
    {
        var headers = _config.Security.Headers;
        var response = context.Response.Headers;

        if (headers.EnableCsp && !string.IsNullOrWhiteSpace(headers.CspDirectives))
        {
            var headerName = headers.CspReportOnly
                ? "Content-Security-Policy-Report-Only"
                : "Content-Security-Policy";
            response[headerName] = headers.CspDirectives;
        }

        if (headers.EnableHsts && context.Request.IsHttps)
        {
            var hsts = $"max-age={headers.HstsMaxAgeSeconds}";
            if (headers.HstsIncludeSubdomains) hsts += "; includeSubDomains";
            if (headers.HstsPreload) hsts += "; preload";
            response["Strict-Transport-Security"] = hsts;
        }

        if (headers.EnableContentTypeOptions)
            response["X-Content-Type-Options"] = "nosniff";

        if (headers.EnableFrameOptions && !string.IsNullOrWhiteSpace(headers.FrameOptions))
            response["X-Frame-Options"] = headers.FrameOptions;

        if (headers.EnableReferrerPolicy && !string.IsNullOrWhiteSpace(headers.ReferrerPolicy))
            response["Referrer-Policy"] = headers.ReferrerPolicy;

        if (headers.EnablePermissionsPolicy && !string.IsNullOrWhiteSpace(headers.PermissionsPolicy))
            response["Permissions-Policy"] = headers.PermissionsPolicy;
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
