using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CodeLogic;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application;
using StarterWebsite.Config;

namespace StarterWebsite.Application;

public sealed class StarterWebsiteApplication : IApplication, IWebRouteContributor
{
    public ApplicationManifest Manifest { get; } = new()
    {
        Id = "starter.website",
        Name = "CL.WebLogic Starter Website",
        Version = "1.0.0",
        Description = "Starter website showing app routes, plugin routes, page context, and theme rendering",
        Author = "Media2A"
    };

    private StarterWebsiteConfig _config = new();

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

        context.Events.Subscribe<WebRequestHandledEvent>(e =>
            context.Logger.Trace($"{e.Method} {e.Path} => {e.StatusCode} in {e.DurationMs}ms"));

        await web.RegisterContributorAsync(new WebContributorDescriptor
        {
            Id = Manifest.Id,
            Name = Manifest.Name,
            Kind = "Application",
            Description = Manifest.Description ?? string.Empty
        }, this).ConfigureAwait(false);
    }

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterPage("/", new WebRouteOptions
        {
            Name = "Starter Home",
            Description = "Application-owned homepage rendered through the shared CL.WebLogic theme.",
            Tags = ["starter", "page", "home"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/home.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["tagline"] = _config.Tagline,
                ["description"] = "This route is registered by the application. The HTML comes from the starter theme, while the web runtime is provided by CL.WebLogic.",
                ["theme_name"] = _config.ThemeName
            })), "GET");

        context.RegisterPage("/about", new WebRouteOptions
        {
            Name = "About",
            Description = "Shows page-context values gathered from the request.",
            Tags = ["starter", "page", "context"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/about.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["theme_name"] = _config.ThemeName,
                ["plugin_count"] = "2",
                ["page_path"] = request.Path,
                ["request_method"] = request.Method,
                ["current_user"] = request.UserId == string.Empty ? "anonymous" : request.UserId,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
            })), "GET");

        context.RegisterApi("/api/site", new WebRouteOptions
        {
            Name = "Site Summary",
            Description = "Application API showing current site metadata and request context values.",
            Tags = ["starter", "api", "site"]
        }, _ =>
        {
            var pageContext = WebLogicRequest.GetPageContextFromRequest();
            return Task.FromResult(WebResult.Json(new
            {
                site = _config.SiteTitle,
                tagline = _config.Tagline,
                theme = _config.ThemeName,
                plugins = new[] { "ThemeShowcasePlugin", "PluginApiPlugin" },
                pageContext = new
                {
                    pageContext.Path,
                    pageContext.Method,
                    pageContext.UserId,
                    accessGroups = pageContext.AccessGroups
                }
            }));
        }, "GET");

        context.RegisterFallback(new WebRouteOptions
        {
            Name = "Starter Not Found",
            Description = "Themed fallback page for unknown routes.",
            Tags = ["starter", "fallback"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/not-found.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["path"] = request.Path
            },
            404)), "GET", "POST");

        return Task.CompletedTask;
    }

    public Task OnStopAsync() => Task.CompletedTask;
}
