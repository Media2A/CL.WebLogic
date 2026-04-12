using System.Text.Json;
using CL.WebLogic.Configuration;
using CodeLogic.Framework.Libraries;

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

public sealed class FileWebDashboardLayoutStore : IWebDashboardLayoutStore
{
    private readonly string _layoutDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FileWebDashboardLayoutStore(LibraryContext context)
    {
        _layoutDirectory = Path.Combine(context.DataDirectory, "widgets", "layouts");
        Directory.CreateDirectory(_layoutDirectory);
    }

    public async Task<WebDashboardLayoutRecord?> GetAsync(string ownerKey, string dashboardKey, CancellationToken cancellationToken = default)
    {
        var filePath = ResolveFilePath(ownerKey, dashboardKey);
        if (!File.Exists(filePath))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<WebDashboardLayoutRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
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
            Widgets = widgets.ToList(),
            UpdatedUtc = DateTime.UtcNow
        };

        var filePath = ResolveFilePath(ownerKey, dashboardKey);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return record;
    }

    private string ResolveFilePath(string ownerKey, string dashboardKey)
    {
        var safeOwner = SanitizeKey(ownerKey);
        var safeDashboard = SanitizeKey(dashboardKey);
        return Path.Combine(_layoutDirectory, safeOwner, $"{safeDashboard}.json");
    }

    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "_default";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(key.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return sanitized.ToLowerInvariant();
    }
}
