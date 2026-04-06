using CodeLogic.Core.Configuration;

namespace StarterWebsite.Config;

public sealed class StarterWebsiteConfig : ConfigModelBase
{
    public string SiteTitle { get; set; } = "Starter Website";
    public string Tagline { get; set; } = "Custom routes, plugins, and themes on CL.WebLogic";
    public string ThemeName { get; set; } = "Harbor Starter";
}
