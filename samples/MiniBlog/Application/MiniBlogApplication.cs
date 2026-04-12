using CL.WebLogic.Routing;
using CodeLogic.Framework.Application;
using MiniBlog.Config;

namespace MiniBlog;

public sealed partial class MiniBlogApplication : IApplication, IWebRouteContributor
{
    public ApplicationManifest Manifest { get; } = new()
    {
        Id = "miniblog.app",
        Name = "MiniBlog",
        Version = "1.0.0",
        Description = "Public MiniBlog sample for CL.WebLogic",
        Author = "Media2A"
    };

    private MiniBlogConfig _config = new();

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        RegisterWidgets(context);
        RegisterPages(context);
        RegisterFallbacks(context);
        return Task.CompletedTask;
    }
}
