using CL.WebLogic;
using CL.WebLogic.MySql;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CodeLogic;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application;
using StarterWebsite.Config;
using System.Text;

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

        if (web.IdentityStore is WebMySqlIdentityStore mySqlIdentityStore)
        {
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
                ["theme_name"] = _config.ThemeName,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
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

        context.RegisterPage("/login", new WebRouteOptions
        {
            Name = "Login",
            Description = "Starter login page for demo users and RBAC walkthroughs.",
            Tags = ["starter", "page", "auth", "login"]
        }, async request =>
        {
            if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var form = await request.ReadFormAsync().ConfigureAwait(false);
                var userId = form.GetValueOrDefault("userId")?.Trim() ?? string.Empty;
                var password = form.GetValueOrDefault("password") ?? string.Empty;
                var returnUrl = SanitizeReturnUrl(form.GetValueOrDefault("returnUrl"));
                var identity = await ValidateLoginAsync(userId, password).ConfigureAwait(false);

                if (identity is not null)
                {
                    request.SetSessionValue("weblogic.user_id", identity.UserId);
                    request.SetSessionValue("weblogic.access_groups", string.Join(",", identity.AccessGroups));
                    request.SetSessionValue("starter.display_name", identity.DisplayName);
                    return Redirect(request, returnUrl ?? "/rbac");
                }

                return WebResult.Template("templates/login.html", BuildLoginModel(
                    returnUrl ?? "/rbac",
                    "Login failed. Try one of the seeded demo users shown below.",
                    true));
            }

            if (request.IsAuthenticated)
                return Redirect(request, "/rbac");

            var requestedReturnUrl = SanitizeReturnUrl(request.GetQuery("returnUrl")) ?? "/rbac";
            var signedOut = string.Equals(request.GetQuery("signedOut"), "1", StringComparison.OrdinalIgnoreCase);
            return WebResult.Template("templates/login.html", BuildLoginModel(
                requestedReturnUrl,
                signedOut ? "You have been signed out." : string.Empty,
                false));
        }, "GET", "POST");

        context.RegisterPage("/logout", new WebRouteOptions
        {
            Name = "Logout",
            Description = "Clears the starter auth session and redirects back to login.",
            Tags = ["starter", "page", "auth", "logout"]
        }, request =>
        {
            request.SetSessionValue("weblogic.user_id", null);
            request.SetSessionValue("weblogic.access_groups", null);
            request.SetSessionValue("starter.display_name", null);
            return Task.FromResult(Redirect(request, "/login?signedOut=1"));
        }, "GET", "POST");

        context.RegisterPage("/rbac", new WebRouteOptions
        {
            Name = "RBAC Hub",
            Description = "Authenticated starter page showing the current session identity and available role-based pages.",
            Tags = ["starter", "page", "rbac"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["member", "editor", "staff", "admin", "beta"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/rbac-home.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["display_name"] = request.GetSessionValue("starter.display_name", request.UserId) ?? request.UserId,
                ["user_id"] = request.UserId,
                ["access_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups),
                ["auth_message"] = BuildAccessSummary(request)
            })), "GET");

        context.RegisterPage("/rbac/admin", new WebRouteOptions
        {
            Name = "Admin Test Page",
            Description = "RBAC page that requires the admin group.",
            Tags = ["starter", "page", "rbac", "admin"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["admin"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/rbac-admin.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["display_name"] = request.GetSessionValue("starter.display_name", request.UserId) ?? request.UserId,
                ["user_id"] = request.UserId,
                ["access_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
            })), "GET");

        context.RegisterPage("/rbac/editor", new WebRouteOptions
        {
            Name = "Editor Test Page",
            Description = "RBAC page that accepts editor or admin access.",
            Tags = ["starter", "page", "rbac", "editor"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/rbac-editor.html",
            new Dictionary<string, object?>
            {
                ["title"] = _config.SiteTitle,
                ["display_name"] = request.GetSessionValue("starter.display_name", request.UserId) ?? request.UserId,
                ["user_id"] = request.UserId,
                ["access_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
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
                demoUsers = _config.DemoUsers.Select(static user => new
                {
                    user.UserId,
                    user.DisplayName,
                    accessGroups = user.AccessGroups
                }),
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

    private async Task<WebIdentityProfile?> ValidateLoginAsync(string userId, string password)
    {
        var web = WebLogicLibrary.GetRequired();
        if (web.IdentityStore is IWebCredentialValidator credentialValidator)
            return await credentialValidator.ValidateCredentialsAsync(userId, password).ConfigureAwait(false);

        return null;
    }

    private Dictionary<string, object?> BuildLoginModel(string returnUrl, string message, bool isError)
    {
        var accountCards = new StringBuilder();
        foreach (var user in _config.DemoUsers)
        {
            accountCards.AppendLine($"""
                <div class="demo-user-card">
                    <p class="demo-user-title">{System.Net.WebUtility.HtmlEncode(user.DisplayName)}</p>
                    <p class="demo-user-meta mb-1">User ID: <code>{System.Net.WebUtility.HtmlEncode(user.UserId)}</code></p>
                    <p class="demo-user-meta mb-1">Password: <code>{System.Net.WebUtility.HtmlEncode(user.Password)}</code></p>
                    <p class="demo-user-meta mb-0">Groups: <code>{System.Net.WebUtility.HtmlEncode(string.Join(", ", user.AccessGroups))}</code></p>
                </div>
                """);
        }

        return new Dictionary<string, object?>
        {
            ["title"] = _config.SiteTitle,
            ["return_url"] = returnUrl,
            ["message"] = message,
            ["message_class"] = isError ? "alert-danger" : "alert-success",
            ["message_style"] = string.IsNullOrWhiteSpace(message) ? "display:none;" : string.Empty,
            ["demo_accounts"] = accountCards.ToString()
        };
    }

    private static string BuildAccessSummary(WebRequestContext request)
    {
        if (request.HasAccessGroup("admin"))
            return "You can access every RBAC demo page, including the admin page.";

        if (request.HasAnyAccessGroup(["editor"]))
            return "You can access the editor page, but the admin page should return 403.";

        return "You are authenticated, but you should be blocked from the editor and admin pages.";
    }

    private static string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        return returnUrl.StartsWith("/", StringComparison.Ordinal) ? returnUrl : null;
    }

    private static WebResult Redirect(WebRequestContext request, string location)
    {
        request.HttpContext.Response.Headers.Location = location;
        return new WebResult
        {
            StatusCode = 302,
            ContentType = "text/html; charset=utf-8",
            TextBody = $"""<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0;url={System.Net.WebUtility.HtmlEncode(location)}"></head><body>Redirecting to <a href="{System.Net.WebUtility.HtmlEncode(location)}">{System.Net.WebUtility.HtmlEncode(location)}</a>...</body></html>"""
        };
    }

    private static string BuildEmail(string userId) =>
        $"{userId}@starter.local";
}
