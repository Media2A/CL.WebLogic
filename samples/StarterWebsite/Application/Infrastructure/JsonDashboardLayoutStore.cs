using System.Text.Json;
using CL.WebLogic.Theming;

namespace StarterWebsite.Application.Infrastructure;

public sealed class JsonDashboardLayoutStore : IWebDashboardLayoutStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonDashboardLayoutStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<WebDashboardLayoutRecord?> GetAsync(string ownerKey, string dashboardKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(dashboardKey))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            var key = BuildKey(ownerKey, dashboardKey);
            return state.TryGetValue(key, out var layout)
                ? layout
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WebDashboardLayoutRecord> SaveAsync(string ownerKey, string dashboardKey, IReadOnlyList<WebDashboardWidgetPlacement> widgets, CancellationToken cancellationToken = default)
    {
        var record = new WebDashboardLayoutRecord
        {
            OwnerKey = ownerKey,
            DashboardKey = dashboardKey,
            Widgets = NormalizeWidgets(widgets),
            UpdatedUtc = DateTime.UtcNow
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state[BuildKey(ownerKey, dashboardKey)] = record;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, WebDashboardLayoutRecord>> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, WebDashboardLayoutRecord>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var state = await JsonSerializer.DeserializeAsync<Dictionary<string, WebDashboardLayoutRecord>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return state ?? new Dictionary<string, WebDashboardLayoutRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteStateAsync(Dictionary<string, WebDashboardLayoutRecord> state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildKey(string ownerKey, string dashboardKey) =>
        $"{ownerKey.Trim().ToLowerInvariant()}::{dashboardKey.Trim().ToLowerInvariant()}";

    private static List<WebDashboardWidgetPlacement> NormalizeWidgets(IReadOnlyList<WebDashboardWidgetPlacement> widgets)
    {
        var zones = widgets
            .Where(static item => !string.IsNullOrWhiteSpace(item.WidgetName))
            .GroupBy(static item => NormalizeZone(item.Zone), StringComparer.OrdinalIgnoreCase);

        var normalized = new List<WebDashboardWidgetPlacement>();
        foreach (var zone in zones)
        {
            var order = 10;
            foreach (var widget in zone.OrderBy(static item => item.Order))
            {
                normalized.Add(new WebDashboardWidgetPlacement
                {
                    InstanceId = string.IsNullOrWhiteSpace(widget.InstanceId) ? Guid.NewGuid().ToString("N") : widget.InstanceId.Trim(),
                    WidgetName = widget.WidgetName.Trim(),
                    Zone = zone.Key,
                    Order = order,
                    Settings = new Dictionary<string, string>(widget.Settings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
                });

                order += 10;
            }
        }

        return normalized;
    }

    private static string NormalizeZone(string? zone) =>
        string.IsNullOrWhiteSpace(zone) ? "main" : zone.Trim().ToLowerInvariant();
}
