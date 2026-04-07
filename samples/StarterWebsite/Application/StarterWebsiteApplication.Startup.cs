using CL.WebLogic;
using CL.WebLogic.Forms;
using CL.WebLogic.MySql;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application;
using StarterWebsite.Application.Forms;
using StarterWebsite.Config;
using System.Text.Json;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
    public Task OnConfigureAsync(ApplicationContext context)
    {
        context.Configuration.Register<StarterWebsiteConfig>();
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(ApplicationContext context)
    {
        _config = context.Configuration.Get<StarterWebsiteConfig>();
        context.Logger.Info($"Starter website initialized for '{_config.SiteTitle}'");
        return Task.CompletedTask;
    }

    public async Task OnStartAsync(ApplicationContext context)
    {
        var web = WebLogicLibrary.GetRequired();
        RegisterFormProviders(web);
        _ = WebFormBinder.GetDefinition<ProfileIntakeForm>();

        context.Events.Subscribe<WebRequestHandledEvent>(e =>
            context.Logger.Trace($"{e.Method} {e.Path} => {e.StatusCode} in {e.DurationMs}ms"));

        await web.RegisterContributorAsync(new WebContributorDescriptor
        {
            Id = Manifest.Id,
            Name = Manifest.Name,
            Kind = "Application",
            Description = Manifest.Description ?? string.Empty
        }, this).ConfigureAwait(false);

        await SeedIdentityAsync(web).ConfigureAwait(false);
        await SeedWidgetSettingsAsync(web).ConfigureAwait(false);
        await SeedDashboardLayoutsAsync(web, context).ConfigureAwait(false);
    }

    public Task OnStopAsync() => Task.CompletedTask;

    private static void RegisterFormProviders(WebLogicLibrary web)
    {
        web.FormOptionsProviders.Register("starter.countries", new StarterCountryOptionsProvider());
        web.FormOptionsProviders.Register("starter.offices", new StarterOfficeOptionsProvider());
        web.FormOptionsProviders.RegisterHttpLookup("starter.mentors", new WebFormHttpLookupOptions
        {
            UrlTemplate = "/api/demo/lookups/mentors?country={Country}&term={term}",
            RootProperty = "items",
            ValueProperty = "id",
            LabelProperty = "label"
        });
    }

    private async Task SeedIdentityAsync(WebLogicLibrary web)
    {
        if (web.IdentityStore is not WebMySqlIdentityStore mySqlIdentityStore)
            return;

        await mySqlIdentityStore.SeedUsersAsync(_config.DemoUsers.Select(user => new WebIdentitySeed
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName,
            Email = BuildEmail(user.UserId),
            Password = user.Password,
            AccessGroups = user.AccessGroups,
            IsActive = true
        })).ConfigureAwait(false);
    }

    private static async Task SeedWidgetSettingsAsync(WebLogicLibrary web)
    {
        if (web.WidgetSettingsStore is null)
            return;

        await web.WidgetSettingsStore.UpsertAsync(
            "dashboard.main.quicklinks",
            "starter.quick-links",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved dashboard navigation"
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "dashboard.sidebar.request",
            "starter.request-glance",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved dashboard request"
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "site.sidebar.request",
            "starter.request-glance",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved page sidebar"
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "member.sidebar.quicklinks",
            "starter.quick-links",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved member navigation"
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "dashboard.main.plugin-callout",
            "plugin.feature-callout",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved plugin dashboard card",
                ["description"] = "This plugin widget is using persisted instance settings from the WebLogic widget store."
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "site.sidebar.plugin-callout",
            "plugin.feature-callout",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved plugin sidebar",
                ["description"] = "This sidebar widget is coming from plugin area composition with persisted settings."
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "dashboard.main.counter",
            "starter.counter-panel",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Saved action counter",
                ["count"] = "3"
            }).ConfigureAwait(false);

        await web.WidgetSettingsStore.UpsertAsync(
            "dashboard.sidebar.activity",
            "starter.activity-stream",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Dashboard activity",
                ["entries_json"] = JsonSerializer.Serialize(new[]
                {
                    new DashboardActivityEntry("weblogic.dashboard.ready", "Dashboard ready", "The starter dashboard is ready to receive widget messages.", DateTimeOffset.UtcNow.AddMinutes(-5).ToString("u")),
                    new DashboardActivityEntry("weblogic.widgets.seeded", "Widget settings seeded", "Persisted widget instance settings were loaded into the demo store.", DateTimeOffset.UtcNow.AddMinutes(-2).ToString("u"))
                })
            }).ConfigureAwait(false);
    }

    private async Task SeedDashboardLayoutsAsync(WebLogicLibrary web, ApplicationContext context)
    {
        if (web.DashboardLayouts is null || web.WidgetSettingsStore is null)
            return;

        try
        {
            foreach (var user in _config.DemoUsers)
            {
                var layout = await web.GetDashboardLayoutAsync(user.UserId, "starter-main").ConfigureAwait(false);
                if (layout is null)
                {
                    var seeded = BuildDefaultUserDashboard(user.UserId);
                    await web.SaveDashboardLayoutAsync(user.UserId, "starter-main", seeded).ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            context.Logger.Warning($"Dashboard layout seeding was skipped: {ex.Message}");
        }
    }
}
