using CL.MySQL2;
using CL.MySQL2.Models;
using CL.WebLogic.Theming;
using CodeLogic;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic.MySql;

public sealed class WebMySqlDashboardLayoutStore : IWebDashboardLayoutStore
{
    private readonly string _connectionId;
    private MySQL2Library? _mysql;

    public WebMySqlDashboardLayoutStore(string connectionId)
    {
        _connectionId = connectionId;
    }

    public async Task InitializeAsync()
    {
        _mysql = Libraries.Get<MySQL2Library>();
        if (_mysql is null)
            return;

        await _mysql.SyncTableAsync<WebDashboardLayoutRow>(connectionId: _connectionId).ConfigureAwait(false);
    }

    public async Task<WebDashboardLayoutRecord?> GetAsync(string ownerKey, string dashboardKey, CancellationToken cancellationToken = default)
    {
        if (_mysql is null || string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(dashboardKey))
            return null;

        var repository = _mysql.GetRepository<WebDashboardLayoutRow>(_connectionId);
        var results = await repository.GetByColumnAsync("user_id", ownerKey, cancellationToken).ConfigureAwait(false);
        if (results.IsFailure || results.Value is null)
            return null;

        var row = results.Value.FirstOrDefault(item =>
            string.Equals(item.DashboardKey, dashboardKey, StringComparison.OrdinalIgnoreCase));

        if (row is null)
            return null;

        return new WebDashboardLayoutRecord
        {
            OwnerKey = row.OwnerKey,
            DashboardKey = row.DashboardKey,
            Widgets = NormalizeWidgets(WebDashboardLayoutSerialization.Deserialize(row.LayoutJson)),
            UpdatedUtc = row.UpdatedUtc
        };
    }

    public async Task<WebDashboardLayoutRecord> SaveAsync(
        string ownerKey,
        string dashboardKey,
        IReadOnlyList<WebDashboardWidgetPlacement> widgets,
        CancellationToken cancellationToken = default)
    {
        if (_mysql is null)
            throw new InvalidOperationException("MySQL dashboard layout store is not initialized.");

        var repository = _mysql.GetRepository<WebDashboardLayoutRow>(_connectionId);
        var normalizedWidgets = NormalizeWidgets(widgets);
        var existing = await GetAsync(ownerKey, dashboardKey, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            var insert = await repository.InsertAsync(new WebDashboardLayoutRow
            {
                OwnerKey = ownerKey,
                DashboardKey = dashboardKey,
                LayoutJson = WebDashboardLayoutSerialization.Serialize(normalizedWidgets),
                UpdatedUtc = now
            }, cancellationToken).ConfigureAwait(false);

            if (insert.IsFailure || insert.Value is null)
                throw new InvalidOperationException(insert.Error?.Message ?? "Could not save dashboard layout.");
        }
        else
        {
            var rows = await repository.GetByColumnAsync("user_id", ownerKey, cancellationToken).ConfigureAwait(false);
            var row = rows.IsSuccess && rows.Value is not null
                ? rows.Value.FirstOrDefault(item => string.Equals(item.DashboardKey, dashboardKey, StringComparison.OrdinalIgnoreCase))
                : null;

            if (row is null)
                throw new InvalidOperationException("Dashboard layout row disappeared during save.");

            row.LayoutJson = WebDashboardLayoutSerialization.Serialize(normalizedWidgets);
            row.UpdatedUtc = now;

            var update = await repository.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
            if (update.IsFailure)
                throw new InvalidOperationException(update.Error?.Message ?? "Could not update dashboard layout.");
        }

        return new WebDashboardLayoutRecord
        {
            OwnerKey = ownerKey,
            DashboardKey = dashboardKey,
            Widgets = normalizedWidgets,
            UpdatedUtc = now
        };
    }

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

[Table(Name = "weblogic_dashboard_layouts")]
public sealed class WebDashboardLayoutRow
{
    [Column(DataType = DataType.BigInt, Primary = true, AutoIncrement = true, NotNull = true)]
    public long Id { get; set; }

    [Column(Name = "user_id", DataType = DataType.VarChar, Size = 128, NotNull = true, Index = true)]
    public string OwnerKey { get; set; } = string.Empty;

    [Column(Name = "dashboard_key", DataType = DataType.VarChar, Size = 128, NotNull = true, Index = true)]
    public string DashboardKey { get; set; } = string.Empty;

    [Column(Name = "layout_json", DataType = DataType.LongText, NotNull = true)]
    public string LayoutJson { get; set; } = "[]";

    [Column(Name = "updated_utc", DataType = DataType.DateTime, NotNull = true)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
