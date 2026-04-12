using CL.WebLogic.Routing;
using CL.WebLogic.Theming;

namespace MiniBlog;

public sealed partial class MiniBlogApplication
{
    private void RegisterWidgets(WebRegistrationContext context)
    {
        context.RegisterWidget("blog.latest-posts", new WebWidgetOptions
        {
            Description = "Public blog widget showing the newest published posts.",
            Tags = ["blog", "widget", "posts"]
        }, async widgetContext =>
        {
            var posts = await GetPublishedPostsAsync().ConfigureAwait(false);
            return WebWidgetResult.Template("widgets/latest-posts.html", new Dictionary<string, object?>
            {
                ["title"] = widgetContext.GetParameter("title", "Latest posts") ?? "Latest posts",
                ["posts"] = posts.Take(4).ToArray()
            });
        });

        context.RegisterWidgetArea("home.sidebar", "blog.latest-posts", new WebWidgetAreaOptions
        {
            Description = "Latest post widget on the blog home sidebar.",
            Order = 10,
            InstanceId = "home.sidebar.latest-posts",
            IncludeRoutePatterns = ["/", "/posts"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Fresh from the journal"
            }
        });
    }
}
