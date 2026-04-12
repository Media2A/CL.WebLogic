using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;

namespace MiniBlog;

public sealed partial class MiniBlogApplication
{
    private void RegisterPages(WebRegistrationContext context)
    {
        context.RegisterPage("/", new WebRouteOptions
        {
            Name = "MiniBlog Home",
            Description = "Public MiniBlog homepage.",
            Tags = ["miniblog", "page", "home"]
        }, async request =>
        {
            var posts = await GetPublishedPostsAsync().ConfigureAwait(false);
            var model = BuildLayoutModel(request, _config.SiteTitle, _config.SiteTitle, _config.Tagline);
            model["featured_post"] = posts.FirstOrDefault();
            model["recent_posts"] = posts.Skip(1).Take(6).ToArray();
            return WebResult.Template("templates/home.html", model, CreateMeta(
                request,
                _config.SiteTitle,
                "A polished CL.WebLogic reference app with a public blog and a plugin-owned administration site.",
                ["weblogic", "miniblog", "codelogic", "plugins"]));
        }, "GET");

        context.RegisterPage("/posts", new WebRouteOptions
        {
            Name = "Post Listing",
            Description = "Published post listing.",
            Tags = ["miniblog", "page", "posts"]
        }, async request =>
        {
            var pageNumber = ParsePositiveInt(request.GetQuery("page"), 1);
            var postsPage = await GetPublishedPostsPageAsync(pageNumber, 3).ConfigureAwait(false);
            var model = BuildLayoutModel(request, "Posts", "Latest writing", "Published pieces from the public MiniBlog sample.");
            model["posts"] = postsPage.Items;
            model["posts_pager_html"] = BuildPostsPagerHtml(postsPage);
            model["posts_total_items"] = postsPage.TotalItems;
            return WebResult.Template("templates/posts.html", model, CreateMeta(
                request,
                "Posts | Northwind Journal",
                "Browse the published MiniBlog sample posts powered by CL.WebLogic and CL.MySQL2.",
                ["weblogic", "blog posts", "mysql2"]));
        }, "GET");

        context.RegisterPage("/posts/*", new WebRouteOptions
        {
            Name = "Post Detail",
            Description = "Public post detail page.",
            Tags = ["miniblog", "page", "post"]
        }, async request =>
        {
            var slug = request.Path["/posts/".Length..].Trim('/');
            var post = string.IsNullOrWhiteSpace(slug) ? null : await GetPublishedPostBySlugAsync(slug).ConfigureAwait(false);
            if (post is null)
                return Redirect(request, "/posts");

            var model = BuildLayoutModel(request, post.Title, post.Title, post.Summary);
            model["post"] = post;
            return WebResult.Template("templates/post-detail.html", model, CreateMeta(
                request,
                post.MetaTitle,
                post.MetaDescription,
                ["weblogic", "miniblog", post.Slug]));
        }, "GET");

        context.RegisterPage("/login", new WebRouteOptions
        {
            Name = "Login",
            Description = "MiniBlog sign-in page.",
            Tags = ["miniblog", "page", "auth"]
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
                    request.SetSessionValue("miniblog.display_name", identity.DisplayName);
                    return Redirect(request, returnUrl ?? "/admin");
                }

                return WebResult.Template("templates/login.html", BuildLoginModel(returnUrl ?? "/admin", "Login failed. Use one of the seeded demo accounts.", true), CreateMeta(
                    request,
                    "Login | Northwind Journal",
                    "Sign in to reach the plugin-owned MiniBlog administration site.",
                    ["weblogic", "login", "rbac"]));
            }

            var requestedReturnUrl = SanitizeReturnUrl(request.GetQuery("returnUrl")) ?? "/admin";
            var signedOut = string.Equals(request.GetQuery("signedOut"), "1", StringComparison.OrdinalIgnoreCase);
            return WebResult.Template("templates/login.html", BuildLoginModel(requestedReturnUrl, signedOut ? "You have been signed out." : string.Empty, false), CreateMeta(
                request,
                "Login | Northwind Journal",
                "Sign in to reach the plugin-owned MiniBlog administration site.",
                ["weblogic", "login", "rbac"]));
        }, "GET", "POST");

        context.RegisterPage("/logout", new WebRouteOptions
        {
            Name = "Logout",
            Description = "Clears the MiniBlog session.",
            Tags = ["miniblog", "page", "auth"]
        }, request =>
        {
            request.SetSessionValue("weblogic.user_id", null);
            request.SetSessionValue("weblogic.access_groups", null);
            request.SetSessionValue("miniblog.display_name", null);
            return Task.FromResult(Redirect(request, "/login?signedOut=1"));
        }, "GET", "POST");
    }
}
