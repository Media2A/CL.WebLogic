using CL.WebLogic.Forms;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CodeLogic.Framework.Application.Plugins;
using CodeLogic.Framework.Libraries;
using MiniBlog.Admin.Plugin.Config;
using MiniBlog.Shared.Models;
using MiniBlog.Shared.Services;

namespace MiniBlog.Admin.Plugin.Admin;

public sealed class MiniBlogAdminPlugin : IPlugin, IWebRouteContributor
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "MiniBlog.Admin.Plugin",
        Name = "MiniBlog Admin Plugin",
        Version = "1.0.0",
        Description = "Plugin-owned administration site for the MiniBlog sample",
        Author = "Media2A"
    };

    public PluginState State { get; private set; } = PluginState.Loaded;
    private AdminPluginConfig _config = new();

    public Task OnConfigureAsync(PluginContext context)
    {
        context.Configuration.Register<AdminPluginConfig>();
        State = PluginState.Configured;
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(PluginContext context)
    {
        _config = context.Configuration.Get<AdminPluginConfig>();
        State = PluginState.Initialized;
        return Task.CompletedTask;
    }

    public Task OnStartAsync(PluginContext context)
    {
        State = PluginState.Started;
        return Task.CompletedTask;
    }

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterWidget("admin.editor-summary", new CL.WebLogic.Theming.WebWidgetOptions
        {
            Description = "Admin summary widget contributed by the MiniBlog admin plugin.",
            Tags = ["miniblog", "admin", "widget"]
        }, async widgetContext =>
        {
            var posts = await new MiniBlogDataService().GetAllPostsAsync().ConfigureAwait(false);
            return CL.WebLogic.Theming.WebWidgetResult.Template("widgets/admin-summary.html", new Dictionary<string, object?>
            {
                ["draft_count"] = posts.Count(post => string.Equals(post.Status, "draft", StringComparison.OrdinalIgnoreCase)),
                ["published_count"] = posts.Count(post => string.Equals(post.Status, "published", StringComparison.OrdinalIgnoreCase)),
                ["title"] = _config.DashboardTitle
            });
        });

        context.RegisterPage("/admin", new WebRouteOptions
        {
            Name = "MiniBlog Admin Dashboard",
            Description = "Plugin-owned admin dashboard.",
            Tags = ["miniblog", "admin", "page"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, async request =>
        {
            var posts = await new MiniBlogDataService().GetAllPostsAsync().ConfigureAwait(false);
            return WebResult.Template("templates/admin-dashboard.html", new Dictionary<string, object?>
            {
                ["page_title"] = "Admin Dashboard",
                ["hero_title"] = _config.DashboardTitle,
                ["hero_copy"] = "This entire administration surface is registered by a plugin DLL and auto-wired into WebLogic at startup.",
                ["posts"] = posts.Take(5).ToArray(),
                ["current_user"] = request.GetSessionValue("miniblog.display_name", request.UserId) ?? request.UserId,
                ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
            });
        }, "GET");

        context.RegisterPage("/admin/posts", new WebRouteOptions
        {
            Name = "Admin Posts",
            Description = "Plugin-owned post management page.",
            Tags = ["miniblog", "admin", "page"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, async request =>
        {
            var posts = await new MiniBlogDataService().GetAllPostsAsync().ConfigureAwait(false);
            return WebResult.Template("templates/admin-posts.html", new Dictionary<string, object?>
            {
                ["page_title"] = "Manage posts",
                ["hero_title"] = "Manage posts",
                ["hero_copy"] = "Drafts and published pieces come from the shared MySQL-backed MiniBlog data service.",
                ["posts"] = posts
            });
        }, "GET");

        context.RegisterPage("/admin/posts/new", new WebRouteOptions
        {
            Name = "New Post",
            Description = "Plugin-owned editor page for a new post.",
            Tags = ["miniblog", "admin", "editor"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/admin-editor.html",
            BuildEditorModel(request, null, "Create a new story", _config.EditorHeadline))),
            "GET");

        context.RegisterPage("/admin/posts/edit/*", new WebRouteOptions
        {
            Name = "Edit Post",
            Description = "Plugin-owned editor page for an existing post.",
            Tags = ["miniblog", "admin", "editor"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, async request =>
        {
            var id = request.Path["/admin/posts/edit/".Length..].Trim('/');
            var post = string.IsNullOrWhiteSpace(id) ? null : await new MiniBlogDataService().GetPostByIdAsync(id).ConfigureAwait(false);
            if (post is null)
                return WebResult.Template("templates/admin-editor.html", BuildEditorModel(request, null, "Create a new story", _config.EditorHeadline));

            return WebResult.Template("templates/admin-editor.html", BuildEditorModel(request, post, $"Editing {post.Title}", "Update copy, metadata, and publish state."));
        }, "GET");

        context.RegisterApi("/api/admin/posts/save", new WebRouteOptions
        {
            Name = "Save Post",
            Description = "Plugin-owned save endpoint for editor forms.",
            Tags = ["miniblog", "admin", "api"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["editor", "admin"]
        }, async request =>
        {
            var submission = await request.Forms.BindAsync<MiniBlogPostEditorForm>().ConfigureAwait(false);
            if (!submission.IsValid)
            {
                return WebResult.Json(new
                {
                    success = false,
                    message = "Validation failed.",
                    errors = submission.Errors,
                    values = submission.Values
                }, 400);
            }

            var command = submission.MapTo<MiniBlogPostUpsertCommand>();
            await new MiniBlogDataService().SavePostAsync(
                command,
                request.UserId,
                request.GetSessionValue("miniblog.display_name", request.UserId) ?? request.UserId).ConfigureAwait(false);

            return WebResult.Json(new
            {
                success = true,
                message = "Post saved successfully.",
                redirectUrl = "/admin/posts"
            });
        }, "POST");

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        State = PluginState.Stopped;
        return Task.CompletedTask;
    }

    public Task<HealthStatus> HealthCheckAsync() =>
        Task.FromResult(HealthStatus.Healthy("MiniBlog admin plugin is active."));

    public void Dispose()
    {
    }

    private static Dictionary<string, object?> BuildEditorModel(WebRequestContext request, MiniBlogPostDetail? post, string title, string copy)
    {
        var form = new WebFormRenderState
        {
            Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(MiniBlogPostEditorForm.PostId)] = post?.Id ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.Title)] = post?.Title ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.Slug)] = post?.Slug ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.Summary)] = post?.Summary ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.BodyHtml)] = post?.BodyHtml ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.Status)] = post?.Status ?? "draft",
                [nameof(MiniBlogPostEditorForm.MetaTitle)] = post?.MetaTitle ?? string.Empty,
                [nameof(MiniBlogPostEditorForm.MetaDescription)] = post?.MetaDescription ?? string.Empty
            }
        };

        var generatedForm = request.Forms.RenderForm<MiniBlogPostEditorForm>(form, new WebFormRenderOptions
        {
            Action = "/api/admin/posts/save",
            Method = "post",
            SchemaId = "miniblog-admin-post-schema",
            SubmitLabel = "Save post",
            IncludeResetButton = false,
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data-miniblog-editor-form"] = string.Empty
            }
        });

        return new Dictionary<string, object?>
        {
            ["page_title"] = title,
            ["hero_title"] = title,
            ["hero_copy"] = copy,
            ["editor_form"] = generatedForm,
            ["status_badge"] = post?.Status ?? "draft"
        };
    }
}
