using CL.WebLogic.Runtime;
using CL.WebLogic.Theming;

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
    public IWebMiddleware[]? Middleware { get; init; }
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

    public void RegisterPageScript<TScript>(string path, params string[] methods)
        where TScript : class, IWebPageScript =>
        RegisterPageScript<TScript>(path, new WebRouteOptions(), methods);

    public void RegisterPageScript<TScript>(string path, WebRouteOptions options, params string[] methods)
        where TScript : class, IWebPageScript =>
        RegisterPage(path, options, request => WebPageScriptExecutor.ExecuteAsync<TScript>(request), methods);

    public void RegisterApi(string path, WebRouteHandler handler, params string[] methods) =>
        RegisterApi(path, new WebRouteOptions(), handler, methods);

    public void RegisterApi(string path, WebRouteOptions options, WebRouteHandler handler, params string[] methods) =>
        _api.Register(path, WebRouteKind.Api, handler, Contributor, options, methods);

    public void RegisterFallback(WebRouteHandler handler, params string[] methods) =>
        RegisterFallback(new WebRouteOptions(), handler, methods);

    public void RegisterFallback(WebRouteOptions options, WebRouteHandler handler, params string[] methods) =>
        _api.RegisterFallback(handler, Contributor, options, methods);

    public void RegisterWidget(string name, WebWidgetHandler handler) =>
        RegisterWidget(name, new WebWidgetOptions(), handler);

    public void RegisterWidget(string name, WebWidgetOptions options, WebWidgetHandler handler) =>
        _api.RegisterWidget(name, handler, Contributor, options);

    public void UseMiddleware(IWebMiddleware middleware) =>
        _api.UseMiddleware(middleware);

    public void UseMiddleware(Func<WebRequestContext, WebMiddlewareNext, Task<WebResult>> handler) =>
        _api.UseMiddleware(new WebDelegateMiddleware(handler));

    public void RegisterWidgetArea(string areaName, string widgetName) =>
        RegisterWidgetArea(areaName, widgetName, new WebWidgetAreaOptions());

    public void RegisterWidgetArea(string areaName, string widgetName, WebWidgetAreaOptions options) =>
        _api.RegisterWidgetArea(areaName, widgetName, Contributor, options);
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
    private readonly WebWidgetRegistry _widgets;
    private WebLogicRuntime? _runtime;

    internal WebLogicRegistrationApi(WebRouteRegistry routes, WebWidgetRegistry widgets)
    {
        _routes = routes;
        _widgets = widgets;
    }

    internal void SetRuntime(WebLogicRuntime runtime)
    {
        _runtime = runtime;
    }

    public WebRegistrationContext CreateContext(WebContributorDescriptor contributor) =>
        new(this, contributor);

    internal void UseMiddleware(IWebMiddleware middleware) =>
        _runtime?.UseMiddleware(middleware);

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

    internal void RegisterWidget(
        string name,
        WebWidgetHandler handler,
        WebContributorDescriptor contributor,
        WebWidgetOptions options)
    {
        _widgets.Register(name, handler, contributor, options);
    }

    internal void RegisterWidgetArea(
        string areaName,
        string widgetName,
        WebContributorDescriptor contributor,
        WebWidgetAreaOptions options)
    {
        _widgets.RegisterAreaWidget(areaName, widgetName, contributor, options);
    }
}
