using CL.WebLogic;
using CL.WebLogic.Forms;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CL.WebLogic.Theming;
using StarterWebsite.Application.Forms;
using System.Text;
using System.Text.Json;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
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
            ["page_title"] = "Login",
            ["page_eyebrow"] = "Starter auth flow",
            ["hero_title"] = "Sign in to the RBAC demo",
            ["hero_copy"] = "This starter keeps auth simple on purpose: configured demo users, session-backed identity, and role-based access checks that are easy to inspect.",
            ["return_url"] = returnUrl,
            ["message"] = message,
            ["message_class"] = isError ? "alert-danger" : "alert-success",
            ["message_style"] = string.IsNullOrWhiteSpace(message) ? "display:none;" : string.Empty,
            ["demo_accounts"] = accountCards.ToString()
        };
    }

    private static object[] BuildQuickLinks() =>
    [
        new { Label = "About", Url = "/about", Description = "Request context and theme notes" },
        new { Label = "Template lab", Url = "/template-lab", Description = "Layouts, widgets, loops, and page scripts" },
        new { Label = "Form lab", Url = "/form-lab", Description = "C# forms, client validation, uploads, and lookups" },
        new { Label = "Dashboard", Url = "/dashboard", Description = "AJAX-loaded widget dashboard demo" },
        new { Label = "Dashboard studio", Url = "/dashboard/studio", Description = "Per-user saved dashboard builder" },
        new { Label = "Login", Url = "/login", Description = "MySQL-backed starter auth flow" },
        new { Label = "RBAC hub", Url = "/rbac", Description = "Access-group demo pages" },
        new { Label = "Plugin showcase", Url = "/plugins/theme-showcase", Description = "Plugin route rendered in the same shell" },
        new { Label = "API explorer", Url = "/weblogic/apiexplorer", Description = "Built-in WebLogic discovery page" }
    ];

    private static object[] BuildSnapshotCards(WebRequestContext request) =>
    [
        new { Label = "Current path", Value = request.Path, Note = "Comes directly from {page:path}" },
        new { Label = "Method", Value = request.Method, Note = "Also available from PageContext in C#" },
        new { Label = "User", Value = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId, Note = "Resolved through session-backed auth" },
        new { Label = "Groups", Value = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups), Note = "Used by route and template RBAC checks" }
    ];

    private static object[] BuildTemplateFeatureCards() =>
    [
        new { Title = "Layouts", Description = "A page can define sections while the shared shell owns navigation, scripts, and footer." },
        new { Title = "Widgets", Description = "Widgets can be registered by the app or plugins and render template-backed HTML with full request context." },
        new { Title = "Loops", Description = "The home page iterates cards and link grids directly in the template instead of building large HTML strings in C#." },
        new { Title = "Conditionals", Description = "Auth-aware and access-group-aware blocks live in the template without dragging in Razor." }
    ];

    private static object[] BuildAboutCards() =>
    [
        new { Title = "Page context", Description = "Current path, method, user, and groups are available both in C# and directly in the template." },
        new { Title = "Plugins", Description = "Plugin pages and plugin widgets can blend into the same shell while staying modular." },
        new { Title = "Explorer", Description = "WebLogic can already describe its own route and API surface through built-in endpoints." }
    ];

    private static object[] BuildDashboardFilters(WebRequestContext request) =>
    [
        new { Label = "All widgets", Value = "all", Active = true },
        new { Label = "Application", Value = "Application", Active = false },
        new { Label = "Plugin", Value = "Plugin", Active = false },
        new { Label = "Anonymous-ready", Value = "accessible", Active = false }
    ];

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

    private static WebPageMeta CreateMeta(
        WebRequestContext request,
        string title,
        string description,
        IReadOnlyList<string>? keywords = null)
    {
        var canonicalUrl = $"http://127.0.0.1:53248{request.Path}";
        return new WebPageMeta
        {
            Title = title,
            Description = description,
            CanonicalUrl = canonicalUrl,
            Language = "en",
            Keywords = keywords ?? [],
            OpenGraph = new WebOpenGraphMeta
            {
                Title = title,
                Description = description,
                Type = "website",
                Url = canonicalUrl,
                SiteName = "Starter Website"
            },
            Twitter = new WebTwitterMeta
            {
                Card = "summary",
                Title = title,
                Description = description
            }
        };
    }

    private static List<WebDashboardWidgetPlacement> BuildDefaultUserDashboard(string userId)
    {
        var prefix = $"{userId}.starter-main";
        return
        [
            new WebDashboardWidgetPlacement
            {
                InstanceId = $"{prefix}.main.quicklinks",
                WidgetName = "starter.quick-links",
                Zone = "main",
                Order = 10,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = $"Navigation for {userId}"
                }
            },
            new WebDashboardWidgetPlacement
            {
                InstanceId = $"{prefix}.main.counter",
                WidgetName = "starter.counter-panel",
                Zone = "main",
                Order = 20,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = $"Counter for {userId}",
                    ["count"] = "2"
                }
            },
            new WebDashboardWidgetPlacement
            {
                InstanceId = $"{prefix}.main.plugin",
                WidgetName = "plugin.feature-callout",
                Zone = "main",
                Order = 30,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = $"Plugin note for {userId}",
                    ["description"] = "This plugin widget is part of the saved personal dashboard layout."
                }
            },
            new WebDashboardWidgetPlacement
            {
                InstanceId = $"{prefix}.sidebar.request",
                WidgetName = "starter.request-glance",
                Zone = "sidebar",
                Order = 10,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = $"Request view for {userId}"
                }
            },
            new WebDashboardWidgetPlacement
            {
                InstanceId = $"{prefix}.sidebar.activity",
                WidgetName = "starter.activity-stream",
                Zone = "sidebar",
                Order = 20,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = $"Activity for {userId}",
                    ["entries_json"] = JsonSerializer.Serialize(new[]
                    {
                        new DashboardActivityEntry("dashboard.layout.updated", "Dashboard seeded", $"A starter dashboard layout was created for {userId}.", DateTimeOffset.UtcNow.ToString("u"))
                    })
                }
            }
        ];
    }

    private static List<DashboardActivityEntry> ParseActivityEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<DashboardActivityEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<DashboardActivityEntry>> LoadActivityEntriesAsync(WebWidgetActionContext actionContext, string activityInstanceId)
    {
        var record = actionContext.Widget.SettingsStore is null
            ? null
            : await actionContext.Widget.SettingsStore.GetAsync(activityInstanceId).ConfigureAwait(false);

        return ParseActivityEntries(record?.Settings.GetValueOrDefault("entries_json"));
    }

    private static string ResolveRelatedActivityInstanceId(string? counterInstanceId)
    {
        if (string.IsNullOrWhiteSpace(counterInstanceId))
            return "dashboard.sidebar.activity";

        if (counterInstanceId.EndsWith(".main.counter", StringComparison.OrdinalIgnoreCase))
            return counterInstanceId[..^".main.counter".Length] + ".sidebar.activity";

        return "dashboard.sidebar.activity";
    }

    private sealed record DashboardActivityEntry(string Channel, string Title, string Detail, string TimestampUtc);

    private sealed class StarterCountryOptionsProvider : IWebFormOptionsProvider
    {
        public Task<IReadOnlyList<WebFormSelectOption>> GetOptionsAsync(WebFormOptionsProviderContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WebFormSelectOption>>(
            [
                new() { Value = "denmark", Label = "Denmark" },
                new() { Value = "sweden", Label = "Sweden" },
                new() { Value = "norway", Label = "Norway" }
            ]);
    }

    private sealed class StarterOfficeOptionsProvider : IWebFormOptionsProvider
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<WebFormSelectOption>> Offices =
            new Dictionary<string, IReadOnlyList<WebFormSelectOption>>(StringComparer.OrdinalIgnoreCase)
            {
                ["denmark"] =
                [
                    new() { Value = "copenhagen", Label = "Copenhagen Studio" },
                    new() { Value = "aarhus", Label = "Aarhus Lab" }
                ],
                ["sweden"] =
                [
                    new() { Value = "stockholm", Label = "Stockholm Hub" },
                    new() { Value = "malmo", Label = "Malmo Dock" }
                ],
                ["norway"] =
                [
                    new() { Value = "oslo", Label = "Oslo Signal House" },
                    new() { Value = "bergen", Label = "Bergen North Deck" }
                ]
            };

        public Task<IReadOnlyList<WebFormSelectOption>> GetOptionsAsync(WebFormOptionsProviderContext context, CancellationToken cancellationToken = default)
        {
            var country = context.GetValue(nameof(ProfileIntakeForm.Country)) ?? string.Empty;
            return Task.FromResult(Offices.TryGetValue(country, out var options)
                ? options
                : (IReadOnlyList<WebFormSelectOption>)[]);
        }
    }

    private sealed record StarterMentorOption(string Value, string Label, string Office, string Country, string Specialty)
    {
        public static readonly IReadOnlyList<StarterMentorOption> All =
        [
            new("mentor-alina-fjord", "Alina Fjord", "Copenhagen Studio", "denmark", "Brand systems"),
            new("mentor-kasper-lund", "Kasper Lund", "Aarhus Lab", "denmark", "Edge APIs"),
            new("mentor-saga-nyberg", "Saga Nyberg", "Stockholm Hub", "sweden", "Search UX"),
            new("mentor-linn-dahl", "Linn Dahl", "Malmo Dock", "sweden", "Commerce flows"),
            new("mentor-erik-frost", "Erik Frost", "Oslo Signal House", "norway", "Realtime pipelines"),
            new("mentor-ida-vik", "Ida Vik", "Bergen North Deck", "norway", "Content modeling")
        ];
    }
}
