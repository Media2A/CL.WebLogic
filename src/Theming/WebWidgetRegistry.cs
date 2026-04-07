using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Theming;

public delegate Task<WebWidgetResult> WebWidgetHandler(WebWidgetContext context);
public delegate Task<object?> WebWidgetDataHandler(WebWidgetContext context);
public delegate Task<WebWidgetActionResult> WebWidgetActionHandler(WebWidgetActionContext context);

public sealed class WebWidgetOptions
{
    public string Description { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
    public bool AllowAnonymous { get; init; } = true;
    public string[] RequiredAccessGroups { get; init; } = [];
    public IReadOnlyDictionary<string, string> SampleParameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public WebWidgetDataHandler? DataHandler { get; init; }
    public WebWidgetActionHandler? ActionHandler { get; init; }
    public IReadOnlyDictionary<string, string> Actions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WebWidgetAreaOptions
{
    public string Description { get; init; } = string.Empty;
    public int Order { get; init; } = 0;
    public string? InstanceId { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string[] IncludeRoutePatterns { get; init; } = [];
    public string[] ExcludeRoutePatterns { get; init; } = [];
    public bool AllowAnonymous { get; init; } = true;
    public string[] RequiredAccessGroups { get; init; } = [];
}

public sealed class WebWidgetDefinition
{
    public required string Name { get; init; }
    public required WebContributorDescriptor Contributor { get; init; }
    public required WebWidgetHandler Handler { get; init; }
    public required string Description { get; init; }
    public required string[] Tags { get; init; }
    public required bool AllowAnonymous { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
    public required IReadOnlyDictionary<string, string> SampleParameters { get; init; }
    public WebWidgetDataHandler? DataHandler { get; init; }
    public WebWidgetActionHandler? ActionHandler { get; init; }
    public required IReadOnlyDictionary<string, string> Actions { get; init; }
}

public sealed class WebWidgetDescriptor
{
    public required string Name { get; init; }
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required string SourceKind { get; init; }
    public required string Description { get; init; }
    public required string[] Tags { get; init; }
    public required bool AllowAnonymous { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
    public required IReadOnlyDictionary<string, string> SampleParameters { get; init; }
    public required bool HasData { get; init; }
    public required bool HasActions { get; init; }
    public required IReadOnlyDictionary<string, string> Actions { get; init; }
}

public sealed class WebWidgetAreaDefinition
{
    public required string AreaName { get; init; }
    public required string WidgetName { get; init; }
    public string? InstanceId { get; init; }
    public required WebContributorDescriptor Contributor { get; init; }
    public required string Description { get; init; }
    public required int Order { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required string[] IncludeRoutePatterns { get; init; }
    public required string[] ExcludeRoutePatterns { get; init; }
    public required bool AllowAnonymous { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
}

public sealed class WebWidgetAreaDescriptor
{
    public required string AreaName { get; init; }
    public required string WidgetName { get; init; }
    public string? InstanceId { get; init; }
    public required string SourceId { get; init; }
    public required string SourceName { get; init; }
    public required string SourceKind { get; init; }
    public required string Description { get; init; }
    public required int Order { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required string[] IncludeRoutePatterns { get; init; }
    public required string[] ExcludeRoutePatterns { get; init; }
    public required bool AllowAnonymous { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
}

public sealed class WebWidgetResult
{
    public string? Html { get; init; }
    public string? TemplatePath { get; init; }
    public IReadOnlyDictionary<string, object?>? Model { get; init; }

    public static WebWidgetResult HtmlContent(string html) => new()
    {
        Html = html
    };

    public static WebWidgetResult Template(string templatePath, IReadOnlyDictionary<string, object?>? model = null) => new()
    {
        TemplatePath = templatePath,
        Model = model
    };
}

public sealed class WebWidgetContext
{
    public required string Name { get; init; }
    public string? InstanceId { get; init; }
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }
    public required IReadOnlyDictionary<string, object?> Model { get; init; }
    public required WebRequestContext Request { get; init; }
    public required WebContributorDescriptor Contributor { get; init; }
    public IWebWidgetSettingsStore? SettingsStore { get; init; }

    public string? GetParameter(string key, string? defaultValue = null) =>
        Parameters.TryGetValue(key, out var value) ? value : defaultValue;

    public T? GetService<T>() where T : class =>
        Request.GetService<T>();

    public T GetRequiredService<T>() where T : notnull =>
        Request.GetRequiredService<T>();

    public Task<WebWidgetSettingsRecord?> GetInstanceSettingsAsync() =>
        string.IsNullOrWhiteSpace(InstanceId) || SettingsStore is null
            ? Task.FromResult<WebWidgetSettingsRecord?>(null)
            : SettingsStore.GetAsync(InstanceId);
}

public sealed class WebWidgetActionContext
{
    public required string ActionName { get; init; }
    public required WebWidgetContext Widget { get; init; }
    public required IReadOnlyDictionary<string, string> Arguments { get; init; }

    public string? GetArgument(string key, string? defaultValue = null) =>
        Arguments.TryGetValue(key, out var value) ? value : defaultValue;

    public async Task SaveSettingsAsync(IReadOnlyDictionary<string, string> settings)
    {
        if (Widget.SettingsStore is null || string.IsNullOrWhiteSpace(Widget.InstanceId))
            return;

        await Widget.SettingsStore.UpsertAsync(Widget.InstanceId, Widget.Name, settings).ConfigureAwait(false);
    }

    public async Task SaveSettingsAsync(string instanceId, string widgetName, IReadOnlyDictionary<string, string> settings)
    {
        if (Widget.SettingsStore is null || string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(widgetName))
            return;

        await Widget.SettingsStore.UpsertAsync(instanceId, widgetName, settings).ConfigureAwait(false);
    }
}

public enum WebWidgetClientMessageLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed class WebWidgetClientMessage
{
    public WebWidgetClientMessageLevel Level { get; init; } = WebWidgetClientMessageLevel.Info;
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public sealed class WebWidgetClientEvent
{
    public string Channel { get; init; } = string.Empty;
    public string? Name { get; init; }
    public object? Payload { get; init; }
}

public sealed class WebWidgetRefreshPlan
{
    public IReadOnlyList<string> WidgetInstances { get; init; } = [];
    public IReadOnlyList<string> WidgetAreas { get; init; } = [];
    public bool RefreshDashboard { get; init; }
    public bool RefreshWidgetCatalog { get; init; }
    public bool RefreshWidgetAreas { get; init; }
}

public sealed class WebWidgetActionResult
{
    public int StatusCode { get; init; } = 200;
    public object? Payload { get; init; }
    public WebWidgetRefreshPlan? Refresh { get; init; }
    public IReadOnlyList<WebWidgetClientMessage> Messages { get; init; } = [];
    public IReadOnlyList<WebWidgetClientEvent> Events { get; init; } = [];

    public static WebWidgetActionResult Ok(
        object? payload = null,
        WebWidgetRefreshPlan? refresh = null,
        IReadOnlyList<WebWidgetClientMessage>? messages = null,
        IReadOnlyList<WebWidgetClientEvent>? events = null) => new()
    {
        StatusCode = 200,
        Payload = payload,
        Refresh = refresh,
        Messages = messages ?? [],
        Events = events ?? []
    };
}

public sealed class WebWidgetRegistry
{
    private readonly Dictionary<string, WebWidgetDefinition> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WebWidgetAreaDefinition>> _areas = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _widgets.Count;

    public void Register(
        string name,
        WebWidgetHandler handler,
        WebContributorDescriptor contributor,
        WebWidgetOptions? options = null)
    {
        var normalized = NormalizeName(name);
        var resolvedOptions = options ?? new WebWidgetOptions();

        _widgets[normalized] = new WebWidgetDefinition
        {
            Name = normalized,
            Contributor = contributor,
            Handler = handler,
            Description = resolvedOptions.Description,
            Tags = resolvedOptions.Tags,
            AllowAnonymous = resolvedOptions.AllowAnonymous,
            RequiredAccessGroups = resolvedOptions.RequiredAccessGroups,
            SampleParameters = resolvedOptions.SampleParameters,
            DataHandler = resolvedOptions.DataHandler,
            ActionHandler = resolvedOptions.ActionHandler,
            Actions = resolvedOptions.Actions
        };
    }

    public bool TryGet(string name, out WebWidgetDefinition? widget) =>
        _widgets.TryGetValue(NormalizeName(name), out widget);

    public void RegisterAreaWidget(
        string areaName,
        string widgetName,
        WebContributorDescriptor contributor,
        WebWidgetAreaOptions? options = null)
    {
        var normalizedArea = NormalizeName(areaName);
        var normalizedWidget = NormalizeName(widgetName);
        var resolvedOptions = options ?? new WebWidgetAreaOptions();

        if (!_areas.TryGetValue(normalizedArea, out var items))
        {
            items = [];
            _areas[normalizedArea] = items;
        }

        items.RemoveAll(item =>
            string.Equals(item.WidgetName, normalizedWidget, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Contributor.Id, contributor.Id, StringComparison.OrdinalIgnoreCase));

        items.Add(new WebWidgetAreaDefinition
        {
            AreaName = normalizedArea,
            WidgetName = normalizedWidget,
            InstanceId = resolvedOptions.InstanceId,
            Contributor = contributor,
            Description = resolvedOptions.Description,
            Order = resolvedOptions.Order,
            Parameters = resolvedOptions.Parameters,
            IncludeRoutePatterns = resolvedOptions.IncludeRoutePatterns,
            ExcludeRoutePatterns = resolvedOptions.ExcludeRoutePatterns,
            AllowAnonymous = resolvedOptions.AllowAnonymous,
            RequiredAccessGroups = resolvedOptions.RequiredAccessGroups
        });
    }

    public IReadOnlyList<WebWidgetAreaDefinition> GetAreaWidgets(string areaName)
    {
        if (!_areas.TryGetValue(NormalizeName(areaName), out var items))
            return [];

        return items
            .OrderBy(static item => item.Order)
            .ThenBy(static item => item.Contributor.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Contributor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.WidgetName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<WebWidgetDescriptor> GetDescriptors() =>
        _widgets.Values
            .Select(static widget => new WebWidgetDescriptor
            {
                Name = widget.Name,
                SourceId = widget.Contributor.Id,
                SourceName = widget.Contributor.Name,
                SourceKind = widget.Contributor.Kind,
                Description = widget.Description,
                Tags = widget.Tags,
                AllowAnonymous = widget.AllowAnonymous,
                RequiredAccessGroups = widget.RequiredAccessGroups,
                SampleParameters = widget.SampleParameters,
                HasData = widget.DataHandler is not null,
                HasActions = widget.ActionHandler is not null && widget.Actions.Count > 0,
                Actions = widget.Actions
            })
            .OrderBy(static widget => widget.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<WebWidgetAreaDescriptor> GetAreaDescriptors() =>
        _areas
            .SelectMany(static pair => pair.Value)
            .Select(static area => new WebWidgetAreaDescriptor
            {
                AreaName = area.AreaName,
                WidgetName = area.WidgetName,
                InstanceId = area.InstanceId,
                SourceId = area.Contributor.Id,
                SourceName = area.Contributor.Name,
                SourceKind = area.Contributor.Kind,
                Description = area.Description,
                Order = area.Order,
                Parameters = area.Parameters,
                IncludeRoutePatterns = area.IncludeRoutePatterns,
                ExcludeRoutePatterns = area.ExcludeRoutePatterns,
                AllowAnonymous = area.AllowAnonymous,
                RequiredAccessGroups = area.RequiredAccessGroups
            })
            .OrderBy(static area => area.AreaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static area => area.Order)
            .ThenBy(static area => area.SourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static area => area.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static area => area.WidgetName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string NormalizeName(string name) =>
        name.Trim();
}
