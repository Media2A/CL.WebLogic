using CodeLogic.Core.Configuration;

namespace MiniBlog.Config;

public sealed class MiniBlogConfig : ConfigModelBase
{
    public string SiteTitle { get; set; } = "Northwind Journal";
    public string Tagline { get; set; } = "A polished CL.WebLogic sample with a plugin-owned admin site.";
    public string ThemeName { get; set; } = "Editorial Current";
    public string ConnectionId { get; set; } = "Default";
    public string PublicBaseUrl { get; set; } = "http://127.0.0.1:53260";
}
