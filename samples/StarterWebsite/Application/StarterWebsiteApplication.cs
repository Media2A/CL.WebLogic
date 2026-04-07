using CL.WebLogic.Forms;
using CL.WebLogic.Routing;
using CodeLogic.Framework.Application;
using StarterWebsite.Config;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication : IApplication, IWebRouteContributor
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

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        RegisterWidgetsAndAreas(context);
        RegisterPages(context);
        RegisterApis(context);
        RegisterFallbacks(context);
        return Task.CompletedTask;
    }
}
