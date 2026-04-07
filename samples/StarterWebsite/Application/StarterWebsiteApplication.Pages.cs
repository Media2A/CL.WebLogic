using CL.WebLogic;
using CL.WebLogic.Forms;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using StarterWebsite.Application.Forms;
using StarterWebsite.Application.PageScripts;
using System.Text.Json;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
    private void RegisterPages(WebRegistrationContext context)
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
    }
}
