using CL.Common.Caching;
using CL.WebLogic.Configuration;
using CL.WebLogic.Forms;
using CL.WebLogic.Realtime;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CL.WebLogic.Theming;
using CodeLogic;
using CodeLogic.Framework.Application;
using CodeLogic.Framework.Application.Plugins;
using CodeLogic.Framework.Libraries;
using System.Reflection;
using System.Text.Json;

namespace CL.WebLogic;

public sealed class WebLogicLibrary : ILibrary
{
    private readonly HashSet<string> _registeredPluginContributorIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pluginRegistrationLock = new();
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
    private ThemeManager? _themeManager;
    private PluginManager? _attachedPluginManager;
    private IWebIdentityStore? _configuredIdentityStore;
    private IWebDashboardLayoutStore? _configuredDashboardLayouts;
    private IWebRequestAuditStore? _configuredAuditStore;
    private WebLogicConfig? _resolvedConfig;
    public IWebWidgetSettingsStore? WidgetSettingsStore { get; private set; }
    public IWebIdentityStore? IdentityStore { get; private set; }
    public IWebDashboardLayoutStore? DashboardLayouts { get; private set; }
    public WebFormOptionsRegistry FormOptionsProviders { get; } = new();

    public WebRouteRegistry Routes { get; } = new();
    public WebWidgetRegistry Widgets { get; } = new();
    public WebLogicRegistrationApi Registration { get; }
    public WebLogicRealtimeBridge? RealtimeBridge { get; private set; }
    public WebLogicRuntime? Runtime { get; private set; }

    public WebLogicLibrary()
    {
        Registration = new WebLogicRegistrationApi(Routes, Widgets);
    }

    public Task OnConfigureAsync(LibraryContext context)
    {
        context.Configuration.Register<WebLogicConfig>("weblogic");
        return Task.CompletedTask;
    }

    public async Task OnInitializeAsync(LibraryContext context)
    {
        var config = context.Configuration.Get<WebLogicConfig>();
        _resolvedConfig = config;
        var validation = config.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"{Manifest.Name} configuration is invalid: {string.Join("; ", validation.Errors)}");
        }

        _cache = new MemoryCache(TimeSpan.FromSeconds(30));

        IdentityStore = _configuredIdentityStore;
        DashboardLayouts = _configuredDashboardLayouts ?? new FileWebDashboardLayoutStore(context);

        IWebWidgetSettingsStore? widgetSettingsStore = config.Widgets.EnablePersistentSettings
            ? new FileWebWidgetSettingsStore(context, config)
            : null;

        WidgetSettingsStore = widgetSettingsStore;

        var themeManager = new ThemeManager(context, config, Widgets, widgetSettingsStore);
        _themeManager = themeManager;
        var authResolver = _authResolver is null || _authResolver is DefaultWebAuthResolver
            ? new DefaultWebAuthResolver(config.Auth, IdentityStore)
            : _authResolver;

        RealtimeBridge = new WebLogicRealtimeBridge(new WebLogicRealtimeBuffer());
        RealtimeBridge.RegisterDefaultMappings(context.Events);

        var security = new WebSecurityService(context, config, authResolver);
        themeManager.SetSecurityService(security);
        var auditStore = _configuredAuditStore ?? NullWebRequestAuditStore.Instance;

        Runtime = new WebLogicRuntime(
            context,
            config,
            Routes,
            themeManager,
            security,
            auditStore);

        Registration.SetRuntime(Runtime);
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
        IdentityStore = null;
        DashboardLayouts = null;
        WidgetSettingsStore = null;
        _themeManager = null;
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
        IdentityStore = null;
        DashboardLayouts = null;
        WidgetSettingsStore = null;
        _themeManager = null;
    }

    public void UseAuthResolver(IWebAuthResolver authResolver)
    {
        _authResolver = authResolver ?? throw new ArgumentNullException(nameof(authResolver));
    }

    public void UseIdentityStore(IWebIdentityStore identityStore)
    {
        _configuredIdentityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        IdentityStore = identityStore;
    }

    public void UseDashboardLayoutStore(IWebDashboardLayoutStore dashboardLayoutStore)
    {
        _configuredDashboardLayouts = dashboardLayoutStore ?? throw new ArgumentNullException(nameof(dashboardLayoutStore));
        DashboardLayouts = dashboardLayoutStore;
    }

    public void UseRequestAuditStore(IWebRequestAuditStore auditStore)
    {
        _configuredAuditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
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

    public async Task RegisterLoadedPluginsAsync(PluginManager? pluginManager = null)
    {
        pluginManager ??= CodeLogic.CodeLogic.GetPluginManager();
        if (pluginManager is null)
            return;

        AttachPluginManager(pluginManager);

        foreach (var plugin in pluginManager.GetAllPlugins())
            await RegisterPluginContributorAsync(plugin).ConfigureAwait(false);
    }

    public void AttachPluginManager(PluginManager pluginManager)
    {
        ArgumentNullException.ThrowIfNull(pluginManager);

        if (ReferenceEquals(_attachedPluginManager, pluginManager))
            return;

        if (_attachedPluginManager is not null)
            _attachedPluginManager.OnPluginLoaded -= HandlePluginLoaded;

        _attachedPluginManager = pluginManager;
        _attachedPluginManager.OnPluginLoaded += HandlePluginLoaded;
    }

    public void RegisterPage(string path, WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterPage(path, handler, methods);

    public void RegisterApi(string path, WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterApi(path, handler, methods);

    public void RegisterFallback(WebRouteHandler handler, params string[] methods) =>
        Routes.RegisterFallback(handler, methods);

    public void RegisterWidget(
        string name,
        WebWidgetHandler handler,
        WebContributorDescriptor? contributor = null,
        WebWidgetOptions? options = null)
    {
        Widgets.Register(
            name,
            handler,
            contributor ?? new WebContributorDescriptor
            {
                Id = "application",
                Name = "Application",
                Kind = "Application",
                Description = string.Empty
            },
            options);
    }

    public static WebLogicLibrary GetRequired() =>
        Libraries.Get<WebLogicLibrary>()
        ?? throw new InvalidOperationException("CL.WebLogic is not loaded.");

    public WebLogicConfig? GetConfig() => _resolvedConfig;

    private async void HandlePluginLoaded(string pluginId)
    {
        try
        {
            var plugin = _attachedPluginManager?
                .GetAllPlugins()
                .FirstOrDefault(candidate => string.Equals(candidate.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));

            if (plugin is not null)
                await RegisterPluginContributorAsync(plugin).ConfigureAwait(false);
        }
        catch
        {
            // Keep plugin-load event handling non-fatal for the host.
        }
    }

    private async Task RegisterPluginContributorAsync(IPlugin plugin)
    {
        if (plugin is not IWebRouteContributor routeContributor)
            return;

        var contributor = CreatePluginContext(plugin.Manifest).Contributor;

        lock (_pluginRegistrationLock)
        {
            if (!_registeredPluginContributorIds.Add(contributor.Id))
                return;
        }

        try
        {
            await RegisterContributorAsync(contributor, routeContributor).ConfigureAwait(false);
        }
        catch
        {
            lock (_pluginRegistrationLock)
                _registeredPluginContributorIds.Remove(contributor.Id);
            throw;
        }
    }

    public Task<WebDashboardLayoutRecord?> GetDashboardLayoutAsync(
        string ownerKey,
        string dashboardKey,
        CancellationToken cancellationToken = default) =>
        DashboardLayouts?.GetAsync(ownerKey, dashboardKey, cancellationToken)
        ?? Task.FromResult<WebDashboardLayoutRecord?>(null);

    public async Task<WebDashboardLayoutRecord> SaveDashboardLayoutAsync(
        string ownerKey,
        string dashboardKey,
        IReadOnlyList<WebDashboardWidgetPlacement> widgets,
        CancellationToken cancellationToken = default)
    {
        if (DashboardLayouts is null)
            throw new InvalidOperationException("Dashboard layouts are unavailable.");

        foreach (var widget in widgets.Where(static item => item.Settings.Count > 0))
        {
            if (WidgetSettingsStore is null || string.IsNullOrWhiteSpace(widget.InstanceId) || string.IsNullOrWhiteSpace(widget.WidgetName))
                continue;

            await WidgetSettingsStore.UpsertAsync(widget.InstanceId, widget.WidgetName, widget.Settings).ConfigureAwait(false);
        }

        return await DashboardLayouts.SaveAsync(ownerKey, dashboardKey, widgets, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RenderDashboardZoneAsync(
        string ownerKey,
        string dashboardKey,
        string zone,
        WebRequestContext request,
        CancellationToken cancellationToken = default)
    {
        var layout = await GetDashboardLayoutAsync(ownerKey, dashboardKey, cancellationToken).ConfigureAwait(false);
        var widgets = (layout?.Widgets ?? [])
            .Where(item => string.Equals(item.Zone, zone, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Order)
            .ToArray();

        return await RenderDashboardWidgetsAsync(request, widgets).ConfigureAwait(false);
    }

    private async Task<WebWidgetContext> BuildWidgetContextAsync(
        WebWidgetDefinition widget,
        WebRequestContext request,
        IReadOnlyDictionary<string, string> incomingParameters,
        string? instanceId)
    {
        var mergedParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in widget.SampleParameters)
            mergedParameters[pair.Key] = pair.Value;

        foreach (var pair in incomingParameters)
            mergedParameters[pair.Key] = pair.Value;

        if (!string.IsNullOrWhiteSpace(instanceId) && WidgetSettingsStore is not null)
        {
            var record = await WidgetSettingsStore.GetAsync(instanceId).ConfigureAwait(false);
            if (record is not null && (string.IsNullOrWhiteSpace(record.WidgetName) || string.Equals(record.WidgetName, widget.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var pair in record.Settings)
                    mergedParameters[pair.Key] = pair.Value;
            }
        }

        return new WebWidgetContext
        {
            Name = widget.Name,
            InstanceId = instanceId,
            Parameters = mergedParameters,
            Model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            Request = request,
            Contributor = widget.Contributor,
            SettingsStore = WidgetSettingsStore
        };
    }

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

        explorerContributor.RegisterApi("/weblogic/client/weblogic.client.js", new WebRouteOptions
        {
            Name = "WebLogic Client Runtime",
            Description = "Serves the CL.WebLogic.Client browser runtime owned by the library itself.",
            Tags = ["weblogic", "client", "assets"]
        }, _ => Task.FromResult(WebResult.Bytes(
            LoadEmbeddedClientRuntime(),
            "application/javascript; charset=utf-8")), "GET");

        explorerContributor.RegisterApi("/api/weblogic/forms/options", new WebRouteOptions
        {
            Name = "WebLogic Form Options",
            Description = "Resolves dynamic options for a form field provider.",
            Tags = ["weblogic", "forms", "options"]
        }, async request =>
        {
            var formId = request.GetQuery("formId");
            var fieldName = request.GetQuery("field");
            if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(fieldName))
                return WebResult.Text("formId and field are required.", 400);

            var definition = WebFormBinder.GetDefinitionById(formId);
            if (definition is null)
                return WebResult.Text("Form definition not found.", 404);

            var field = definition.Fields.FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                return WebResult.Text("Field definition not found.", 404);

            if (string.IsNullOrWhiteSpace(field.OptionsProvider))
                return WebResult.Json(new { formId, field = fieldName, options = field.Options });

            var values = request.Query
                .Where(static pair => !string.Equals(pair.Key, "formId", StringComparison.OrdinalIgnoreCase) && !string.Equals(pair.Key, "field", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);

            var options = await FormOptionsProviders.ResolveAsync(field.OptionsProvider, request, definition, field, values).ConfigureAwait(false);
            return WebResult.Json(new
            {
                formId,
                field = fieldName,
                options = options.Select(static option => new { option.Value, option.Label }).ToArray()
            });
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/forms/search", new WebRouteOptions
        {
            Name = "WebLogic Form Search",
            Description = "Resolves async search results for autocomplete-backed form fields.",
            Tags = ["weblogic", "forms", "search", "autocomplete"]
        }, async request =>
        {
            var formId = request.GetQuery("formId");
            var fieldName = request.GetQuery("field");
            var term = request.GetQuery("term");
            if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(fieldName))
                return WebResult.Text("formId and field are required.", 400);

            var definition = WebFormBinder.GetDefinitionById(formId);
            if (definition is null)
                return WebResult.Text("Form definition not found.", 404);

            var field = definition.Fields.FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (field is null)
                return WebResult.Text("Field definition not found.", 404);

            var values = request.Query
                .Where(static pair =>
                    !string.Equals(pair.Key, "formId", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "field", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "term", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<WebFormSelectOption> options;
            if (!string.IsNullOrWhiteSpace(field.SearchProvider))
            {
                options = await FormOptionsProviders.ResolveSearchAsync(field.SearchProvider, request, definition, field, values, term).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(field.OptionsProvider))
            {
                options = await FormOptionsProviders.ResolveAsync(field.OptionsProvider, request, definition, field, values).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(term))
                {
                    options = options
                        .Where(option =>
                            option.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            option.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }
            }
            else
            {
                options = string.IsNullOrWhiteSpace(term)
                    ? field.Options
                    : field.Options.Where(option =>
                        option.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        option.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            return WebResult.Json(new
            {
                formId,
                field = fieldName,
                term,
                options = options.Select(static option => new { option.Value, option.Label }).ToArray()
            });
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgets", new WebRouteOptions
        {
            Name = "WebLogic Widget Explorer",
            Description = "Lists registered widgets and their metadata.",
            Tags = ["weblogic", "explorer", "widgets"]
        }, _ => Task.FromResult(WebResult.Json(new
        {
            generatedUtc = DateTime.UtcNow,
            widgets = Widgets.GetDescriptors()
                .OrderBy(static widget => widget.SourceKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static widget => widget.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static widget => widget.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        })), "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgets/render", new WebRouteOptions
        {
            Name = "WebLogic Widget Renderer",
            Description = "Renders a widget to HTML so dashboards can load widget instances over AJAX.",
            Tags = ["weblogic", "explorer", "widgets", "render"]
        }, async request =>
        {
            var widgetName = request.GetQuery("name");
            if (string.IsNullOrWhiteSpace(widgetName))
                return WebResult.Text("Missing required widget name.", 400);

            if (!Widgets.TryGet(widgetName, out var widget) || widget is null)
                return WebResult.Text("Widget not found.", 404);

            if (!widget.AllowAnonymous && !request.IsAuthenticated)
                return WebResult.Text("Authentication required.", 401);

            if (widget.RequiredAccessGroups.Length > 0 && !request.HasAnyAccessGroup(widget.RequiredAccessGroups))
                return WebResult.Text("Access denied.", 403);

            if (_themeManager is null || Runtime is null)
                return WebResult.Text("WebLogic runtime is not initialized.", 503);

            var parameters = widget.SampleParameters
                .Concat(request.Query
                    .Where(static pair => !string.Equals(pair.Key, "name", StringComparison.OrdinalIgnoreCase))
                    .Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)))
                .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

            var html = await _themeManager.RenderWidgetAsync(
                widget.Name,
                parameters,
                null,
                Runtime.ThemeRoot,
                request,
                request.GetQuery("instanceId")).ConfigureAwait(false);

            return WebResult.Html(html);
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgets/settings", new WebRouteOptions
        {
            Name = "WebLogic Widget Settings",
            Description = "Lists saved widget instance settings or returns a specific instance record.",
            Tags = ["weblogic", "explorer", "widgets", "settings"]
        }, async request =>
        {
            if (WidgetSettingsStore is null)
                return WebResult.Json(new { enabled = false, settings = Array.Empty<object>() });

            var instanceId = request.GetQuery("instanceId");
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                var record = await WidgetSettingsStore.GetAsync(instanceId).ConfigureAwait(false);
                return WebResult.Json(new
                {
                    enabled = true,
                    instance = record
                });
            }

            var settings = await WidgetSettingsStore.GetAllAsync().ConfigureAwait(false);
            return WebResult.Json(new
            {
                enabled = true,
                settings
            });
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgets/settings/save", new WebRouteOptions
        {
            Name = "WebLogic Widget Settings Save",
            Description = "Saves widget instance settings for a named widget instance.",
            Tags = ["weblogic", "explorer", "widgets", "settings", "save"]
        }, async request =>
        {
            if (WidgetSettingsStore is null)
                return WebResult.Text("Widget settings are disabled.", 503);

            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var instanceId = form.GetValueOrDefault("instanceId") ?? request.GetQuery("instanceId");
            var widgetName = form.GetValueOrDefault("widgetName") ?? request.GetQuery("widgetName");

            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(widgetName))
                return WebResult.Text("instanceId and widgetName are required.", 400);

            var settings = form
                .Where(static pair =>
                    !string.Equals(pair.Key, "instanceId", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "widgetName", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            await WidgetSettingsStore.UpsertAsync(instanceId, widgetName, settings).ConfigureAwait(false);
            var record = await WidgetSettingsStore.GetAsync(instanceId).ConfigureAwait(false);

            return WebResult.Json(new
            {
                saved = true,
                instance = record
            });
        }, "POST");

        explorerContributor.RegisterApi("/api/weblogic/widgets/data", new WebRouteOptions
        {
            Name = "WebLogic Widget Data",
            Description = "Returns structured data for a widget instance when the widget provides a data handler.",
            Tags = ["weblogic", "explorer", "widgets", "data"]
        }, async request =>
        {
            var widgetName = request.GetQuery("name");
            if (string.IsNullOrWhiteSpace(widgetName))
                return WebResult.Text("Missing required widget name.", 400);

            if (!Widgets.TryGet(widgetName, out var widget) || widget is null)
                return WebResult.Text("Widget not found.", 404);

            if (widget.DataHandler is null)
                return WebResult.Text("Widget does not expose data.", 404);

            if (!widget.AllowAnonymous && !request.IsAuthenticated)
                return WebResult.Text("Authentication required.", 401);

            if (widget.RequiredAccessGroups.Length > 0 && !request.HasAnyAccessGroup(widget.RequiredAccessGroups))
                return WebResult.Text("Access denied.", 403);

            var instanceId = request.GetQuery("instanceId");
            var parameters = widget.SampleParameters
                .Concat(request.Query
                    .Where(static pair =>
                        !string.Equals(pair.Key, "name", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(pair.Key, "instanceId", StringComparison.OrdinalIgnoreCase))
                    .Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)))
                .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

            var widgetContext = await BuildWidgetContextAsync(widget, request, parameters, instanceId).ConfigureAwait(false);
            var payload = await widget.DataHandler(widgetContext).ConfigureAwait(false);

            return WebResult.Json(new
            {
                widget = widget.Name,
                instanceId,
                payload
            });
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgets/action", new WebRouteOptions
        {
            Name = "WebLogic Widget Action",
            Description = "Invokes a named widget action for a widget instance.",
            Tags = ["weblogic", "explorer", "widgets", "action"]
        }, async request =>
        {
            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var widgetName = form.GetValueOrDefault("name") ?? request.GetQuery("name");
            var actionName = form.GetValueOrDefault("action") ?? request.GetQuery("action");
            var instanceId = form.GetValueOrDefault("instanceId") ?? request.GetQuery("instanceId");

            if (string.IsNullOrWhiteSpace(widgetName) || string.IsNullOrWhiteSpace(actionName))
                return WebResult.Text("name and action are required.", 400);

            if (!Widgets.TryGet(widgetName, out var widget) || widget is null)
                return WebResult.Text("Widget not found.", 404);

            if (widget.ActionHandler is null || !widget.Actions.ContainsKey(actionName))
                return WebResult.Text("Widget action not found.", 404);

            if (!widget.AllowAnonymous && !request.IsAuthenticated)
                return WebResult.Text("Authentication required.", 401);

            if (widget.RequiredAccessGroups.Length > 0 && !request.HasAnyAccessGroup(widget.RequiredAccessGroups))
                return WebResult.Text("Access denied.", 403);

            var arguments = form
                .Where(static pair =>
                    !string.Equals(pair.Key, "name", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "action", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "instanceId", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var widgetContext = await BuildWidgetContextAsync(widget, request, arguments, instanceId).ConfigureAwait(false);
            var result = await widget.ActionHandler(new WebWidgetActionContext
            {
                ActionName = actionName,
                Widget = widgetContext,
                Arguments = arguments
            }).ConfigureAwait(false);

            await PublishWidgetClientEventsAsync(request, widget.Name, instanceId, result.Events).ConfigureAwait(false);

            return WebResult.Json(new
            {
                action = actionName,
                widget = widget.Name,
                instanceId,
                payload = result.Payload,
                refresh = result.Refresh,
                messages = result.Messages,
                events = result.Events
            }, result.StatusCode);
        }, "POST");

        explorerContributor.RegisterApi("/api/weblogic/widgetareas", new WebRouteOptions
        {
            Name = "WebLogic Widget Areas",
            Description = "Lists widget area registrations and their contributing widgets.",
            Tags = ["weblogic", "explorer", "widgets", "areas"]
        }, _ => Task.FromResult(WebResult.Json(new
        {
            generatedUtc = DateTime.UtcNow,
            areas = Widgets.GetAreaDescriptors()
        })), "GET");

        explorerContributor.RegisterApi("/api/weblogic/widgetareas/render", new WebRouteOptions
        {
            Name = "WebLogic Widget Area Renderer",
            Description = "Renders all widgets registered to an area as a combined HTML fragment.",
            Tags = ["weblogic", "explorer", "widgets", "areas", "render"]
        }, async request =>
        {
            var areaName = request.GetQuery("name");
            if (string.IsNullOrWhiteSpace(areaName))
                return WebResult.Text("Missing required area name.", 400);

            if (_themeManager is null || Runtime is null)
                return WebResult.Text("WebLogic runtime is not initialized.", 503);

            var html = await _themeManager.RenderWidgetAreaAsync(
                areaName,
                null,
                Runtime.ThemeRoot,
                request,
                request.GetQuery("targetPath")).ConfigureAwait(false);

            return WebResult.Html(html);
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/dashboard/layout", new WebRouteOptions
        {
            Name = "WebLogic Dashboard Layout",
            Description = "Returns the signed-in user's saved dashboard layout.",
            Tags = ["weblogic", "dashboard", "layout"]
        }, async request =>
        {
            if (DashboardLayouts is null)
                return WebResult.Text("Dashboard layouts are unavailable.", 503);

            var dashboardKey = request.GetQuery("dashboardKey", "main") ?? "main";
            var ownerKey = request.GetQuery("ownerKey") ?? request.GetQuery("scopeKey") ?? request.UserId;
            if (string.IsNullOrWhiteSpace(ownerKey))
                return WebResult.Text("ownerKey is required.", 400);

            var layout = await GetDashboardLayoutAsync(ownerKey, dashboardKey).ConfigureAwait(false);

            return WebResult.Json(new
            {
                ownerKey,
                dashboardKey,
                widgets = layout?.Widgets ?? [],
                updatedUtc = layout?.UpdatedUtc
            });
        }, "GET");

        explorerContributor.RegisterApi("/api/weblogic/dashboard/layout/save", new WebRouteOptions
        {
            Name = "WebLogic Dashboard Layout Save",
            Description = "Saves the signed-in user's dashboard layout and widget settings.",
            Tags = ["weblogic", "dashboard", "layout", "save"]
        }, async request =>
        {
            if (DashboardLayouts is null)
                return WebResult.Text("Dashboard layouts are unavailable.", 503);

            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var dashboardKey = form.GetValueOrDefault("dashboardKey") ?? request.GetQuery("dashboardKey") ?? "main";
            var ownerKey = form.GetValueOrDefault("ownerKey") ?? form.GetValueOrDefault("scopeKey") ?? request.GetQuery("ownerKey") ?? request.GetQuery("scopeKey") ?? request.UserId;
            var layoutJson = form.GetValueOrDefault("layoutJson") ?? request.GetQuery("layoutJson");
            if (string.IsNullOrWhiteSpace(ownerKey))
                return WebResult.Text("ownerKey is required.", 400);

            var widgets = WebDashboardLayoutSerialization.Deserialize(layoutJson);
            var saved = await SaveDashboardLayoutAsync(ownerKey, dashboardKey, widgets).ConfigureAwait(false);

            var dashboardEvent = WebLogicRealtimeEnvelope.Create(
                WebLogicRealtimeKind.Custom,
                "cl.weblogic.dashboard",
                $"Dashboard layout saved: {dashboardKey}",
                $"Saved {saved.Widgets.Count} widget placements for {ownerKey}.",
                new
                {
                    dashboardKey,
                    ownerKey,
                    widgetCount = saved.Widgets.Count
                },
                request.IsAuthenticated && !string.IsNullOrWhiteSpace(request.UserId)
                    ? WebLogicRealtimeAudience.User
                    : WebLogicRealtimeAudience.Public,
                users: request.IsAuthenticated && !string.IsNullOrWhiteSpace(request.UserId) ? [request.UserId] : null,
                correlationId: request.HttpContext.TraceIdentifier,
                properties: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["widgetChannel"] = "dashboard.layout.updated",
                    ["dashboardKey"] = dashboardKey,
                    ["ownerKey"] = ownerKey
                });

            await (RealtimeBridge?.PublishAsync(dashboardEvent) ?? Task.CompletedTask).ConfigureAwait(false);

            return WebResult.Json(new
            {
                saved = true,
                ownerKey,
                dashboardKey,
                widgets = saved.Widgets,
                updatedUtc = saved.UpdatedUtc,
                refresh = new WebWidgetRefreshPlan
                {
                    RefreshWidgetAreas = true
                },
                events = new[]
                {
                    new WebWidgetClientEvent
                    {
                        Channel = "dashboard.layout.updated",
                        Name = dashboardKey,
                        Payload = new
                        {
                            dashboardKey,
                            ownerKey
                        }
                    }
                },
                messages = new[]
                {
                    new WebWidgetClientMessage
                    {
                        Level = WebWidgetClientMessageLevel.Success,
                        Title = "Dashboard saved",
                        Detail = $"Saved {saved.Widgets.Count} widgets for {ownerKey}."
                    }
                }
            });
        }, "POST");

        explorerContributor.RegisterApi("/api/weblogic/dashboard/render", new WebRouteOptions
        {
            Name = "WebLogic Dashboard Renderer",
            Description = "Renders a signed-in user's saved dashboard zone.",
            Tags = ["weblogic", "dashboard", "render"]
        }, async request =>
        {
            if (DashboardLayouts is null || _themeManager is null || Runtime is null)
                return WebResult.Text("Dashboard rendering is unavailable.", 503);

            var dashboardKey = request.GetQuery("dashboardKey", "main") ?? "main";
            var zone = request.GetQuery("zone", "main") ?? "main";
            var ownerKey = request.GetQuery("ownerKey") ?? request.GetQuery("scopeKey") ?? request.UserId;
            if (string.IsNullOrWhiteSpace(ownerKey))
                return WebResult.Text("ownerKey is required.", 400);

            var html = await RenderDashboardZoneAsync(ownerKey, dashboardKey, zone, request).ConfigureAwait(false);
            return WebResult.Html(html);
        }, "GET");

        explorerContributor.RegisterPage("/weblogic/apiexplorer", new WebRouteOptions
        {
            Name = "WebLogic API Explorer Page",
            Description = "Simple built-in HTML view of registered API routes.",
            Tags = ["weblogic", "explorer", "api"]
        }, _ => Task.FromResult(WebResult.Html(BuildApiExplorerHtml())), "GET");

        explorerContributor.RegisterPage("/weblogic/widgets", new WebRouteOptions
        {
            Name = "WebLogic Widget Explorer Page",
            Description = "Built-in HTML view of registered widgets and AJAX-loaded widget previews.",
            Tags = ["weblogic", "explorer", "widgets"]
        }, _ => Task.FromResult(WebResult.Html(BuildWidgetExplorerHtml())), "GET");

        explorerContributor.RegisterPage("/weblogic/widgetareas", new WebRouteOptions
        {
            Name = "WebLogic Widget Area Explorer Page",
            Description = "Built-in HTML view of widget areas and their composed output.",
            Tags = ["weblogic", "explorer", "widgets", "areas"]
        }, _ => Task.FromResult(WebResult.Html(BuildWidgetAreaExplorerHtml())), "GET");

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
                    <p><a href="/api/weblogic/apiexplorer">JSON API explorer</a> | <a href="/api/weblogic/routes">All routes</a> | <a href="/api/weblogic/plugins">Contributors</a> | <a href="/weblogic/widgets">Widgets</a></p>
                    {{items}}
                </main>
            </body>
            </html>
            """.Replace("{{items}}", items, StringComparison.Ordinal);
    }

    private string BuildWidgetExplorerHtml()
    {
        var widgets = Widgets.GetDescriptors()
            .OrderBy(static widget => widget.SourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static widget => widget.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static widget => widget.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = string.Join(Environment.NewLine, widgets.Select(widget =>
            $"""
            <article class="api-card">
                <h2>{System.Net.WebUtility.HtmlEncode(widget.Name)}</h2>
                <p>{System.Net.WebUtility.HtmlEncode(widget.Description)}</p>
                <p><strong>Source:</strong> {System.Net.WebUtility.HtmlEncode(widget.SourceName)} ({System.Net.WebUtility.HtmlEncode(widget.SourceKind)})</p>
                <p><strong>Access:</strong> {(widget.AllowAnonymous ? "Anonymous" : System.Net.WebUtility.HtmlEncode(string.Join(", ", widget.RequiredAccessGroups)))}</p>
                <p><strong>Tags:</strong> {System.Net.WebUtility.HtmlEncode(string.Join(", ", widget.Tags))}</p>
                <div data-widget-preview="{System.Net.WebUtility.HtmlEncode(widget.Name)}" class="mt-3 p-3" style="background:#f4f7f9;border-radius:14px;">Loading preview...</div>
            </article>
            """));

        return
            $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CL.WebLogic Widget Explorer</title>
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
                    <h1>Widget Explorer</h1>
                    <p>This built-in page lists every registered widget and loads a preview through the widget-render API.</p>
                    <p><a href="/api/weblogic/widgets">JSON widget explorer</a> | <a href="/api/weblogic/widgets/render?name=starter.request-glance">Render API</a> | <a href="/api/weblogic/widgets/settings">Settings API</a> | <a href="/weblogic/apiexplorer">API Explorer</a></p>
                    {{items}}
                </main>
                <script src="/assets/vendor/jquery/jquery-4.0.0.min.js"></script>
                <script>
                    $(function () {
                        $("[data-widget-preview]").each(function () {
                            const $preview = $(this);
                            const name = $preview.data("widget-preview");
                            $.get("/api/weblogic/widgets/render", { name })
                                .done(function (html) { $preview.html(html); })
                                .fail(function () { $preview.text("Preview failed to load."); });
                        });
                    });
                </script>
            </body>
            </html>
            """.Replace("{{items}}", items, StringComparison.Ordinal);
    }

    private string BuildWidgetAreaExplorerHtml()
    {
        var areas = Widgets.GetAreaDescriptors()
            .GroupBy(static area => area.AreaName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = string.Join(Environment.NewLine, areas.Select(group =>
        {
            var widgets = string.Join(Environment.NewLine, group.Select(area =>
                $"""
                <li>
                    <strong>{System.Net.WebUtility.HtmlEncode(area.WidgetName)}</strong>
                    from {System.Net.WebUtility.HtmlEncode(area.SourceName)} ({System.Net.WebUtility.HtmlEncode(area.SourceKind)})
                </li>
                """));

            var policyText = string.Join(" | ", group.Select(area =>
            {
                var include = area.IncludeRoutePatterns.Length == 0 ? "*" : string.Join(", ", area.IncludeRoutePatterns);
                var exclude = area.ExcludeRoutePatterns.Length == 0 ? "(none)" : string.Join(", ", area.ExcludeRoutePatterns);
                var access = area.AllowAnonymous
                    ? "anonymous"
                    : string.Join(", ", area.RequiredAccessGroups.Length == 0 ? ["authenticated"] : area.RequiredAccessGroups);

                return $"routes={include}; exclude={exclude}; access={access}";
            }));

            return
                $"""
                <article class="api-card">
                    <h2>{System.Net.WebUtility.HtmlEncode(group.Key)}</h2>
                    <ul>{widgets}</ul>
                    <p><strong>Instances:</strong> {System.Net.WebUtility.HtmlEncode(string.Join(", ", group.Select(area => area.InstanceId ?? "(none)")))}</p>
                    <p><strong>Policies:</strong> {System.Net.WebUtility.HtmlEncode(policyText)}</p>
                    <div data-widget-area-preview="{System.Net.WebUtility.HtmlEncode(group.Key)}" class="mt-3 p-3" style="background:#f4f7f9;border-radius:14px;">Loading area preview...</div>
                </article>
                """;
        }));

        return
            $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>CL.WebLogic Widget Areas</title>
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
                    <h1>Widget Areas</h1>
                    <p>This built-in page lists widget areas and renders the combined area output through the area-render API.</p>
                    <p><a href="/api/weblogic/widgetareas">JSON area explorer</a> | <a href="/api/weblogic/widgetareas/render?name=dashboard.main">Render area API</a> | <a href="/weblogic/widgets">Widgets</a></p>
                    {{items}}
                </main>
                <script src="/assets/vendor/jquery/jquery-4.0.0.min.js"></script>
                <script>
                    $(function () {
                        $("[data-widget-area-preview]").each(function () {
                            const $preview = $(this);
                            const name = $preview.data("widget-area-preview");
                            $.get("/api/weblogic/widgetareas/render", { name, targetPath: window.location.pathname })
                                .done(function (html) { $preview.html(html || "<em>No accessible widgets in this area.</em>"); })
                                .fail(function () { $preview.text("Area preview failed to load."); });
                        });
                    });
                </script>
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
            WebLogicRealtimeAudience.User => request.IsAuthenticated && evt.Users.Contains(request.UserId, StringComparer.OrdinalIgnoreCase),
            WebLogicRealtimeAudience.Internal => request.IsAuthenticated,
            _ => false
        };
    }

    private async Task PublishWidgetClientEventsAsync(
        WebRequestContext request,
        string widgetName,
        string? instanceId,
        IReadOnlyList<WebWidgetClientEvent> events)
    {
        if (RealtimeBridge is null || events.Count == 0)
            return;

        foreach (var evt in events.Where(static item => !string.IsNullOrWhiteSpace(item.Channel)))
        {
            var audience = request.IsAuthenticated && !string.IsNullOrWhiteSpace(request.UserId)
                ? WebLogicRealtimeAudience.User
                : WebLogicRealtimeAudience.Public;

            var envelope = WebLogicRealtimeEnvelope.Create(
                WebLogicRealtimeKind.Custom,
                "cl.weblogic.widgets",
                $"Widget channel: {evt.Channel}",
                evt.Name ?? $"Widget event from {widgetName}",
                evt.Payload,
                audience,
                users: audience == WebLogicRealtimeAudience.User ? [request.UserId] : null,
                correlationId: request.HttpContext.TraceIdentifier,
                properties: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["widgetChannel"] = evt.Channel,
                    ["widgetName"] = widgetName,
                    ["instanceId"] = instanceId,
                    ["eventName"] = evt.Name
                });

            await RealtimeBridge.PublishAsync(envelope).ConfigureAwait(false);
        }
    }

    private async Task<string> RenderDashboardWidgetsAsync(
        WebRequestContext request,
        IReadOnlyList<WebDashboardWidgetPlacement> widgets)
    {
        if (_themeManager is null || Runtime is null || widgets.Count == 0)
            return string.Empty;

        var fragments = new List<string>();
        foreach (var widget in widgets)
        {
            var html = await _themeManager.RenderWidgetAsync(
                widget.WidgetName,
                widget.Settings,
                null,
                Runtime.ThemeRoot,
                request,
                widget.InstanceId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(html))
                fragments.Add(html);
        }

        return string.Join(Environment.NewLine, fragments);
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

    private static byte[] LoadEmbeddedClientRuntime()
    {
        var assembly = typeof(WebLogicLibrary).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(static name => name.EndsWith("Client.weblogic.client.js", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException("The embedded CL.WebLogic client runtime could not be found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded CL.WebLogic client runtime stream could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
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
