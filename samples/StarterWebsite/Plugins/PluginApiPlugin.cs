using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CodeLogic.Core.Configuration;
using CodeLogic.Framework.Application.Plugins;
using CodeLogic.Framework.Libraries;

namespace StarterWebsite.Plugins;

public sealed class PluginApiPlugin : IPlugin, IWebRouteContributor
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "starter.plugin-api",
        Name = "Plugin API Plugin",
        Version = "1.0.0",
        Description = "Adds API endpoints from a plugin to show modular website features",
        Author = "Media2A"
    };

    public PluginState State { get; private set; } = PluginState.Loaded;
    private PluginApiConfig _config = new();

    public Task OnConfigureAsync(PluginContext context)
    {
        context.Configuration.Register<PluginApiConfig>();
        State = PluginState.Configured;
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(PluginContext context)
    {
        _config = context.Configuration.Get<PluginApiConfig>();
        State = PluginState.Initialized;
        return Task.CompletedTask;
    }

    public async Task OnStartAsync(PluginContext context)
    {
        var web = WebLogicLibrary.GetRequired();

        await web.RegisterContributorAsync(new WebContributorDescriptor
        {
            Id = Manifest.Id,
            Name = Manifest.Name,
            Kind = "Plugin",
            Description = Manifest.Description ?? string.Empty
        }, this).ConfigureAwait(false);

        State = PluginState.Started;
    }

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterApi("/api/plugins/manifest", new WebRouteOptions
        {
            Name = "Plugin Manifest API",
            Description = "Shows plugin-owned routes plus sample RBAC metadata.",
            Tags = ["starter", "plugin", "api"]
        }, request => Task.FromResult(WebResult.Json(new
        {
            feature = _config.FeatureName,
            enabled = true,
            routes = new[]
            {
                "/plugins/theme-showcase",
                "/api/plugins/manifest",
                "/api/plugins/message",
                "/api/plugins/secure"
            },
            pageContext = new
            {
                request.Path,
                request.Method,
                request.UserId,
                accessGroups = request.AccessGroups
            }
        })), "GET");

        context.RegisterApi("/api/plugins/message", new WebRouteOptions
        {
            Name = "Plugin Message API",
            Description = "Reads form or query values from the shared page context.",
            Tags = ["starter", "plugin", "api", "form"]
        }, async request =>
        {
            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var name = form.GetValueOrDefault("name")
                ?? request.GetQuery("name")
                ?? "friend";

            return WebResult.Json(new
            {
                feature = _config.FeatureName,
                message = $"Hello, {name}. This JSON response came from a plugin-registered API.",
                pageContext = new
                {
                    request.Path,
                    request.Method,
                    queryName = request.GetQuery("name"),
                    formKeys = form.Keys.OrderBy(static key => key).ToArray()
                }
            });
        }, "GET", "POST");

        context.RegisterApi("/api/plugins/secure", new WebRouteOptions
        {
            Name = "Plugin Secure API",
            Description = "Sample RBAC-protected endpoint requiring the admin access group.",
            Tags = ["starter", "plugin", "api", "rbac"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["admin"]
        }, request => Task.FromResult(WebResult.Json(new
        {
            feature = _config.FeatureName,
            message = "You can see this because your request context has the admin access group.",
            request.UserId,
            accessGroups = request.AccessGroups
        })), "GET");

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        State = PluginState.Stopped;
        return Task.CompletedTask;
    }

    public Task<HealthStatus> HealthCheckAsync() =>
        Task.FromResult(HealthStatus.Healthy("Plugin API endpoints are registered"));

    public void Dispose()
    {
    }
}

public sealed class PluginApiConfig : ConfigModelBase
{
    public string FeatureName { get; set; } = "Starter Plugin API";
}
