using CL.WebLogic;
using CL.WebLogic.Forms;
using CL.WebLogic.MySql;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CL.WebLogic.Theming;
using CodeLogic;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application;
using StarterWebsite.Application.Forms;
using StarterWebsite.Application.PageScripts;
using StarterWebsite.Config;
using System.Text;
using System.Text.Json;

namespace StarterWebsite.Application;

public sealed class StarterWebsiteApplication : IApplication, IWebRouteContributor
{
    private static readonly WebFormSchemaOptions ProfileFormSchemaOptions = new()
    {
        FieldOverrides = new Dictionary<string, WebFormFieldOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["FavoriteColor"] = new WebFormFieldOverride
            {
                HelpText = "These options are supplied by the server at render time, not hardcoded in the template.",
                Options =
                [
                    new WebFormSelectOption { Value = "amber", Label = "Amber Glow" },
                    new WebFormSelectOption { Value = "teal", Label = "Teal Current" },
                    new WebFormSelectOption { Value = "coral", Label = "Coral Burst" },
                    new WebFormSelectOption { Value = "slate", Label = "Slate Mode" }
                ]
            }
        }
    };

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
        web.FormOptionsProviders.Register("starter.countries", new StarterCountryOptionsProvider());
        web.FormOptionsProviders.Register("starter.offices", new StarterOfficeOptionsProvider());
        web.FormOptionsProviders.RegisterHttpLookup("starter.mentors", new WebFormHttpLookupOptions
        {
            UrlTemplate = "/api/demo/lookups/mentors?country={Country}&term={term}",
            RootProperty = "items",
            ValueProperty = "id",
            LabelProperty = "label"
        });
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

        if (web.WidgetSettingsStore is not null)
        {
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

        if (web.DashboardLayouts is not null && web.WidgetSettingsStore is not null)
        {
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

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterWidget("starter.quick-links", new WebWidgetOptions
        {
            Description = "Renders a themed grid of starter demo links.",
            Tags = ["starter", "widget", "links"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Jump around the starter"
            }
        }, _ => Task.FromResult(WebWidgetResult.Template(
            "widgets/quick-links.html",
            new Dictionary<string, object?>
            {
                ["title"] = _.GetParameter("title", "Jump around the starter") ?? "Jump around the starter",
                ["links"] = BuildQuickLinks()
            })));

        context.RegisterWidget("starter.request-glance", new WebWidgetOptions
        {
            Description = "Compact card showing the current request and RBAC identity.",
            Tags = ["starter", "widget", "context"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Current request"
            }
        }, widgetContext => Task.FromResult(WebWidgetResult.Template(
            "widgets/request-glance.html",
            new Dictionary<string, object?>
            {
                ["title"] = widgetContext.GetParameter("title", "Current request") ?? "Current request",
                ["user"] = string.IsNullOrWhiteSpace(widgetContext.Request.UserId) ? "anonymous" : widgetContext.Request.UserId,
                ["path"] = widgetContext.Request.Path,
                ["method"] = widgetContext.Request.Method,
                ["groups"] = widgetContext.Request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", widgetContext.Request.AccessGroups)
            })));

        context.RegisterWidget("starter.counter-panel", new WebWidgetOptions
        {
            Description = "Interactive counter widget backed by persisted widget instance settings.",
            Tags = ["starter", "widget", "actions", "data"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Counter panel",
                ["count"] = "0"
            },
            DataHandler = async widgetContext =>
            {
                var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
                var title = widgetContext.GetParameter("title", "Counter panel") ?? "Counter panel";
                var countText = widgetContext.GetParameter("count", "0") ?? "0";
                _ = int.TryParse(countText, out var count);

                return new
                {
                    widget = widgetContext.Name,
                    widgetContext.InstanceId,
                    title,
                    count,
                    updatedUtc = settings?.UpdatedUtc
                };
            },
            ActionHandler = async actionContext =>
            {
                var title = actionContext.Widget.GetParameter("title", "Counter panel") ?? "Counter panel";
                var countText = actionContext.Widget.GetParameter("count", "0") ?? "0";
                _ = int.TryParse(countText, out var count);

                switch (actionContext.ActionName)
                {
                    case "increment":
                        count++;
                        break;
                    case "reset":
                        count = 0;
                        break;
                    default:
                        return WebWidgetActionResult.Ok(new { message = "Unknown action ignored." });
                }

                await actionContext.SaveSettingsAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = title,
                    ["count"] = count.ToString()
                }).ConfigureAwait(false);

                var activityInstanceId = ResolveRelatedActivityInstanceId(actionContext.Widget.InstanceId);
                var activityEntries = await LoadActivityEntriesAsync(actionContext, activityInstanceId).ConfigureAwait(false);
                activityEntries.Insert(0, new DashboardActivityEntry(
                    $"starter.counter.{actionContext.ActionName}",
                    actionContext.ActionName.Equals("increment", StringComparison.OrdinalIgnoreCase) ? "Counter incremented" : "Counter reset",
                    $"The dashboard counter is now {count}.",
                    DateTimeOffset.UtcNow.ToString("u")));

                if (activityEntries.Count > 6)
                    activityEntries.RemoveRange(6, activityEntries.Count - 6);

                await actionContext.SaveSettingsAsync(
                    activityInstanceId,
                    "starter.activity-stream",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = actionContext.Widget.InstanceId?.Contains(".starter-main.", StringComparison.OrdinalIgnoreCase) == true
                            ? $"Activity for {actionContext.Widget.Request.UserId}"
                            : "Dashboard activity",
                        ["entries_json"] = JsonSerializer.Serialize(activityEntries)
                    }).ConfigureAwait(false);

                return WebWidgetActionResult.Ok(
                    new
                    {
                        title,
                        count
                    },
                    refresh: new WebWidgetRefreshPlan
                    {
                        WidgetInstances = [actionContext.Widget.InstanceId ?? string.Empty, activityInstanceId],
                        WidgetAreas = ["dashboard.main", "dashboard.sidebar"],
                        RefreshWidgetAreas = true
                    },
                    messages:
                    [
                        new WebWidgetClientMessage
                        {
                            Level = WebWidgetClientMessageLevel.Success,
                            Title = "Counter updated",
                            Detail = $"The dashboard counter is now {count}."
                        }
                    ],
                    events:
                    [
                        new WebWidgetClientEvent
                        {
                            Channel = "dashboard.counter.updated",
                            Name = actionContext.ActionName,
                            Payload = new
                            {
                                count,
                                widget = actionContext.Widget.Name,
                                instanceId = actionContext.Widget.InstanceId
                            }
                        }
                    ]);
            },
            Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["increment"] = "Increase the counter by one.",
                ["reset"] = "Reset the counter to zero."
            }
        }, async widgetContext =>
        {
            var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
            var title = widgetContext.GetParameter("title", "Counter panel") ?? "Counter panel";
            var countText = widgetContext.GetParameter("count", "0") ?? "0";
            _ = int.TryParse(countText, out var count);

            return WebWidgetResult.Template(
                "widgets/counter-panel.html",
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["count"] = count,
                    ["instance_id"] = widgetContext.InstanceId ?? string.Empty,
                    ["widget_name"] = widgetContext.Name,
                    ["updated_utc"] = settings?.UpdatedUtc.ToString("u") ?? "not saved yet"
                });
        });

        context.RegisterWidget("starter.activity-stream", new WebWidgetOptions
        {
            Description = "Shows widget action activity so dashboard widgets can react to each other through saved state and refresh channels.",
            Tags = ["starter", "widget", "activity", "messaging"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Activity stream"
            },
            DataHandler = async widgetContext =>
            {
                var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
                return new
                {
                    widget = widgetContext.Name,
                    widgetContext.InstanceId,
                    title = widgetContext.GetParameter("title", "Activity stream") ?? "Activity stream",
                    entries = ParseActivityEntries(settings?.Settings.GetValueOrDefault("entries_json"))
                };
            }
        }, async widgetContext =>
        {
            var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
            return WebWidgetResult.Template(
                "widgets/activity-stream.html",
                new Dictionary<string, object?>
                {
                    ["title"] = widgetContext.GetParameter("title", "Activity stream") ?? "Activity stream",
                    ["entries"] = ParseActivityEntries(settings?.Settings.GetValueOrDefault("entries_json")),
                    ["instance_id"] = widgetContext.InstanceId ?? string.Empty,
                    ["widget_name"] = widgetContext.Name
                });
        });

        context.RegisterWidgetArea("dashboard.main", "starter.quick-links", new WebWidgetAreaOptions
        {
            Description = "Starter quick links in the main dashboard area.",
            Order = 10,
            InstanceId = "dashboard.main.quicklinks",
            IncludeRoutePatterns = ["/dashboard", "/"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Starter navigation"
            }
        });

        context.RegisterWidgetArea("dashboard.main", "starter.counter-panel", new WebWidgetAreaOptions
        {
            Description = "Interactive counter widget in the dashboard main area.",
            Order = 20,
            InstanceId = "dashboard.main.counter",
            IncludeRoutePatterns = ["/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Dashboard counter",
                ["count"] = "1"
            }
        });

        context.RegisterWidgetArea("dashboard.sidebar", "starter.request-glance", new WebWidgetAreaOptions
        {
            Description = "Starter request card in the sidebar dashboard area.",
            Order = 10,
            InstanceId = "dashboard.sidebar.request",
            IncludeRoutePatterns = ["/dashboard", "/about", "/template-lab"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Sidebar request view"
            }
        });

        context.RegisterWidgetArea("dashboard.sidebar", "starter.activity-stream", new WebWidgetAreaOptions
        {
            Description = "Dashboard activity widget showing widget-to-widget refresh flow.",
            Order = 20,
            InstanceId = "dashboard.sidebar.activity",
            IncludeRoutePatterns = ["/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Dashboard activity"
            }
        });

        context.RegisterWidgetArea("site.sidebar", "starter.request-glance", new WebWidgetAreaOptions
        {
            Description = "Shared sidebar request widget for selected pages.",
            Order = 10,
            InstanceId = "site.sidebar.request",
            IncludeRoutePatterns = ["/about", "/template-lab", "/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Page sidebar"
            }
        });

        context.RegisterWidgetArea("member.sidebar", "starter.quick-links", new WebWidgetAreaOptions
        {
            Description = "Authenticated quick-links sidebar for member areas.",
            Order = 20,
            InstanceId = "member.sidebar.quicklinks",
            IncludeRoutePatterns = ["/rbac", "/rbac/*"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["member", "editor", "staff", "admin", "beta"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Member navigation"
            }
        });

        context.RegisterPage("/", new WebRouteOptions
        {
            Name = "Starter Home",
            Description = "Application-owned homepage rendered through the shared CL.WebLogic theme.",
            Tags = ["starter", "page", "home"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/home.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = _config.SiteTitle,
                ["page_eyebrow"] = "Starter website",
                ["hero_title"] = _config.SiteTitle,
                ["hero_copy"] = "This route is registered by the application. The page now uses a shared layout, partials, widgets, and request-aware tokens from the new template engine.",
                ["tagline"] = _config.Tagline,
                ["theme_name"] = _config.ThemeName,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups),
                ["snapshot_cards"] = BuildSnapshotCards(request),
                ["template_features"] = BuildTemplateFeatureCards()
            },
            CreateMeta(
                request,
                _config.SiteTitle,
                "Filesystem-first CL.WebLogic starter website with widgets, routes, plugins, and a custom template engine.",
                ["weblogic", "starter website", "widgets", "plugins"]))), "GET");

        context.RegisterPageScript<TemplateLabPageScript>("/template-lab", new WebRouteOptions
        {
            Name = "Template Lab",
            Description = "Shows layouts, widgets, loops, and page-script integration in one page.",
            Tags = ["starter", "page", "templates", "widgets"]
        }, "GET");

        context.RegisterPage("/about", new WebRouteOptions
        {
            Name = "About",
            Description = "Shows page-context values gathered from the request.",
            Tags = ["starter", "page", "context"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/about.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = "About",
                ["page_eyebrow"] = "About this starter",
                ["hero_title"] = _config.SiteTitle,
                ["hero_copy"] = "Starter websites should be small, obvious, and easy to extend. This page now renders through the shared layout and a request-aware widget.",
                ["theme_name"] = _config.ThemeName,
                ["plugin_count"] = "2",
                ["page_path"] = request.Path,
                ["request_method"] = request.Method,
                ["current_user"] = request.UserId == string.Empty ? "anonymous" : request.UserId,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups),
                ["about_cards"] = BuildAboutCards()
            },
            CreateMeta(
                request,
                "About | Starter Website",
                "See how CL.WebLogic exposes page context, plugins, and theme-driven rendering in the starter site.",
                ["weblogic", "about", "page context"]))), "GET");

        context.RegisterPage("/form-lab", new WebRouteOptions
        {
            Name = "Form Lab",
            Description = "Shows model-driven forms, client validation, and file/image checks.",
            Tags = ["starter", "page", "forms", "validation"]
        }, request =>
        {
            var renderState = new WebFormRenderState
            {
                Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AudienceSegment"] = request.IsAuthenticated ? "signed-in member" : "guest prospect",
                    ["FormIntent"] = "profile-intake"
                }
            };

            var schema = request.Forms.GetClientSchema<ProfileIntakeForm>(ProfileFormSchemaOptions);
            var generatedForm = request.Forms.RenderForm<ProfileIntakeForm>(renderState, new WebFormRenderOptions
            {
                Action = "/api/forms/profile-intake",
                Method = "post",
                SchemaId = "starter-profile-form-schema",
                SchemaOptions = ProfileFormSchemaOptions,
                Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["data-profile-intake-form"] = string.Empty
                }
            });
            return Task.FromResult(WebResult.Template(
                "templates/form-lab.html",
                new Dictionary<string, object?>
                {
                    ["page_title"] = "Form Lab",
                    ["page_eyebrow"] = "Model-driven forms",
                    ["hero_title"] = "One C# form model, both client and server",
                    ["hero_copy"] = "This page proves the new forms foundation: the browser validates against a CL.WebLogic schema generated from C#, and the server binds and validates the exact same model through request.Forms.",
                    ["form_schema_json"] = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }),
                    ["generated_form_html"] = generatedForm
                },
                CreateMeta(
                    request,
                    "Form Lab | Starter Website",
                    "C# form models driving client validation, server binding, and file/image validation in CL.WebLogic.",
                    ["weblogic", "forms", "validation", "uploads"]))); 
        }, "GET");

        context.RegisterPage("/dashboard", new WebRouteOptions
        {
            Name = "Widget Dashboard",
            Description = "Dashboard demo that loads widget previews over AJAX from the WebLogic widget render endpoint.",
            Tags = ["starter", "page", "dashboard", "widgets"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/dashboard.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = "Widget Dashboard",
                ["page_eyebrow"] = "Dashboard demo",
                ["hero_title"] = "AJAX-loaded widgets running on top of CL.WebLogic",
                ["hero_copy"] = "This page discovers widgets from the runtime, sorts them, and loads each preview over HTTP through the built-in widget and widget-area render endpoints.",
                ["dashboard_filters"] = BuildDashboardFilters(request)
            },
            CreateMeta(
                request,
                "Widget Dashboard | Starter Website",
                "Explore CL.WebLogic widgets, widget areas, AJAX rendering, and dashboard composition in the starter demo.",
                ["weblogic", "dashboard", "widgets", "ajax"]))), "GET");

        context.RegisterPage("/dashboard/studio", new WebRouteOptions
        {
            Name = "Dashboard Studio",
            Description = "Personal dashboard builder with add, remove, and reorder support stored per user.",
            Tags = ["starter", "page", "dashboard", "studio"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["member", "editor", "staff", "admin", "beta"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/dashboard-studio.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = "Dashboard Studio",
                ["page_eyebrow"] = "Personal workspace",
                ["hero_title"] = "Build your own widget dashboard",
                ["hero_copy"] = "This page stores widget placement and ordering per signed-in user in MySQL, then renders the saved zones back through CL.WebLogic.",
                ["dashboard_key"] = "starter-main",
                ["current_user"] = request.UserId,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
            },
            CreateMeta(
                request,
                "Dashboard Studio | Starter Website",
                "Build and save a personalized CL.WebLogic dashboard layout with reusable widget functions and stored layout state.",
                ["weblogic", "dashboard studio", "layout", "widgets"]))), "GET");

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
                    true),
                    CreateMeta(
                        request,
                        "Login | Starter Website",
                        "Sign in to the starter RBAC demo and personal dashboard studio.",
                        ["weblogic", "login", "rbac"]));
            }

            if (request.IsAuthenticated)
                return Redirect(request, "/rbac");

            var requestedReturnUrl = SanitizeReturnUrl(request.GetQuery("returnUrl")) ?? "/rbac";
            var signedOut = string.Equals(request.GetQuery("signedOut"), "1", StringComparison.OrdinalIgnoreCase);
            return WebResult.Template("templates/login.html", BuildLoginModel(
                requestedReturnUrl,
                signedOut ? "You have been signed out." : string.Empty,
                false),
                CreateMeta(
                    request,
                    "Login | Starter Website",
                    "Sign in to the starter RBAC demo and personal dashboard studio.",
                    ["weblogic", "login", "rbac"]));
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
            },
            CreateMeta(
                request,
                "RBAC Hub | Starter Website",
                "Starter RBAC hub showing session-backed identity and access-group-aware pages.",
                ["weblogic", "rbac", "auth"]))), "GET");

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
            },
            CreateMeta(
                request,
                "Admin RBAC Page | Starter Website",
                "Admin-only RBAC test page in the CL.WebLogic starter demo.",
                ["weblogic", "rbac", "admin"]))), "GET");

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
            },
            CreateMeta(
                request,
                "Editor RBAC Page | Starter Website",
                "Editor-or-admin RBAC test page in the CL.WebLogic starter demo.",
                ["weblogic", "rbac", "editor"]))), "GET");

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

        context.RegisterApi("/api/demo/lookups/mentors", new WebRouteOptions
        {
            Name = "Starter Mentor Lookup",
            Description = "JSON lookup endpoint used by the generic HTTP-backed WebLogic form lookup provider.",
            Tags = ["starter", "api", "forms", "lookup"]
        }, request =>
        {
            var country = request.GetQuery(nameof(ProfileIntakeForm.Country)) ?? request.GetQuery("country") ?? string.Empty;
            var term = request.GetQuery("term") ?? string.Empty;
            var items = StarterMentorOption.All
                .Where(mentor => string.IsNullOrWhiteSpace(country) || string.Equals(mentor.Country, country, StringComparison.OrdinalIgnoreCase))
                .Where(mentor =>
                    string.IsNullOrWhiteSpace(term) ||
                    mentor.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Specialty.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Office.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .Select(static mentor => new
                {
                    id = mentor.Value,
                    label = $"{mentor.Label} · {mentor.Office} · {mentor.Specialty}",
                    mentor.Country,
                    mentor.Office,
                    mentor.Specialty
                })
                .ToArray();

            return Task.FromResult(WebResult.Json(new { items }));
        }, "GET");

        context.RegisterApi("/api/forms/profile-intake", new WebRouteOptions
        {
            Name = "Profile Intake Submit",
            Description = "Binds and validates the starter profile intake form using request.Forms.",
            Tags = ["starter", "api", "forms", "validation"]
        }, async request =>
        {
            var submission = await request.Forms.BindAsync<ProfileIntakeForm>(ProfileFormSchemaOptions).ConfigureAwait(false);
            if (!submission.IsValid)
            {
                return WebResult.Json(new
                {
                    success = false,
                    message = "Validation failed. Fix the highlighted fields and try again.",
                    errors = submission.Errors,
                    values = submission.Values
                }, 400);
            }

            var file = submission.Files.GetValueOrDefault("ProfileImage");
            var command = submission.MapTo<ProfileIntakeCommand>();
            var record = WebFormMapper.Map<ProfileIntakeCommand, ProfileIntakeRecord>(command);
            return WebResult.Json(new
            {
                success = true,
                message = "The form passed both client and server validation.",
                normalized = new
                {
                    displayName = submission.Model.DisplayName,
                    emailAddress = submission.Model.EmailAddress,
                    country = submission.Model.Country,
                    localOffice = submission.Model.LocalOffice,
                    preferredMentorId = submission.Model.PreferredMentorId,
                    age = submission.Model.Age,
                    favoriteColor = submission.Model.FavoriteColor,
                    bio = submission.Model.Bio,
                    audienceSegment = submission.Model.AudienceSegment,
                    formIntent = submission.Model.FormIntent
                },
                mapped = new
                {
                    command,
                    record
                },
                upload = file is null
                    ? null
                    : new
                    {
                        file.FileName,
                        file.ContentType,
                        file.Length
                    }
            });
        }, "POST");

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
            CreateMeta(
                request,
                "Page Not Found | Starter Website",
                $"The requested path {request.Path} was not found in the starter site.",
                ["weblogic", "404", "not found"]),
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
        new { Label = "Form lab", Url = "/form-lab", Description = "C# forms, client validation, and uploads" },
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

    private sealed class StarterMentorSearchProvider : IWebFormSearchProvider
    {
        private static readonly IReadOnlyList<StarterMentorOption> Mentors =
        [
            new("mentor-alina-fjord", "Alina Fjord", "Copenhagen Studio", "denmark", "Brand systems"),
            new("mentor-kasper-lund", "Kasper Lund", "Aarhus Lab", "denmark", "Edge APIs"),
            new("mentor-saga-nyberg", "Saga Nyberg", "Stockholm Hub", "sweden", "Search UX"),
            new("mentor-linn-dahl", "Linn Dahl", "Malmo Dock", "sweden", "Commerce flows"),
            new("mentor-erik-frost", "Erik Frost", "Oslo Signal House", "norway", "Realtime pipelines"),
            new("mentor-ida-vik", "Ida Vik", "Bergen North Deck", "norway", "Content modeling")
        ];

        public Task<IReadOnlyList<WebFormSelectOption>> GetOptionsAsync(WebFormOptionsProviderContext context, CancellationToken cancellationToken = default)
        {
            var country = context.GetValue(nameof(ProfileIntakeForm.Country));
            var options = Mentors
                .Where(mentor => string.IsNullOrWhiteSpace(country) || string.Equals(mentor.Country, country, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(static mentor => mentor.ToOption())
                .ToArray();

            return Task.FromResult<IReadOnlyList<WebFormSelectOption>>(options);
        }

        public Task<IReadOnlyList<WebFormSelectOption>> SearchAsync(WebFormSearchProviderContext context, CancellationToken cancellationToken = default)
        {
            var country = context.GetValue(nameof(ProfileIntakeForm.Country));
            var term = context.SearchTerm ?? string.Empty;
            var options = Mentors
                .Where(mentor => string.IsNullOrWhiteSpace(country) || string.Equals(mentor.Country, country, StringComparison.OrdinalIgnoreCase))
                .Where(mentor =>
                    string.IsNullOrWhiteSpace(term) ||
                    mentor.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Specialty.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Office.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .Select(static mentor => mentor.ToOption())
                .ToArray();

            return Task.FromResult<IReadOnlyList<WebFormSelectOption>>(options);
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

        public WebFormSelectOption ToOption() => new()
        {
            Value = Value,
            Label = $"{Label} · {Office} · {Specialty}"
        };
    }
}
