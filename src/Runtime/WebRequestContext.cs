using System.Text;
using System.Text.Json;
using CL.WebLogic.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CL.WebLogic.Runtime;

public sealed class WebRequestContext
{
    private Dictionary<string, string>? _form;
    private IReadOnlyList<WebUploadedFile>? _files;
    private string? _bodyText;

    public required HttpContext HttpContext { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string ClientIp { get; init; }
    public required string UserAgent { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required IReadOnlyDictionary<string, string> Query { get; init; }
    public required IReadOnlyDictionary<string, string> Cookies { get; init; }
    public required IReadOnlyDictionary<string, string> Session { get; init; }
    public required WebRequestIdentity Identity { get; init; }

    public WebRouteDefinition? Route { get; set; }

    public string QueryString => HttpContext.Request.QueryString.Value ?? string.Empty;
    public string Host => HttpContext.Request.Host.Value ?? string.Empty;
    public string Scheme => HttpContext.Request.Scheme;
    public string ContentType => HttpContext.Request.ContentType ?? string.Empty;
    public bool IsHttps => HttpContext.Request.IsHttps;
    public bool IsAuthenticated => Identity.IsAuthenticated;
    public string UserId => Identity.UserId;
    public IReadOnlyCollection<string> AccessGroups => Identity.AccessGroups;

    public static WebRequestContext? Current => WebRequestContextAccessor.Current;

    public string? GetHeader(string key, string? defaultValue = null) =>
        Headers.TryGetValue(key, out var value) ? value : defaultValue;

    public string? GetQuery(string key, string? defaultValue = null) =>
        Query.TryGetValue(key, out var value) ? value : defaultValue;

    public string? GetCookie(string key, string? defaultValue = null) =>
        Cookies.TryGetValue(key, out var value) ? value : defaultValue;

    public string? GetSessionValue(string key, string? defaultValue = null) =>
        Session.TryGetValue(key, out var value) ? value : defaultValue;

    public bool HasAccessGroup(string accessGroup) =>
        Identity.HasAccessGroup(accessGroup);

    public bool HasAnyAccessGroup(IEnumerable<string> accessGroups) =>
        Identity.HasAnyAccessGroup(accessGroups);

    public void SetSessionValue(string key, string? value)
    {
        if (value is null)
        {
            HttpContext.Session.Remove(key);
            return;
        }

        HttpContext.Session.SetString(key, value);
    }

    public T? GetService<T>() where T : class =>
        HttpContext.RequestServices.GetService<T>();

    public object? GetService(Type serviceType) =>
        HttpContext.RequestServices.GetService(serviceType);

    public T GetRequiredService<T>() where T : notnull =>
        HttpContext.RequestServices.GetRequiredService<T>();

    public async Task<Dictionary<string, string>> ReadFormAsync()
    {
        if (_form is not null)
            return new Dictionary<string, string>(_form, StringComparer.OrdinalIgnoreCase);

        if (!HttpContext.Request.HasFormContentType)
        {
            _form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _files = [];
            return new Dictionary<string, string>(_form, StringComparer.OrdinalIgnoreCase);
        }

        HttpContext.Request.EnableBuffering();
        var form = await HttpContext.Request.ReadFormAsync().ConfigureAwait(false);
        HttpContext.Request.Body.Position = 0;

        _form = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        _files = form.Files
            .Select(static file => new WebUploadedFile(
                file.Name,
                file.FileName,
                file.ContentType,
                file.Length))
            .ToArray();

        return new Dictionary<string, string>(_form, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> GetFormValueAsync(string key, string? defaultValue = null)
    {
        var form = await ReadFormAsync().ConfigureAwait(false);
        return form.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public async Task<IReadOnlyList<WebUploadedFile>> ReadFilesAsync()
    {
        if (_files is not null)
            return _files;

        await ReadFormAsync().ConfigureAwait(false);
        return _files ?? [];
    }

    public async Task<string> ReadBodyAsStringAsync()
    {
        if (_bodyText is not null)
            return _bodyText;

        HttpContext.Request.EnableBuffering();
        HttpContext.Request.Body.Position = 0;

        using var reader = new StreamReader(
            HttpContext.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        _bodyText = await reader.ReadToEndAsync().ConfigureAwait(false);
        HttpContext.Request.Body.Position = 0;
        return _bodyText;
    }

    public async Task<T?> ReadJsonAsync<T>()
    {
        HttpContext.Request.EnableBuffering();
        HttpContext.Request.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(HttpContext.Request.Body).ConfigureAwait(false);
        HttpContext.Request.Body.Position = 0;
        return result;
    }

    public object? GetItem(object key) =>
        HttpContext.Items.TryGetValue(key, out var value) ? value : null;

    public void SetItem(object key, object? value) =>
        HttpContext.Items[key] = value;
}

public sealed record WebUploadedFile(
    string FieldName,
    string FileName,
    string ContentType,
    long Length);

public sealed class WebRequestIdentity
{
    private readonly HashSet<string> _accessGroups;

    public WebRequestIdentity(string? userId, IEnumerable<string>? accessGroups)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId;
        _accessGroups = (accessGroups ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string UserId { get; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId) || _accessGroups.Count > 0;
    public IReadOnlyCollection<string> AccessGroups => _accessGroups;

    public bool HasAccessGroup(string accessGroup) =>
        _accessGroups.Contains(accessGroup);

    public bool HasAnyAccessGroup(IEnumerable<string> accessGroups) =>
        accessGroups.Any(HasAccessGroup);
}

public static class WebLogicRequest
{
    public static WebRequestContext GetPageContextFromRequest() =>
        WebRequestContextAccessor.Current
        ?? throw new InvalidOperationException("No active WebLogic request context is available.");
}

internal static class WebRequestContextAccessor
{
    private static readonly AsyncLocal<WebRequestContext?> CurrentHolder = new();

    public static WebRequestContext? Current
    {
        get => CurrentHolder.Value;
        set => CurrentHolder.Value = value;
    }
}
