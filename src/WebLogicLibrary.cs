using CL.Common.Caching;
using CL.WebLogic.Configuration;
using CL.WebLogic.MySql;
using CL.WebLogic.Realtime;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CL.WebLogic.Theming;
using CodeLogic;
using CodeLogic.Framework.Application;
using CodeLogic.Framework.Application.Plugins;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic;

public sealed class WebLogicLibrary : ILibrary
{
    public LibraryManifest Manifest { get; } = new()
    {
        Id = "CL.WebLogic",
        Name = "WebLogic Library",
        Version = "1.0.0",
        Description = "Filesystem-first web runtime for CodeLogic3 with custom routing, themes, and ASP.NET integration",
        Author = "Media2A",
        Tags = ["web", "cms", "routing", "themes", "aspnet"]
    };

    private MemoryCache? _cache;
    private IWebAuthResolver? _authResolver;

    public WebRouteRegistry Routes { get; } = new();
    public WebLogicRegistrationApi Registration { get; }
    public WebLogicRealtimeBridge? RealtimeBridge { get; private set; }
    public WebLogicRuntime? Runtime { get; private set; }

    public WebLogicLibrary()
    {
        Registration = new WebLogicRegistrationApi(Routes);
    }

    public Task OnConfigureAsync(LibraryContext context)
    {
        context.Configuration.Register<WebLogicConfig>("weblogic");
        return Task.CompletedTask;
    }

    public async Task OnInitializeAsync(LibraryContext context)
    {
        var config = context.Configuration.Get<WebLogicConfig>();
        var validation = config.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"{Manifest.Name} configuration is invalid: {string.Join("; ", validation.Errors)}");
        }

        _cache = new MemoryCache(TimeSpan.FromSeconds(30));

        IWebIdentityStore? identityStore = null;
        if (config.Auth.Mode == WebAuthMode.MySql && config.Auth.MySql.Enabled)
        {
            var mySqlStore = new WebMySqlIdentityStore(context, config);
            await mySqlStore.InitializeAsync().ConfigureAwait(false);
            identityStore = mySqlStore;
        }

        var themeManager = new ThemeManager(context, config);
        var authResolver = _authResolver is null || _authResolver is DefaultWebAuthResolver
            ? new DefaultWebAuthResolver(config.Auth, identityStore)
            : _authResolver;

        RealtimeBridge = new WebLogicRealtimeBridge(new WebLogicRealtimeBuffer());
        RealtimeBridge.RegisterDefaultMappings(context.Events);

        var security = new WebSecurityService(context, config, authResolver);
        var auditStore = new WebRequestAuditStore(context, config);

        Runtime = new WebLogicRuntime(
            context,
            config,
            Routes,
            themeManager,
            security,
            auditStore);

        RegisterBuiltInExplorerRoutes();
        await Runtime.InitializeAsync().ConfigureAwait(false);
    }

    public async Task OnStartAsync(LibraryContext context)
    {
        if (Runtime is null)
            throw new InvalidOperationException("WebLogic runtime has not been initialized.");

        await Runtime.StartAsync().ConfigureAwait(false);
    }

    public async Task OnStopAsync()
    {
        if (Runtime is not null)
            await Runtime.StopAsync().ConfigureAwait(false);

        _cache?.Dispose();
        _cache = null;
        RealtimeBridge?.Dispose();
        RealtimeBridge = null;
        Runtime = null;
    }

    public Task<HealthStatus> HealthCheckAsync()
    {
        if (Runtime is null)
            return Task.FromResult(HealthStatus.Unhealthy("WebLogic runtime is not initialized"));

        return Task.FromResult(HealthStatus.Healthy(
            $"routes={Routes.Count}, themeRoot={Runtime.ThemeRoot ?? "(unresolved)"}"));
    }

    public void Dispose()
    {
        _cache?.Dispose();
        _cache = null;
        RealtimeBridge?.Dispose();
        RealtimeBridge = null;
    }

    public void UseAuthResolver(IWebAuthResolver authResolver)
    {
        _authResolver = authResolver ?? throw new ArgumentNullException(nameof(authResolver));
    }

    public WebRegistrationContext CreateApplicationContext(ApplicationManifest manifest) =>
        Registration.CreateContext(new WebContributorDescriptor
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Kind = "Application",
            Description = manifest.Description ?? string.Empty
        });

    public WebRegistrationContext CreatePluginContext(PluginManifest manifest) =>
        Registration.CreateContext(new WebContributorDescriptor
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Kind = "Plugin",
            Description = manifest.Description ?? string.Empty
        });

    public async Task RegisterContributorAsync(WebContributorDescriptor contributor, IWebRouteContributor routeContributor)
    {
        var context = Registration.CreateContext(contributor);
        await routeContributor.RegisterRoutesAsync(context).ConfigureAwait(false);
    }

    public void RegisterPage(string path, WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterPage(path, handler, methods);

    public void RegisterApi(string path, WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterApi(path, handler, methods);

    public void RegisterFallback(WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterFallback(handler, methods);

    public static WebLogicLibrary GetRequired() =>
        Libraries.Get<WebLogicLibrary>()
        ?? throw new InvalidOperationException("CL.WebLogic is not loaded.");

    private void RegisterBuiltInExplorerRoutes()
    {
        var explorerContributor = Registration.CreateContext(new WebContributorDescriptor
        {
            Id = "weblogic.core",
            Name = "CL.WebLogic Core",
            Kind = "Library",
            Description = "Built-in route discovery and API explorer endpoints."
        });

        explorerContributor.RegisterApi("/api/weblogic/routes", new WebRouteOptions
        {
            Name = "WebLogic Route Registry",
            Description = "Lists every registered page, API, and fallback route.",
            Tags = ["weblogic", "explorer", "routes"]
        }, _ => Task.FromResult(WebResult.Json(new
        {
            generatedUtc = DateTime.UtcNow,
            routes = Routes.GetRouteDescriptors()
        })), "GET");

        explorerContributor.RegisterApi("/api/weblogic/plugins", new WebRouteOptions
        {
            Name = "WebLogic Contributors",
            Description = "Lists route contributors including application, plugin, and core registrations.",
            Tags = ["weblogic", "explorer", "plugins"]
        }, _ => Task.FromResult(WebResult.Json(new
        {
            generatedUtc = DateTime.UtcNow,
            contributors = Routes.GetContributors()
        })), "GET");

        explorerContributor.RegisterApi("/api/weblogic/apiexplorer", new WebRouteOptions
        {
            Name = "WebLogic API Explorer",
            Description = "Lists API endpoints known to the WebLogic runtime.",
            Tags = ["weblogic", "explorer", "api"]
        }, _ => Task.FromResult(WebResult.Json(new
        {
            generatedUtc = DateTime.UtcNow,
            apis = Routes.GetRouteDescriptors()
                .Where(static route => string.Equals(route.Kind, nameof(WebRouteKind.Api), StringComparison.OrdinalIgnoreCase))
                .ToArray()
        })), "GET");

        explorerContributor.RegisterPage("/weblogic/apiexplorer", new WebRouteOptions
        {
            Name = "WebLogic API Explorer Page",
            Description = "Simple built-in HTML view of registered API routes.",
            Tags = ["weblogic", "explorer", "api"]
        }, _ => Task.FromResult(WebResult.Html(BuildApiExplorerHtml())), "GET");

        explorerContributor.RegisterApi("/api/weblogic/events/recent", new WebRouteOptions
        {
            Name = "WebLogic Recent Events",
            Description = "Returns recent WebLogic realtime events for dashboards and SignalR replay.",
            Tags = ["weblogic", "explorer", "events", "signalr"]
        }, request =>
        {
            var take = 50;
            _ = int.TryParse(request.GetQuery("take"), out take);
            take = Math.Clamp(take, 1, 250);

            var events = (RealtimeBridge?.GetRecent(take) ?? [])
                .Where(evt => CanViewEvent(request, evt))
                .Select(static evt => evt.ToHubMessage())
                .ToArray();

            return Task.FromResult(WebResult.Json(new
            {
                generatedUtc = DateTime.UtcNow,
                take,
                events
            }));
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/auth/me", new WebRouteOptions
        {
            Name = "WebLogic Current User",
            Description = "Returns the current request user and access groups.",
            Tags = ["weblogic", "auth", "me"]
        }, request => Task.FromResult(WebResult.Json(new
        {
            isAuthenticated = request.IsAuthenticated,
            request.UserId,
            accessGroups = request.AccessGroups,
            request.Path,
            request.Method
        })), "GET");

        explorerContributor.RegisterApi("/api/weblogic/auth/demo-signin", new WebRouteOptions
        {
            Name = "WebLogic Demo Sign-In",
            Description = "Stores a demo user id in session so auth and SignalR pages can show RBAC behavior.",
            Tags = ["weblogic", "auth", "demo"]
        }, async request =>
        {
            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var userId = form.GetValueOrDefault("userId")
                ?? request.GetQuery("userId")
                ?? "demo-admin";

            request.SetSessionValue("weblogic.user_id", userId);

            return WebResult.Json(new
            {
                signedIn = true,
                userId
            });
        }, "GET", "POST");

        explorerContributor.RegisterApi("/api/weblogic/auth/signout", new WebRouteOptions
        {
            Name = "WebLogic Demo Sign-Out",
            Description = "Clears demo auth session values.",
            Tags = ["weblogic", "auth", "demo"]
        }, request =>
        {
            request.SetSessionValue("weblogic.user_id", null);
            request.SetSessionValue("weblogic.access_groups", null);

            return Task.FromResult(WebResult.Json(new
            {
                signedIn = false
            }));
        }, "GET", "POST");

        explorerContributor.RegisterPage("/weblogic/live", new WebRouteOptions
        {
            Name = "WebLogic Live Events",
            Description = "Live event page powered by WebLogic realtime events and SignalR.",
            Tags = ["weblogic", "live", "signalr"]
        }, _ => Task.FromResult(WebResult.Html(BuildLiveEventsHtml())), "GET");

        explorerContributor.RegisterPage("/weblogic/auth-demo", new WebRouteOptions
        {
            Name = "WebLogic Auth Demo",
            Description = "Demo page for session-based sign in and RBAC behavior.",
            Tags = ["weblogic", "auth", "demo"]
        }, request => Task.FromResult(WebResult.Html(BuildAuthDemoHtml(request))), "GET");
    }

    private string BuildApiExplorerHtml()
    {
        var apis = Routes.GetRouteDescriptors()
            .Where(static route => string.Equals(route.Kind, nameof(WebRouteKind.Api), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var items = string.Join(Environment.NewLine, apis.Select(api =>
            $"""
            <article class="api-card">
                <h2>{System.Net.WebUtility.HtmlEncode(api.Path)}</h2>
                <p>{System.Net.WebUtility.HtmlEncode(api.Description)}</p>
                <p><strong>Methods:</strong> {System.Net.WebUtility.HtmlEncode(string.Join(", ", api.Methods))}</p>
                <p><strong>Source:</strong> {System.Net.WebUtility.HtmlEncode(api.SourceName)} ({System.Net.WebUtility.HtmlEncode(api.SourceKind)})</p>
                <p><strong>Access:</strong> {(api.AllowAnonymous ? "Anonymous" : System.Net.WebUtility.HtmlEncode(string.Join(", ", api.RequiredAccessGroups)))}</p>
            </article>
            """));

        return
            $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CL.WebLogic API Explorer</title>
                <style>
                    body { font-family: Segoe UI, sans-serif; margin: 0; background: #f6f2ea; color: #1c2830; }
                    main { max-width: 960px; margin: 0 auto; padding: 48px 24px 72px; }
                    .eyebrow { text-transform: uppercase; letter-spacing: 0.12em; color: #1b7f6a; font-size: 12px; }
                    .api-card { background: white; border-radius: 18px; padding: 20px; margin-top: 18px; box-shadow: 0 18px 40px rgba(31,43,47,0.08); }
                    a { color: #1b7f6a; }
                </style>
            </head>
            <body>
                <main>
                    <p class="eyebrow">CL.WebLogic</p>
                    <h1>API Explorer</h1>
                    <p>This built-in page shows every API route currently registered in the runtime.</p>
                    <p><a href="/api/weblogic/apiexplorer">JSON API explorer</a> | <a href="/api/weblogic/routes">All routes</a> | <a href="/api/weblogic/plugins">Contributors</a></p>
                    {{items}}
                </main>
            </body>
            </html>
            """.Replace("{{items}}", items, StringComparison.Ordinal);
    }

    private static bool CanViewEvent(WebRequestContext request, WebLogicRealtimeEnvelope evt)
    {
        return evt.Audience switch
        {
            WebLogicRealtimeAudience.Public => true,
            WebLogicRealtimeAudience.Authenticated => request.IsAuthenticated,
            WebLogicRealtimeAudience.AccessGroup => request.HasAnyAccessGroup(evt.AccessGroups),
            WebLogicRealtimeAudience.Internal => request.IsAuthenticated,
            _ => false
        };
    }

    private static string BuildLiveEventsHtml()
    {
        return
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CL.WebLogic Live Events</title>
                <link rel="stylesheet" href="/assets/vendor/bootstrap/bootstrap.min.css">
                <link rel="stylesheet" href="/assets/site.css">
            </head>
            <body>
                <div class="site-shell">
                    <section class="hero-panel">
                        <p class="eyebrow">CL.WebLogic Live</p>
                        <h1 class="hero-title">SignalR event stream</h1>
                        <p class="hero-copy">This page listens to the built-in WebLogic realtime hub and falls back to the recent-events API when needed.</p>
                        <div class="hero-actions">
                            <a class="btn btn-warning fw-semibold" href="/">Back home</a>
                            <a class="btn btn-outline-light" href="/api/weblogic/events/recent">Recent events API</a>
                            <a class="btn btn-outline-light" href="/weblogic/auth-demo">Auth demo</a>
                        </div>
                        <div class="mt-3">
                            <span class="hero-badge">SignalR: <span data-signalr-status>connecting</span></span>
                            <span class="hero-badge">Hub: <code>/weblogic-hubs/events</code></span>
                        </div>
                    </section>
                    <section class="section-wrap">
                        <div class="demo-panel">
                            <div class="section-header">
                                <div>
                                    <h2 class="section-title">Realtime feed</h2>
                                    <p class="section-subtitle">Server events appear here as requests, plugins, themes, and runtime state changes happen.</p>
                                </div>
                                <button class="btn btn-outline-light" type="button" data-refresh-dashboard>Refresh snapshot</button>
                            </div>
                            <ul class="list-group list-group-flush activity-list mt-3" data-live-feed>
                                <li class="list-group-item text-secondary">Waiting for the first WebLogic event.</li>
                            </ul>
                        </div>
                    </section>
                    <footer class="footer-bar d-flex flex-wrap justify-content-between gap-3">
                        <span>Realtime is part of CL.WebLogic itself, not only the sample app.</span>
                        <span data-current-year></span>
                    </footer>
                </div>
                <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@9.0.6/dist/browser/signalr.min.js"></script>
                <script src="/assets/vendor/jquery/jquery-4.0.0.min.js"></script>
                <script src="/assets/vendor/bootstrap/bootstrap.bundle.min.js"></script>
                <script src="/assets/site.js"></script>
            </body>
            </html>
            """;
    }

    private static string BuildAuthDemoHtml(WebRequestContext request)
    {
        var currentUser = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
        var currentGroups = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups);

        return
            $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CL.WebLogic Auth Demo</title>
                <link rel="stylesheet" href="/assets/vendor/bootstrap/bootstrap.min.css">
                <link rel="stylesheet" href="/assets/site.css">
            </head>
            <body>
                <div class="site-shell">
                    <section class="hero-panel">
                        <p class="eyebrow">CL.WebLogic Auth</p>
                        <h1 class="hero-title">Session and RBAC demo</h1>
                        <p class="hero-copy">Use the built-in demo endpoints to sign in as a database-backed user and see access groups flow into pages, APIs, and SignalR.</p>
                        <div class="hero-actions">
                            <a class="btn btn-warning fw-semibold" href="/">Back home</a>
                            <a class="btn btn-outline-light" href="/api/weblogic/auth/me">Current user API</a>
                            <a class="btn btn-outline-light" href="/api/plugins/secure">Secure API</a>
                        </div>
                    </section>
                    <section class="section-wrap">
                        <div class="row g-4">
                            <div class="col-lg-6">
                                <div class="demo-panel h-100">
                                    <p class="section-title">Current session</p>
                                    <p class="section-subtitle">This reflects the same request identity WebLogic uses for RBAC checks.</p>
                                    <div class="mt-3">
                                        <p class="mb-2">User: <code data-current-user>{{currentUser}}</code></p>
                                        <p class="mb-2">Groups: <code data-current-groups>{{currentGroups}}</code></p>
                                        <p class="mb-0">Authenticated: <code>{{request.IsAuthenticated}}</code></p>
                                    </div>
                                </div>
                            </div>
                            <div class="col-lg-6">
                                <div class="demo-panel h-100">
                                    <p class="section-title">Demo actions</p>
                                    <p class="section-subtitle">This is intentionally lightweight foundation code, not a full login product yet.</p>
                                    <form class="d-grid gap-3 mt-3" data-auth-demo-form>
                                        <input class="form-control form-control-lg" type="text" name="userId" value="demo-admin" placeholder="demo-admin">
                                        <div class="d-flex gap-2 flex-wrap">
                                            <button class="btn btn-warning fw-semibold" type="submit">Sign in demo user</button>
                                            <button class="btn btn-outline-light" type="button" data-auth-signout>Sign out</button>
                                        </div>
                                    </form>
                                    <div class="code-shell mt-3">
                                        <pre class="mb-0"><code>GET /api/weblogic/auth/me&#10;POST /api/weblogic/auth/demo-signin&#10;POST /api/weblogic/auth/signout</code></pre>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </section>
                    <footer class="footer-bar d-flex flex-wrap justify-content-between gap-3">
                        <span>Current request path: {{request.Path}}</span>
                        <span data-current-year></span>
                    </footer>
                </div>
                <script src="/assets/vendor/jquery/jquery-4.0.0.min.js"></script>
                <script src="/assets/vendor/bootstrap/bootstrap.bundle.min.js"></script>
                <script src="/assets/site.js"></script>
            </body>
            </html>
            """;
    }
}
