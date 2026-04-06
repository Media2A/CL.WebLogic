using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
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
        context.RegisterPage("/plugins/theme-showcase", new WebRouteOptions
        {
            Name = "Theme Showcase",
            Description = "Plugin-owned page that renders inside the shared theme and demonstrates auth-aware template blocks.",
            Tags = ["starter", "plugin", "page"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/plugin-showcase.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.PageTitle,
                ["description"] = _config.Description,
                ["badge"] = "Plugin Route",
                ["links"] = "/api/plugins/manifest",
                ["user_id"] = request.UserId == string.Empty ? "anonymous" : request.UserId,
                ["access_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
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
