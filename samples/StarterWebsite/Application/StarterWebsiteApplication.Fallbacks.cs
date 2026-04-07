using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
    private void RegisterFallbacks(WebRegistrationContext context)
    {
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
    }
}
