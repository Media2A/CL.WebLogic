using System.Security.Cryptography;
using System.Text;
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
    internal const string SessionCookieItemKey = "weblogic.session";
    internal const string SessionClearFlagItemKey = "weblogic.session.clear";

    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private readonly IWebSessionStore? _sessionStore;
    private readonly IWebPermissionResolver? _permissionResolver;
    private readonly Dictionary<string, RateWindow> _rateLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateLock = new();

    public WebSecurityService(
        LibraryContext context,
        WebLogicConfig config,
        IWebSessionStore? sessionStore,
        IWebPermissionResolver? permissionResolver)
    {
        _context = context;
        _config = config;
        _sessionStore = sessionStore;
        _permissionResolver = permissionResolver;
    }

    public string GetClientIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Resolve the active session from the request cookie, if any. Called by the
    /// runtime before building the request context. On success the returned record
    /// is also stashed in <c>HttpContext.Items</c> so downstream code (CSRF, sign-in
    /// rotation, sign-out, realtime hub) can read it without a second DB lookup.
    /// </summary>
    public async Task<WebSessionRecord?> ResolveSessionAsync(HttpContext httpContext)
    {
        if (_sessionStore is null)
            return null;

        if (!httpContext.Request.Cookies.TryGetValue(_config.Session.CookieName, out var token) || string.IsNullOrWhiteSpace(token))
            return null;

        var session = await _sessionStore.GetAsync(token).ConfigureAwait(false);
        if (session is null)
        {
            // Stale cookie. Mark it for clearing on the way out.
            httpContext.Items[SessionClearFlagItemKey] = true;
            return null;
        }

        if (session.IsExpired(DateTime.UtcNow))
        {
            await _sessionStore.RevokeAsync(token).ConfigureAwait(false);
            httpContext.Items[SessionClearFlagItemKey] = true;
            return null;
        }

        if (_config.Session.BindToClientIp && session.IpHash is not null)
        {
            var currentHash = HashIp(GetClientIp(httpContext));
            if (!CryptographicOperations.FixedTimeEquals(currentHash, session.IpHash))
            {
                await _sessionStore.RevokeAsync(token).ConfigureAwait(false);
                httpContext.Items[SessionClearFlagItemKey] = true;
                _context.Logger.Warning($"Session IP bind mismatch for user {session.UserId}; session revoked.");
                return null;
            }
        }

        httpContext.Items[SessionCookieItemKey] = session;

        // Sliding expiry: advance last-seen and push expires_at forward. Not awaited
        // on the hot path — failures here shouldn't block the request, and the store
        // is expected to be idempotent.
        _ = _sessionStore.TouchAsync(token, DateTime.UtcNow);

        return session;
    }

    /// <summary>
    /// Build the request-identity snapshot for <see cref="WebRequestContext"/>.
    /// Identity comes only from the session row resolved by
    /// <see cref="ResolveSessionAsync"/>; there is no header-based or
    /// ASP.NET-session-based fallback anymore. Permissions come from the resolver
    /// (which is expected to cache internally) so every request can pay only an
    /// in-process lookup after warm-up.
    /// </summary>
    public async Task<WebRequestIdentity> ResolveIdentityAsync(HttpContext httpContext)
    {
        var session = httpContext.Items[SessionCookieItemKey] as WebSessionRecord;
        if (session is null)
            return new WebRequestIdentity(null, null, null);

        IReadOnlyCollection<string> permissions = [];
        if (_permissionResolver is not null)
            permissions = await _permissionResolver.GetPermissionsAsync(session.UserId).ConfigureAwait(false);

        return new WebRequestIdentity(session.UserId, session.AccessGroups, permissions);
    }

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
        if (route.AllowAnonymous) return null;

        if (!request.IsAuthenticated)
        {
            PublishBlockedEvent(request, StatusCodes.Status401Unauthorized, "auth:required");
            return WebResult.Text("Unauthorized", StatusCodes.Status401Unauthorized);
        }

        if (route.RequiredAccessGroups.Length > 0
            && !request.HasAnyAccessGroup(route.RequiredAccessGroups))
        {
            PublishBlockedEvent(request, StatusCodes.Status403Forbidden, $"rbac:{string.Join(",", route.RequiredAccessGroups)}");
            return WebResult.Text("Forbidden", StatusCodes.Status403Forbidden);
        }

        if (!string.IsNullOrWhiteSpace(route.RequiredPermission)
            && !request.HasPermission(route.RequiredPermission))
        {
            PublishBlockedEvent(request, StatusCodes.Status403Forbidden, $"perm:{route.RequiredPermission}");
            return WebResult.Text("Forbidden", StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private const string CsrfFormField = "_csrf";
    private const string CsrfHeader = "X-CSRF-Token";

    private static readonly HashSet<string> CsrfSafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    /// <summary>
    /// Return the CSRF token bound to the active session. No session → no token —
    /// callers that need a token for a first POST must sign in first. Previously the
    /// token lived in ASP.NET session state keyed by a cookie; now it lives on the
    /// session row and rotates naturally whenever the session does.
    /// </summary>
    public string GetOrCreateCsrfToken(HttpContext httpContext)
    {
        var session = httpContext.Items[SessionCookieItemKey] as WebSessionRecord;
        return session?.CsrfToken ?? string.Empty;
    }

    public WebResult? ValidateCsrf(WebRequestContext request)
    {
        if (!_config.Security.EnableCsrf)
            return null;

        if (CsrfSafeMethods.Contains(request.Method))
            return null;

        var session = request.HttpContext.Items[SessionCookieItemKey] as WebSessionRecord;
        if (session is null)
        {
            // No session → no CSRF token. Fail closed.
            PublishBlockedEvent(request, StatusCodes.Status403Forbidden, "csrf_missing_session");
            return WebResult.Text("CSRF validation failed", StatusCodes.Status403Forbidden);
        }

        var submittedToken = request.HttpContext.Request.Headers[CsrfHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(submittedToken) && request.HttpContext.Request.HasFormContentType)
            submittedToken = request.HttpContext.Request.Form.TryGetValue(CsrfFormField, out var formValue) ? formValue.ToString() : null;

        if (!string.IsNullOrEmpty(submittedToken)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(session.CsrfToken),
                Encoding.ASCII.GetBytes(submittedToken)))
        {
            return null;
        }

        PublishBlockedEvent(request, StatusCodes.Status403Forbidden, "csrf_invalid");
        return WebResult.Text("CSRF validation failed", StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Issue a new session for <paramref name="userId"/>, revoking any session already
    /// bound to the inbound cookie first. The cookie on the outgoing response is set
    /// to the new token. Returns the created record so the caller can read
    /// <see cref="WebSessionRecord.CsrfToken"/> if it needs to echo it into the
    /// post-login response body.
    /// </summary>
    public async Task<WebSessionRecord> RotateAfterSignInAsync(
        HttpContext httpContext,
        string userId,
        IReadOnlyList<string> accessGroups,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStore is null)
            throw new InvalidOperationException("No IWebSessionStore is registered; cannot mint a session.");

        // Revoke the previous session (if any) so a stolen pre-login token can't
        // ride along with elevated rights.
        if (httpContext.Request.Cookies.TryGetValue(_config.Session.CookieName, out var oldToken) && !string.IsNullOrWhiteSpace(oldToken))
        {
            await _sessionStore.RevokeAsync(oldToken, cancellationToken).ConfigureAwait(false);
        }

        var ipHash = HashIp(GetClientIp(httpContext));
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var uaHash = string.IsNullOrWhiteSpace(userAgent) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(userAgent));

        var created = await _sessionStore.CreateAsync(new WebSessionCreate
        {
            UserId = userId,
            AccessGroups = accessGroups,
            RememberMe = rememberMe,
            MaxConcurrentSessions = _config.Session.MaxConcurrentSessions,
            IdleTimeout = TimeSpan.FromMinutes(_config.Session.IdleTimeoutMinutes),
            RememberMeLifetime = TimeSpan.FromDays(_config.Session.RememberMeDays),
            IpHash = ipHash,
            UserAgentHash = uaHash
        }, cancellationToken).ConfigureAwait(false);

        WriteSessionCookie(httpContext, created);
        httpContext.Items[SessionCookieItemKey] = created;
        return created;
    }

    /// <summary>Revoke the current session and clear the cookie on the response.</summary>
    public async Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (_sessionStore is not null
            && httpContext.Request.Cookies.TryGetValue(_config.Session.CookieName, out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            await _sessionStore.RevokeAsync(token, cancellationToken).ConfigureAwait(false);
        }

        httpContext.Items.Remove(SessionCookieItemKey);
        ClearSessionCookie(httpContext);
    }

    /// <summary>Called on the response path — clears the cookie if the inbound session was invalid.</summary>
    public void FlushSessionCookie(HttpContext httpContext)
    {
        if (httpContext.Items[SessionClearFlagItemKey] is true)
            ClearSessionCookie(httpContext);
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

    private static byte[] HashIp(string ip) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(ip));

    private void WriteSessionCookie(HttpContext httpContext, WebSessionRecord session)
    {
        var options = BuildCookieOptions(session.IsRememberMe ? session.ExpiresAtUtc : null);
        httpContext.Response.Cookies.Append(_config.Session.CookieName, session.Token, options);
    }

    private void ClearSessionCookie(HttpContext httpContext)
    {
        var options = BuildCookieOptions(DateTimeOffset.UnixEpoch);
        httpContext.Response.Cookies.Delete(_config.Session.CookieName, options);
    }

    private CookieOptions BuildCookieOptions(DateTimeOffset? expires)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = _config.Session.CookieSecure,
            SameSite = _config.Session.CookieSameSite switch
            {
                SessionSameSite.Strict => SameSiteMode.Strict,
                SessionSameSite.None => SameSiteMode.None,
                _ => SameSiteMode.Lax
            },
            Path = "/"
        };

        if (!string.IsNullOrWhiteSpace(_config.Session.CookieDomain))
            opts.Domain = _config.Session.CookieDomain;

        if (expires.HasValue)
            opts.Expires = expires.Value;

        return opts;
    }

    private sealed record RateWindow(DateTime WindowStart, int Count);
}
