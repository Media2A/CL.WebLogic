using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using Xunit;

namespace CL.WebLogic.Tests.Routing;

public sealed class WebRouteRegistryTests
{
    private static readonly WebContributorDescriptor Contributor = new()
    {
        Id = "test.app",
        Name = "Test App",
        Kind = "Application",
        Description = "Test contributor"
    };

    private static Task<WebResult> NoopHandler(WebRequestContext _) =>
        Task.FromResult(WebResult.Text("ok"));

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("home", "/home")]
    [InlineData("/home", "/home")]
    [InlineData("/home/", "/home")]
    [InlineData("\\admin\\users", "/admin/users")]
    [InlineData("//a//b///c", "/a/b/c")]
    public void NormalizePath_ProducesExpectedShape(string? input, string expected)
    {
        Assert.Equal(expected, WebRouteRegistry.NormalizePath(input));
    }

    [Fact]
    public void RegisterPage_DefaultsToGetMethod()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/home", NoopHandler);

        Assert.True(registry.TryGet("/home", out var route));
        Assert.NotNull(route);
        Assert.Equal(WebRouteKind.Page, route!.Kind);
        Assert.Contains("GET", route.Methods, StringComparer.OrdinalIgnoreCase);
        Assert.Single(route.Methods);
    }

    [Fact]
    public void RegisterApi_HonorsExplicitMethods_AndUppercasesThem()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterApi("/api/thing", NoopHandler, "post", "Put");

        Assert.True(registry.TryGet("/api/thing", out var route));
        Assert.NotNull(route);
        Assert.Equal(WebRouteKind.Api, route!.Kind);
        Assert.Contains("POST", route.Methods);
        Assert.Contains("PUT", route.Methods);
        Assert.DoesNotContain("GET", route.Methods);
    }

    [Fact]
    public void TryGet_IsCaseInsensitiveOnPath()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/Admin/Users", NoopHandler);

        Assert.True(registry.TryGet("/admin/users", out var route));
        Assert.NotNull(route);
    }

    [Fact]
    public void Register_SamePath_MergesMethodsAndKeepsFirstKind()
    {
        // Registering a second handler on the same path MERGES (per-method
        // dispatch) instead of overwriting — the first registration's Kind is
        // kept and the method sets combine. (The old overwrite behaviour
        // surfaced as confusing 405s; this test previously asserted it.)
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/home", NoopHandler);
        registry.RegisterApi("/home", NoopHandler, "POST");

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet("/home", out var route));
        Assert.Equal(WebRouteKind.Page, route!.Kind);
        Assert.Contains("GET", route.Methods);
        Assert.Contains("POST", route.Methods);
    }

    [Fact]
    public void Register_PropagatesOptionsOntoDefinition()
    {
        var registry = new WebRouteRegistry();
        registry.Register(
            "/admin",
            WebRouteKind.Page,
            NoopHandler,
            Contributor,
            new WebRouteOptions
            {
                Name = "AdminHome",
                Description = "Admin landing page",
                Tags = ["admin", "protected"],
                RequiredAccessGroups = ["admin", "staff"],
                RequiredPermission = "admin.view",
                AllowAnonymous = false
            });

        Assert.True(registry.TryGet("/admin", out var route));
        Assert.Equal("AdminHome", route!.Name);
        Assert.Equal("Admin landing page", route.Description);
        Assert.Equal(["admin", "protected"], route.Tags);
        Assert.Equal(["admin", "staff"], route.RequiredAccessGroups);
        Assert.Equal("admin.view", route.RequiredPermission);
        Assert.False(route.AllowAnonymous);
    }

    [Fact]
    public void Register_BlankRequiredPermission_BecomesNull()
    {
        var registry = new WebRouteRegistry();
        registry.Register(
            "/x",
            WebRouteKind.Page,
            NoopHandler,
            Contributor,
            new WebRouteOptions { RequiredPermission = "   " });

        Assert.True(registry.TryGet("/x", out var route));
        Assert.Null(route!.RequiredPermission);
    }

    [Fact]
    public void WildcardRoute_MatchesByPrefix_AndPrefersLongerWildcard()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/assets/*", NoopHandler);
        registry.RegisterPage("/assets/vendor/*", NoopHandler);

        Assert.True(registry.TryGet("/assets/vendor/bootstrap.css", out var route));
        Assert.Equal("/assets/vendor/*", route!.Path);

        Assert.True(registry.TryGet("/assets/logo.png", out var fallbackMatch));
        Assert.Equal("/assets/*", fallbackMatch!.Path);
    }

    [Fact]
    public void WildcardRoute_NoMatch_ReturnsFalse()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/assets/*", NoopHandler);

        Assert.False(registry.TryGet("/other/thing", out var route));
        Assert.Null(route);
    }

    [Fact]
    public void RegisterFallback_IsExposedSeparately_AndCountsTowardCount()
    {
        var registry = new WebRouteRegistry();
        Assert.Equal(0, registry.Count);
        Assert.Null(registry.Fallback);

        registry.RegisterFallback(NoopHandler);

        Assert.Equal(1, registry.Count);
        Assert.NotNull(registry.Fallback);
        Assert.Equal("*", registry.Fallback!.Path);
        Assert.Equal(WebRouteKind.Fallback, registry.Fallback.Kind);

        // Fallback is NOT reachable through TryGet — it's a separate slot.
        Assert.False(registry.TryGet("/anything", out _));
    }

    [Fact]
    public void GetAllRoutes_ReturnsRoutesSortedByPath_ExcludingFallback()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/zebra", NoopHandler);
        registry.RegisterPage("/apple", NoopHandler);
        registry.RegisterPage("/mango", NoopHandler);
        registry.RegisterFallback(NoopHandler);

        var all = registry.GetAllRoutes();
        Assert.Equal(3, all.Count);
        Assert.Equal(["/apple", "/mango", "/zebra"], all.Select(r => r.Path).ToArray());
    }

    [Fact]
    public void GetRouteDescriptors_IncludesFallback()
    {
        var registry = new WebRouteRegistry();
        registry.RegisterPage("/home", NoopHandler);
        registry.RegisterFallback(NoopHandler);

        var descriptors = registry.GetRouteDescriptors();
        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, d => d.Path == "*" && d.Kind == "Fallback");
    }

    [Fact]
    public void GetContributors_GroupsRoutesBySource()
    {
        var registry = new WebRouteRegistry();
        var plugin = new WebContributorDescriptor
        {
            Id = "test.plugin",
            Name = "Plugin",
            Kind = "Plugin",
            Description = "A plugin"
        };

        registry.Register("/a", WebRouteKind.Page, NoopHandler, Contributor, new WebRouteOptions());
        registry.Register("/b", WebRouteKind.Page, NoopHandler, Contributor, new WebRouteOptions());
        registry.Register("/p/x", WebRouteKind.Page, NoopHandler, plugin, new WebRouteOptions());

        var contributors = registry.GetContributors();
        Assert.Equal(2, contributors.Count);

        var app = contributors.Single(c => c.Id == "test.app");
        Assert.Equal(2, app.RouteCount);

        var pluginSummary = contributors.Single(c => c.Id == "test.plugin");
        Assert.Equal(1, pluginSummary.RouteCount);
    }
}
