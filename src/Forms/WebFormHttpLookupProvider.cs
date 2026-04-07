using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Forms;

public sealed class WebFormHttpLookupOptions
{
    public required string UrlTemplate { get; init; }
    public string RootProperty { get; init; } = string.Empty;
    public string ValueProperty { get; init; } = "value";
    public string LabelProperty { get; init; } = "label";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WebFormHttpLookupProvider : IWebFormSearchProvider
{
    private static readonly HttpClient Client = new();
    private readonly WebFormHttpLookupOptions _options;
    private readonly ConcurrentDictionary<string, IReadOnlyList<WebFormSelectOption>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WebFormHttpLookupProvider(WebFormHttpLookupOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<WebFormSelectOption>> GetOptionsAsync(WebFormOptionsProviderContext context, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(context.Request, context.Values, string.Empty);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var options = await FetchOptionsAsync(BuildUrl(context.Request, context.Values, string.Empty), cancellationToken).ConfigureAwait(false);
        _cache[cacheKey] = options;
        return options;
    }

    public Task<IReadOnlyList<WebFormSelectOption>> SearchAsync(WebFormSearchProviderContext context, CancellationToken cancellationToken = default) =>
        FetchOptionsAsync(BuildUrl(context.Request, context.Values, context.SearchTerm), cancellationToken);

    private async Task<IReadOnlyList<WebFormSelectOption>> FetchOptionsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in _options.Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = ResolveRoot(document.RootElement);
        if (root.ValueKind != JsonValueKind.Array)
            return [];

        var options = new List<WebFormSelectOption>();
        foreach (var item in root.EnumerateArray())
        {
            var value = ResolveString(item, _options.ValueProperty);
            var label = ResolveString(item, _options.LabelProperty);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            options.Add(new WebFormSelectOption
            {
                Value = value,
                Label = string.IsNullOrWhiteSpace(label) ? value : label
            });
        }

        return options;
    }

    private JsonElement ResolveRoot(JsonElement root)
    {
        if (string.IsNullOrWhiteSpace(_options.RootProperty))
            return root;

        return TryGetPath(root, _options.RootProperty, out var resolved)
            ? resolved
            : root;
    }

    private string BuildUrl(WebRequestContext request, IReadOnlyDictionary<string, string?> values, string? term)
    {
        var url = _options.UrlTemplate;
        url = url.Replace("{term}", Uri.EscapeDataString(term ?? string.Empty), StringComparison.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            var token = "{" + pair.Key + "}";
            url = url.Replace(token, Uri.EscapeDataString(pair.Value ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        var origin = $"{request.HttpContext.Request.Scheme}://{request.HttpContext.Request.Host}";
        return new Uri(new Uri(origin), url).ToString();
    }

    private static bool TryGetPath(JsonElement element, string path, out JsonElement value)
    {
        value = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }

        return true;
    }

    private static string ResolveString(JsonElement element, string propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
            return element.ToString();

        if (!TryGetPath(element, propertyPath, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }

    private static string BuildCacheKey(WebRequestContext request, IReadOnlyDictionary<string, string?> values, string? term)
    {
        var parts = values
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}");
        return $"{request.Path}|{term}|{string.Join("&", parts)}";
    }
}
