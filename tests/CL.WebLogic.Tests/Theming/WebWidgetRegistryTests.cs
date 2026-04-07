using CL.WebLogic.Routing;
using CL.WebLogic.Theming;
using Xunit;

namespace CL.WebLogic.Tests.Theming;

public sealed class WebWidgetRegistryTests
{
    private static readonly WebContributorDescriptor AppContributor = new()
    {
        Id = "starter.app",
        Name = "Starter App",
        Kind = "Application",
        Description = "Starter app contributor"
    };

    private static readonly WebContributorDescriptor PluginContributor = new()
    {
        Id = "starter.plugin",
        Name = "Starter Plugin",
        Kind = "Plugin",
        Description = "Starter plugin contributor"
    };

    [Fact]
    public void RegisterWidget_ExposesDescriptorMetadata()
    {
        var registry = new WebWidgetRegistry();

        registry.Register(
            "starter.counter",
            _ => Task.FromResult(WebWidgetResult.HtmlContent("<div>counter</div>")),
            AppContributor,
            new WebWidgetOptions
            {
                Description = "Counter widget",
                Tags = ["starter", "actions"],
                AllowAnonymous = false,
                RequiredAccessGroups = ["admin"],
                SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = "Counter"
                },
                DataHandler = _ => Task.FromResult<object?>(new { count = 1 }),
                ActionHandler = _ => Task.FromResult(WebWidgetActionResult.Ok()),
                Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["increment"] = "Increase the counter."
                }
            });

        var descriptor = Assert.Single(registry.GetDescriptors());
        Assert.Equal("starter.counter", descriptor.Name);
        Assert.Equal("starter.app", descriptor.SourceId);
        Assert.Equal("Application", descriptor.SourceKind);
        Assert.False(descriptor.AllowAnonymous);
        Assert.Equal(["admin"], descriptor.RequiredAccessGroups);
        Assert.True(descriptor.HasData);
        Assert.True(descriptor.HasActions);
        Assert.Equal("Counter", descriptor.SampleParameters["title"]);
        Assert.Equal("Increase the counter.", descriptor.Actions["increment"]);
    }

    [Fact]
    public void RegisterAreaWidget_ReplacesExistingEntryForSameContributorAndWidget()
    {
        var registry = new WebWidgetRegistry();

        registry.RegisterAreaWidget(
            "dashboard.main",
            "starter.counter",
            AppContributor,
            new WebWidgetAreaOptions
            {
                Order = 10,
                InstanceId = "dashboard.main.counter",
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = "Old title"
                }
            });

        registry.RegisterAreaWidget(
            "dashboard.main",
            "starter.counter",
            AppContributor,
            new WebWidgetAreaOptions
            {
                Order = 30,
                InstanceId = "dashboard.main.counter",
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = "New title"
                }
            });

        var area = Assert.Single(registry.GetAreaWidgets("dashboard.main"));
        Assert.Equal(30, area.Order);
        Assert.Equal("New title", area.Parameters["title"]);
    }

    [Fact]
    public void AreaDescriptors_AreSortedByAreaThenOrderThenSourceThenWidget()
    {
        var registry = new WebWidgetRegistry();

        registry.RegisterAreaWidget(
            "dashboard.sidebar",
            "starter.request-glance",
            PluginContributor,
            new WebWidgetAreaOptions { Order = 20 });

        registry.RegisterAreaWidget(
            "dashboard.main",
            "starter.counter",
            PluginContributor,
            new WebWidgetAreaOptions { Order = 20 });

        registry.RegisterAreaWidget(
            "dashboard.main",
            "starter.quick-links",
            AppContributor,
            new WebWidgetAreaOptions
            {
                Order = 10,
                AllowAnonymous = false,
                RequiredAccessGroups = ["member"],
                IncludeRoutePatterns = ["/dashboard"],
                ExcludeRoutePatterns = ["/dashboard/admin"]
            });

        var descriptors = registry.GetAreaDescriptors();

        Assert.Equal(3, descriptors.Count);
        Assert.Equal("dashboard.main", descriptors[0].AreaName);
        Assert.Equal("starter.quick-links", descriptors[0].WidgetName);
        Assert.False(descriptors[0].AllowAnonymous);
        Assert.Equal(["member"], descriptors[0].RequiredAccessGroups);
        Assert.Equal(["/dashboard"], descriptors[0].IncludeRoutePatterns);
        Assert.Equal(["/dashboard/admin"], descriptors[0].ExcludeRoutePatterns);

        Assert.Equal("dashboard.main", descriptors[1].AreaName);
        Assert.Equal("starter.counter", descriptors[1].WidgetName);
        Assert.Equal("dashboard.sidebar", descriptors[2].AreaName);
    }
}
