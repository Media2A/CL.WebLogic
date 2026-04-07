using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Theming;
using CodeLogic.Core.Configuration;
using CodeLogic.Framework.Application.Plugins;
using CodeLogic.Framework.Libraries;

namespace StarterWebsite.Plugins;

public sealed class ThemeShowcasePlugin : IPlugin, IWebRouteContributor
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "starter.theme-showcase",
        Name = "Theme Showcase Plugin",
        Version = "1.0.0",
        Description = "Adds a themed page registered from a plugin",
        Author = "Media2A"
    };

    public PluginState State { get; private set; } = PluginState.Loaded;
    private ThemeShowcaseConfig _config = new();

    public Task OnConfigureAsync(PluginContext context)
    {
        context.Configuration.Register<ThemeShowcaseConfig>();
        State = PluginState.Configured;
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(PluginContext context)
    {
        _config = context.Configuration.Get<ThemeShowcaseConfig>();
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
        context.RegisterWidget("plugin.feature-callout", new WebWidgetOptions
        {
            Description = "Plugin-owned widget rendered inside the shared starter theme.",
            Tags = ["starter", "plugin", "widget"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Plugin-owned widget",
                ["description"] = "This widget was registered by the plugin and resolved through the same theme engine as the application widgets."
            }
        }, widgetContext => Task.FromResult(WebWidgetResult.Template(
            "widgets/plugin-callout.html",
            new Dictionary<string, object?>
            {
                ["title"] = widgetContext.GetParameter("title", "Plugin widget") ?? "Plugin widget",
                ["description"] = widgetContext.GetParameter("description", _config.Description) ?? _config.Description,
                ["user"] = string.IsNullOrWhiteSpace(widgetContext.Request.UserId) ? "anonymous" : widgetContext.Request.UserId,
                ["groups"] = widgetContext.Request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", widgetContext.Request.AccessGroups)
            })));

        context.RegisterWidgetArea("dashboard.main", "plugin.feature-callout", new WebWidgetAreaOptions
        {
            Description = "Plugin widget added to the main dashboard area.",
            Order = 30,
            InstanceId = "dashboard.main.plugin-callout",
            IncludeRoutePatterns = ["/dashboard", "/plugins/theme-showcase"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Plugin dashboard card",
                ["description"] = "This card was injected into dashboard.main by the plugin."
            }
        });

        context.RegisterWidgetArea("site.sidebar", "plugin.feature-callout", new WebWidgetAreaOptions
        {
            Description = "Plugin sidebar contribution for plugin and dashboard pages.",
            Order = 30,
            InstanceId = "site.sidebar.plugin-callout",
            IncludeRoutePatterns = ["/plugins/*", "/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Plugin sidebar card",
                ["description"] = "This sidebar card comes from a plugin area registration."
            }
        });

        context.RegisterPage("/plugins/theme-showcase", new WebRouteOptions
        {
            Name = "Theme Showcase",
            Description = "Plugin-owned page that renders inside the shared theme and demonstrates auth-aware template blocks.",
            Tags = ["starter", "plugin", "page"]
        }, request => Task.FromResult(WebResult.Document(new WebPageDocument
        {
            TemplatePath = "templates/plugin-showcase.html",
            Model = new Dictionary<string, object?>
            {
                ["page_title"] = _config.PageTitle,
                ["page_eyebrow"] = "Plugin route",
                ["hero_title"] = _config.PageTitle,
                ["hero_copy"] = _config.Description,
                ["badge"] = "Plugin Route",
                ["links"] = "/api/plugins/manifest",
                ["user_id"] = request.UserId == string.Empty ? "anonymous" : request.UserId,
                ["access_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
            },
            Meta = new WebPageMeta
            {
                Title = $"{_config.PageTitle} | Starter Website",
                Description = _config.Description,
                CanonicalUrl = $"http://127.0.0.1:53248{request.Path}",
                Language = "en",
                Keywords = ["weblogic", "plugin page", "starter website"],
                OpenGraph = new WebOpenGraphMeta
                {
                    Title = _config.PageTitle,
                    Description = _config.Description,
                    Type = "website",
                    Url = $"http://127.0.0.1:53248{request.Path}",
                    SiteName = "Starter Website"
                }
            }
        })), "GET");

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        State = PluginState.Stopped;
        return Task.CompletedTask;
    }

    public Task<HealthStatus> HealthCheckAsync() =>
        Task.FromResult(HealthStatus.Healthy("Theme showcase route is registered"));

    public void Dispose()
    {
    }
}

public sealed class ThemeShowcaseConfig : ConfigModelBase
{
    public string PageTitle { get; set; } = "Theme Showcase";
    public string Description { get; set; } = "This page is registered by a plugin but rendered through the shared website theme.";
}
