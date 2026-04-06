using CodeLogic.Core.Configuration;

namespace StarterWebsite.Config;

public sealed class StarterWebsiteConfig : ConfigModelBase
{
    public string SiteTitle { get; set; } = "Starter Website";
    public string Tagline { get; set; } = "Custom routes, plugins, and themes on CL.WebLogic";
    public string ThemeName { get; set; } = "Harbor Starter";
    public List<StarterDemoUser> DemoUsers { get; set; } =
    [
        new StarterDemoUser
        {
            UserId = "demo-admin",
            Password = "admin123",
            DisplayName = "Demo Admin",
            AccessGroups = ["admin", "staff", "beta"]
        },
        new StarterDemoUser
        {
            UserId = "editor-jane",
            Password = "editor123",
            DisplayName = "Editor Jane",
            AccessGroups = ["editor", "staff"]
        },
        new StarterDemoUser
        {
            UserId = "member-max",
            Password = "member123",
            DisplayName = "Member Max",
            AccessGroups = ["member"]
        }
    ];
}

public sealed class StarterDemoUser
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string[] AccessGroups { get; set; } = [];
}
