using System.Collections.Concurrent;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Forms;

public interface IWebFormOptionsProvider
{
    Task<IReadOnlyList<WebFormSelectOption>> GetOptionsAsync(WebFormOptionsProviderContext context, CancellationToken cancellationToken = default);
}

public interface IWebFormSearchProvider : IWebFormOptionsProvider
{
    Task<IReadOnlyList<WebFormSelectOption>> SearchAsync(WebFormSearchProviderContext context, CancellationToken cancellationToken = default);
}

public sealed class WebFormOptionsProviderContext
{
    public required string ProviderId { get; init; }
    public required WebRequestContext Request { get; init; }
    public required WebFormDefinition Form { get; init; }
    public required WebFormFieldDefinition Field { get; init; }
    public required IReadOnlyDictionary<string, string?> Values { get; init; }

    public string? GetValue(string fieldName) =>
        Values.TryGetValue(fieldName, out var value) ? value : null;
}

public sealed class WebFormSearchProviderContext
{
    public required string ProviderId { get; init; }
    public required WebRequestContext Request { get; init; }
    public required WebFormDefinition Form { get; init; }
    public required WebFormFieldDefinition Field { get; init; }
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
    public string SearchTerm { get; init; } = string.Empty;
    public string SelectedValue { get; init; } = string.Empty;

    public string? GetValue(string fieldName) =>
        Values.TryGetValue(fieldName, out var value) ? value : null;
}

public sealed class WebFormOptionsRegistry
{
    private readonly ConcurrentDictionary<string, IWebFormOptionsProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string id, IWebFormOptionsProvider provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(provider);
        _providers[id] = provider;
    }

    public void RegisterHttpLookup(string id, WebFormHttpLookupOptions options) =>
        Register(id, new WebFormHttpLookupProvider(options));

    public bool TryGet(string id, out IWebFormOptionsProvider provider) =>
        _providers.TryGetValue(id, out provider!);

    public IReadOnlyList<string> GetProviderIds() =>
        _providers.Keys.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<IReadOnlyList<WebFormSelectOption>> ResolveAsync(
        string providerId,
        WebRequestContext request,
        WebFormDefinition form,
        WebFormFieldDefinition field,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
            return [];

        return await provider.GetOptionsAsync(new WebFormOptionsProviderContext
        {
            ProviderId = providerId,
            Request = request,
            Form = form,
            Field = field,
            Values = values
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebFormSelectOption>> ResolveSearchAsync(
        string providerId,
        WebRequestContext request,
        WebFormDefinition form,
        WebFormFieldDefinition field,
        IReadOnlyDictionary<string, string?> values,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
            return [];

        if (provider is IWebFormSearchProvider searchProvider)
        {
            return await searchProvider.SearchAsync(new WebFormSearchProviderContext
            {
                ProviderId = providerId,
                Request = request,
                Form = form,
                Field = field,
                Values = values,
                SearchTerm = searchTerm ?? string.Empty
            }, cancellationToken).ConfigureAwait(false);
        }

        var options = await ResolveAsync(providerId, request, form, field, values, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(searchTerm))
            return options;

        return options
            .Where(option =>
                option.Label.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                option.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async Task<bool> IsValidSearchSelectionAsync(
        string providerId,
        WebRequestContext request,
        WebFormDefinition form,
        WebFormFieldDefinition field,
        IReadOnlyDictionary<string, string?> values,
        string selectedValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedValue))
            return true;

        if (!_providers.TryGetValue(providerId, out var provider))
            return false;

        var options = await provider.GetOptionsAsync(new WebFormOptionsProviderContext
        {
            ProviderId = providerId,
            Request = request,
            Form = form,
            Field = field,
            Values = values
        }, cancellationToken).ConfigureAwait(false);

        if (options.Any(option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (provider is not IWebFormSearchProvider searchProvider)
            return false;

        var searchMatches = await searchProvider.SearchAsync(new WebFormSearchProviderContext
        {
            ProviderId = providerId,
            Request = request,
            Form = form,
            Field = field,
            Values = values,
            SearchTerm = selectedValue,
            SelectedValue = selectedValue
        }, cancellationToken).ConfigureAwait(false);

        return searchMatches.Any(option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase));
    }
}
