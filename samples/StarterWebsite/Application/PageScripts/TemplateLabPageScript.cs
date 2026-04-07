using CL.WebLogic.Runtime;

namespace StarterWebsite.Application.PageScripts;

public sealed class TemplateLabPageScript : IWebPageScript
{
    public Task<WebResult> ExecuteAsync(WebPageScriptContext context)
    {
        var request = context.Request;
        var features = new object[]
        {
            new { Title = "Layouts", Description = "Pages can define named sections and flow into a shared shell through {layout:...} and {renderbody}." },
            new { Title = "Widgets", Description = "Widgets are server-side render units that get full request context and can be registered by the app or plugins." },
            new { Title = "Page scripts", Description = "A page script prepares the model in C# without dragging in MVC or Razor page-model ceremony." },
            new { Title = "Loops and conditions", Description = "Templates can iterate collections and show blocks only when auth or access-group conditions match." }
        };

        var capabilities = new object[]
        {
            new { Label = "Current path", Value = request.Path },
            new { Label = "Method", Value = request.Method },
            new { Label = "User", Value = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId },
            new { Label = "Groups", Value = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups) }
        };

        return Task.FromResult(context.Document(
            "templates/template-lab.html",
            new Dictionary<string, object?>
            {
                ["page_title"] = "Template Engine Lab",
                ["page_eyebrow"] = "Template engine v2",
                ["hero_title"] = "Layouts, widgets, and page scripts are now part of the foundation",
                ["hero_copy"] = "This page is produced by a C# page script and rendered through the new template engine. The template itself stays HTML-first while the script prepares the model.",
                ["features"] = features,
                ["capabilities"] = capabilities,
                ["is_authenticated"] = request.IsAuthenticated,
                ["current_user"] = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId
            },
            new WebPageMeta
            {
                Title = "Template Engine Lab | Starter Website",
                Description = "A CL.WebLogic page-script demo showing layouts, widgets, loops, conditionals, and request-aware rendering.",
                CanonicalUrl = $"http://127.0.0.1:53248{request.Path}",
                Language = "en",
                Keywords = ["weblogic", "template engine", "widgets", "page scripts"],
                OpenGraph = new WebOpenGraphMeta
                {
                    Title = "Template Engine Lab",
                    Description = "See the new CL.WebLogic template engine features in one page.",
                    Type = "website",
                    Url = $"http://127.0.0.1:53248{request.Path}",
                    SiteName = "Starter Website"
                },
                Twitter = new WebTwitterMeta
                {
                    Card = "summary",
                    Title = "Template Engine Lab",
                    Description = "CL.WebLogic template engine and page-script demo."
                }
            }));
    }
}
