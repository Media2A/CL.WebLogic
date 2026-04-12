using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;

namespace MiniBlog;

public sealed partial class MiniBlogApplication
{
    private void RegisterFallbacks(WebRegistrationContext context)
    {
        context.RegisterFallback(new WebRouteOptions
        {
            Name = "MiniBlog Not Found",
            Description = "Themed fallback page for the MiniBlog sample.",
            Tags = ["miniblog", "fallback"]
        }, request => Task.FromResult(WebResult.Template(
            "templates/not-found.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = "Not found",
                ["hero_title"] = "Page not found",
                ["hero_copy"] = $"There is no page at {request.Path}.",
                ["path"] = request.Path
            },
            CreateMeta(request, "Not Found | Northwind Journal", $"The requested path {request.Path} could not be found.", ["404", "miniblog"]),
            404)), "GET", "POST");
    }
}
