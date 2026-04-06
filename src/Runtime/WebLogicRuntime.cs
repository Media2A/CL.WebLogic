using System.Diagnostics;
using CL.WebLogic.Configuration;
using CL.WebLogic.MySql;
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

    public string? ThemeRoot { get; private set; }

    public WebLogicRuntime(
        LibraryContext context,
        WebLogicConfig config,
        WebRouteRegistry routes,
        ThemeManager themeManager,
        WebSecurityService security,
        IWebRequestAuditStore auditStore)
    {
        _context = context;
        _config = config;
        _routes = routes;
        _themeManager = themeManager;
        _security = security;
        _auditStore = auditStore;
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
        await _auditStore.InitializeAsync(_context, _config).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
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

                var result = await route.Handler(request).ConfigureAwait(false);
                await WriteAsync(httpContext, request, result).ConfigureAwait(false);
                await FinalizeRequestAsync(request, result.StatusCode, started.ElapsedMilliseconds, route.Kind.ToString())
                    .ConfigureAwait(false);
                return;
            }

            var asset = await _themeManager.TryReadAssetAsync(request.Path, ThemeRoot).ConfigureAwait(false);
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

                var result = await fallback.Handler(request).ConfigureAwait(false);
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

    private async Task<WebRequestContext> CreateContextAsync(HttpContext httpContext)
    {
        var session = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (httpContext.Session.IsAvailable)
        {
            foreach (var key in httpContext.Session.Keys)
                session[key] = httpContext.Session.GetString(key) ?? string.Empty;
        }

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
            Session = session,
            Identity = identity
        };
    }

    private async Task WriteAsync(HttpContext httpContext, WebRequestContext request, WebResult result)
    {
        httpContext.Response.StatusCode = result.StatusCode;
        httpContext.Response.ContentType = result.ContentType;

        if (result.TemplatePath is not null)
        {
            var html = await _themeManager.RenderTemplateAsync(
                result.TemplatePath,
                result.Model,
                ThemeRoot,
                request).ConfigureAwait(false);
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
