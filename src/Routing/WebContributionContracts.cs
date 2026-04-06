using CL.WebLogic.Runtime;

namespace CL.WebLogic.Routing;

public interface IWebRouteContributor
{
    Task RegisterRoutesAsync(WebRegistrationContext context);
}

public sealed class WebContributorDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Kind { get; init; } = "Contributor";
    public string Description { get; init; } = string.Empty;
}

public sealed class WebRouteOptions
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
    public string[] RequiredAccessGroups { get; init; } = [];
    public bool AllowAnonymous { get; init; } = true;
}

public sealed class WebRegistrationContext
{
    private readonly WebLogicRegistrationApi _api;

    internal WebRegistrationContext(WebLogicRegistrationApi api, WebContributorDescriptor contributor)
    {
        _api = api;
        Contributor = contributor;
    }

    public WebContributorDescriptor Contributor { get; }

    public void RegisterPage(string path, WebRouteHandler handler, params string[] methods) =>
        RegisterPage(path, new WebRouteOptions(), handler, methods);

    public void RegisterPage(string path, WebRouteOptions options, WebRouteHandler handler, params string[] methods) =>
        _api.Register(path, WebRouteKind.Page, handler, Contributor, options, methods);

    public void RegisterApi(string path, WebRouteHandler handler, params string[] methods) =>
        RegisterApi(path, new WebRouteOptions(), handler, methods);

    public void RegisterApi(string path, WebRouteOptions options, WebRouteHandler handler, params string[] methods) =>
        _api.Register(path, WebRouteKind.Api, handler, Contributor, options, methods);

    public void RegisterFallback(WebRouteHandler handler, params string[] methods) =>
        RegisterFallback(new WebRouteOptions(), handler, methods);

    public void RegisterFallback(WebRouteOptions options, WebRouteHandler handler, params string[] methods) =>
        _api.RegisterFallback(handler, Contributor, options, methods);
}

public sealed class WebRouteDescriptor
{
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public required string[] Methods { get; init; }
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required string SourceKind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string[] Tags { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
    public required bool AllowAnonymous { get; init; }
}

public sealed class WebContributorSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }
    public required int RouteCount { get; init; }
}

public sealed class WebLogicRegistrationApi
{
    private readonly WebRouteRegistry _routes;

    internal WebLogicRegistrationApi(WebRouteRegistry routes)
    {
        _routes = routes;
    }

    public WebRegistrationContext CreateContext(WebContributorDescriptor contributor) =>
        new(this, contributor);

    internal void Register(
        string path,
        WebRouteKind kind,
        WebRouteHandler handler,
        WebContributorDescriptor contributor,
        WebRouteOptions options,
        params string[] methods)
    {
        _routes.Register(path, kind, handler, contributor, options, methods);
    }

    internal void RegisterFallback(
        WebRouteHandler handler,
        WebContributorDescriptor contributor,
        WebRouteOptions options,
        params string[] methods)
    {
        _routes.RegisterFallback(handler, contributor, options, methods);
    }
}
