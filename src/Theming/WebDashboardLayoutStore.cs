using System.Text.Json;

namespace CL.WebLogic.Theming;

public sealed class WebDashboardWidgetPlacement
{
    public string InstanceId { get; init; } = string.Empty;
    public string WidgetName { get; init; } = string.Empty;
    public string Zone { get; init; } = "main";
    public int Order { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WebDashboardLayoutRecord
{
    public string OwnerKey { get; init; } = string.Empty;
    public string DashboardKey { get; init; } = string.Empty;
    public List<WebDashboardWidgetPlacement> Widgets { get; init; } = [];
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}

public interface IWebDashboardLayoutStore
{
    Task<WebDashboardLayoutRecord?> GetAsync(string ownerKey, string dashboardKey, CancellationToken cancellationToken = default);
    Task<WebDashboardLayoutRecord> SaveAsync(string ownerKey, string dashboardKey, IReadOnlyList<WebDashboardWidgetPlacement> widgets, CancellationToken cancellationToken = default);
}

internal static class WebDashboardLayoutSerialization
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static string Serialize(IReadOnlyList<WebDashboardWidgetPlacement> widgets) =>
        JsonSerializer.Serialize(widgets, SerializerOptions);

    public static List<WebDashboardWidgetPlacement> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<WebDashboardWidgetPlacement>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
