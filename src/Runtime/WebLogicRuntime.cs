using System.Diagnostics;
using CL.WebLogic.Configuration;
using CL.WebLogic.Routing;
using CL.WebLogic.Security;
using CL.WebLogic.Theming;
using CodeLogic.Framework.Libraries;
using Microsoft.AspNetCore.Http;

namespace CL.WebLogic.Runtime;

public sealed class WebLogicRuntime
{
    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private readonly WebRouteRegistry _routes;
    private readonly ThemeManager _themeManager;
    private readonly WebSecurityService _security;
    private readonly IWebRequestAuditStore _auditStore;
    private readonly WebOutputCache _outputCache;
    private readonly List<IWebMiddleware> _globalMiddleware = [];

    public string? ThemeRoot { get; private set; }

    public void UseMiddleware(IWebMiddleware middleware)
    {
        _globalMiddleware.Add(middleware ?? throw new ArgumentNullException(nameof(middleware)));
    }

    public void UseMiddleware(Func<WebRequestContext, WebMiddlewareNext, Task<WebResult>> handler)
    {
        _globalMiddleware.Add(new WebDelegateMiddleware(handler));
    }

    public WebLogicRuntime(
        LibraryContext context,
        WebLogicConfig config,
        WebRouteRegistry routes,
        ThemeManager themeManager,
        WebSecurityService security,
        IWebRequestAuditStore auditStore,
        WebOutputCache outputCache)
    {
        _context = context;
        _config = config;
        _routes = routes;
        _themeManager = themeManager;
        _security = security;
        _auditStore = auditStore;
        _outputCache = outputCache;
    }

    public Task InitializeAsync()
    {
        _context.Logger.Info("CL.WebLogic runtime initialized");
        return Task.CompletedTask;
    }

    public async Task StartAsync()
    {
        ThemeRoot = await _themeManager.ResolveThemeRootAsync().ConfigureAwait(false);
        _context.Logger.Info($"Theme root resolved to: {ThemeRoot}");
        _context.Logger.Info(Theming.CompiledTemplateRegistry.Count > 0
            ? $"Compiled templates: {Theming.CompiledTemplateRegistry.Count} registered (hash-gated fast path active)"
            : "Compiled templates: none registered — AST interpreter serves all renders");
        _themeManager.InitializeCaching(ThemeRoot);
        await _auditStore.InitializeAsync(_context, _config).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        _themeManager.DisposeCaching();
        _context.Logger.Info("CL.WebLogic runtime stopped");
        return Task.CompletedTask;
    }

    public async Task HandleRequestAsync(HttpContext httpContext)
    {
        var started = Stopwatch.StartNew();
        var request = await CreateContextAsync(httpContext).ConfigureAwait(false);
        WebRequestContextAccessor.Current = request;

        try
        {
            var securityResult = await _security.ValidateAsync(request).ConfigureAwait(false);
            if (securityResult is not null)
            {
                await WriteAsync(httpContext, request, securityResult).ConfigureAwait(false);
                await FinalizeRequestAsync(request, securityResult.StatusCode, started.ElapsedMilliseconds, "blocked")
                    .ConfigureAwait(false);
                return;
            }

            if (_routes.TryGet(request.Path, out var route) && route is not null)
            {
                request.Route = route;

                if (!route.Methods.Contains(request.Method))
                {
                    var denied = WebResult.Text("Method not allowed", StatusCodes.Status405MethodNotAllowed);
                    await WriteAsync(httpContext, request, denied).ConfigureAwait(false);
                    await FinalizeRequestAsync(request, denied.StatusCode, started.ElapsedMilliseconds, "method_not_allowed")
                        .ConfigureAwait(false);
                    return;
                }

                var authResult = _security.AuthorizeRoute(request, route);
                if (authResult is not null)
                {
                    await WriteAsync(httpContext, request, authResult).ConfigureAwait(false);
                    await FinalizeRequestAsync(request, authResult.StatusCode, started.ElapsedMilliseconds, "access_denied")
                        .ConfigureAwait(false);
                    return;
                }

                var csrfResult = _security.ValidateCsrf(request);
                if (csrfResult is not null)
                {
                    await WriteAsync(httpContext, request, csrfResult).ConfigureAwait(false);
                    await FinalizeRequestAsync(request, csrfResult.StatusCode, started.ElapsedMilliseconds, "csrf_blocked")
                        .ConfigureAwait(false);
                    return;
                }

                var result = await ExecuteWithMiddlewareAsync(request, route).ConfigureAwait(false);
                await WriteAsync(httpContext, request, result).ConfigureAwait(false);
                await FinalizeRequestAsync(request, result.StatusCode, started.ElapsedMilliseconds, route.Kind.ToString())
                    .ConfigureAwait(false);
                return;
            }

            var ifNoneMatch = httpContext.Request.Headers.IfNoneMatch.FirstOrDefault();
            var ifModifiedSince = httpContext.Request.Headers.IfModifiedSince.FirstOrDefault();
            var asset = await _themeManager.TryReadAssetAsync(request.Path, ThemeRoot, ifNoneMatch, ifModifiedSince).ConfigureAwait(false);
            if (asset is not null)
            {
                await WriteAsync(httpContext, request, asset).ConfigureAwait(false);
                await FinalizeRequestAsync(request, asset.StatusCode, started.ElapsedMilliseconds, "asset")
                    .ConfigureAwait(false);
                return;
            }

            var fallback = _routes.Fallback;
            if (fallback is not null && fallback.Methods.Contains(request.Method))
            {
                request.Route = fallback;

                var authResult = _security.AuthorizeRoute(request, fallback);
                if (authResult is not null)
                {
                    await WriteAsync(httpContext, request, authResult).ConfigureAwait(false);
                    await FinalizeRequestAsync(request, authResult.StatusCode, started.ElapsedMilliseconds, "access_denied")
                        .ConfigureAwait(false);
                    return;
                }

                var result = await ExecuteWithMiddlewareAsync(request, fallback).ConfigureAwait(false);
                await WriteAsync(httpContext, request, result).ConfigureAwait(false);
                await FinalizeRequestAsync(request, result.StatusCode, started.ElapsedMilliseconds, "Fallback")
                    .ConfigureAwait(false);
                return;
            }

            var notFound = WebResult.Text("Not found", StatusCodes.Status404NotFound);
            await WriteAsync(httpContext, request, notFound).ConfigureAwait(false);
            await FinalizeRequestAsync(request, notFound.StatusCode, started.ElapsedMilliseconds, "not_found")
                .ConfigureAwait(false);
        }
        finally
        {
            WebRequestContextAccessor.Current = null;
        }
    }

    private async Task<WebResult> ExecuteWithMiddlewareAsync(WebRequestContext request, WebRouteDefinition route)
    {
        // Page-level output cache. Eligible only on GET requests, and only when
        // the route's policy admits this caller's auth state. AnonymousOnly is
        // the defensive default; authenticated requests skip both reads and
        // writes for that scope.
        var policy = route.OutputCache;
        var cacheEligible = policy is not null
                            && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                            && (policy.Scope != WebOutputCacheScope.AnonymousOnly || !request.IsAuthenticated);

        string? pageKey = null;
        if (cacheEligible && policy is not null)
        {
            pageKey = WebOutputCache.BuildPageKey(
                request.Path,
                policy,
                request.Query,
                request.IsAuthenticated ? request.UserId : null);

            var cached = await _outputCache.TryGetPageAsync(pageKey).ConfigureAwait(false);
            if (cached is not null) return cached;
        }

        var result = await RunPipelineAsync(request, route).ConfigureAwait(false);

        if (pageKey is not null && policy is not null && IsCacheableResponse(result))
            await _outputCache.SetPageAsync(pageKey, result, policy.Ttl).ConfigureAwait(false);

        return result;
    }

    private Task<WebResult> RunPipelineAsync(WebRequestContext request, WebRouteDefinition route)
    {
        var chain = new List<IWebMiddleware>(_globalMiddleware.Count + route.Middleware.Length);
        chain.AddRange(_globalMiddleware);
        chain.AddRange(route.Middleware);

        if (chain.Count == 0)
            return route.Handler(request);

        var index = -1;
        WebMiddlewareNext next = null!;
        next = () =>
        {
            index++;
            return index < chain.Count
                ? chain[index].InvokeAsync(request, next)
                : route.Handler(request);
        };

        return next();
    }

    /// <summary>
    /// A response is only safe to cache when the handler returned a plain success
    /// with no per-request state attached. Anything that writes a Set-Cookie, a
    /// non-200 status, or a redirect must skip the cache.
    /// </summary>
    private static bool IsCacheableResponse(WebResult result)
    {
        if (result.StatusCode != StatusCodes.Status200OK) return false;
        if (result.Headers is null) return true;
        foreach (var key in result.Headers.Keys)
        {
            if (string.Equals(key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }


    private async Task<WebRequestContext> CreateContextAsync(HttpContext httpContext)
    {
        // Resolve the DB-backed session first. The resolver stashes the record on
        // HttpContext.Items so CSRF, sign-in rotation, and sign-out can read it
        // without another round trip. No session → anonymous request.
        var session = await _security.ResolveSessionAsync(httpContext).ConfigureAwait(false);

        var identity = await _security.ResolveIdentityAsync(httpContext).ConfigureAwait(false);

        return new WebRequestContext
        {
            HttpContext = httpContext,
            Method = httpContext.Request.Method.ToUpperInvariant(),
            Path = WebRouteRegistry.NormalizePath(httpContext.Request.Path.Value),
            ClientIp = _security.GetClientIp(httpContext),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            Headers = httpContext.Request.Headers.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            Query = httpContext.Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            Cookies = httpContext.Request.Cookies.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            OutputCache = _outputCache,
            Session = session?.AppData ?? EmptySession,
            Identity = identity
        };
    }

    private static readonly IReadOnlyDictionary<string, string> EmptySession =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private async Task WriteAsync(HttpContext httpContext, WebRequestContext request, WebResult result)
    {
        httpContext.Response.StatusCode = result.StatusCode;
        httpContext.Response.ContentType = result.ContentType;

        _security.ApplySecurityHeaders(httpContext);
        _security.FlushSessionCookie(httpContext);
        await _security.FlushSessionAppDataAsync(httpContext).ConfigureAwait(false);

        if (result.Headers is not null)
        {
            foreach (var header in result.Headers)
                httpContext.Response.Headers[header.Key] = header.Value;
        }

        // Mirror the route's output-cache TTL onto the response so an upstream
        // CDN or the browser can cache the same window. Per-user entries are
        // private to each visitor; never advertise them as shared-cacheable.
        if (request.Route?.OutputCache is { SetClientCacheHeaders: true } cachePolicy
            && cachePolicy.Scope != WebOutputCacheScope.PerUser
            && result.StatusCode == StatusCodes.Status200OK
            && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            && !httpContext.Response.Headers.ContainsKey("Cache-Control"))
        {
            var seconds = (long)Math.Max(1, cachePolicy.Ttl.TotalSeconds);
            httpContext.Response.Headers["Cache-Control"] = $"public, max-age={seconds}";
        }

        if (result.StatusCode == 304)
            return;

        if (result.TemplatePath is not null)
        {
            var effectiveThemeRoot = result.ThemeRoot ?? ThemeRoot;
            var html = await _themeManager.RenderTemplateAsync(
                result.TemplatePath,
                result.Model,
                effectiveThemeRoot,
                request,
                result.Meta).ConfigureAwait(false);
            await httpContext.Response.WriteAsync(html).ConfigureAwait(false);
            return;
        }

        if (result.BinaryBody is not null)
        {
            await httpContext.Response.Body.WriteAsync(result.BinaryBody).ConfigureAwait(false);
            return;
        }

        await httpContext.Response.WriteAsync(result.TextBody ?? string.Empty).ConfigureAwait(false);
    }

    private async Task FinalizeRequestAsync(
        WebRequestContext request,
        int statusCode,
        long durationMs,
        string source)
    {
        _context.Events.Publish(new WebRequestHandledEvent(
            request.Method,
            request.Path,
            request.ClientIp,
            statusCode,
            durationMs));

        await _auditStore.RecordAsync(request, statusCode, durationMs, source).ConfigureAwait(false);
    }
}
