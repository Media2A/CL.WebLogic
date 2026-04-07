using System.Text.Json;
using CL.WebLogic.Configuration;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic.Theming;

public sealed class WebWidgetSettingsRecord
{
    public required string InstanceId { get; init; }
    public string WidgetName { get; init; } = string.Empty;
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}

public interface IWebWidgetSettingsStore
{
    Task<IReadOnlyList<WebWidgetSettingsRecord>> GetAllAsync();
    Task<WebWidgetSettingsRecord?> GetAsync(string instanceId);
    Task UpsertAsync(string instanceId, string widgetName, IReadOnlyDictionary<string, string> settings);
}

public sealed class FileWebWidgetSettingsStore : IWebWidgetSettingsStore
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileWebWidgetSettingsStore(LibraryContext context, WebLogicConfig config)
    {
        _settingsPath = Path.Combine(context.DataDirectory, "widgets", config.Widgets.SettingsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
    }

    public async Task<IReadOnlyList<WebWidgetSettingsRecord>> GetAllAsync()
    {
        var state = await LoadStateAsync().ConfigureAwait(false);
        return state.Values
            .OrderBy(static item => item.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<WebWidgetSettingsRecord?> GetAsync(string instanceId)
    {
        var state = await LoadStateAsync().ConfigureAwait(false);
        return state.TryGetValue(instanceId, out var record) ? record : null;
    }

    public async Task UpsertAsync(string instanceId, string widgetName, IReadOnlyDictionary<string, string> settings)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = await LoadStateCoreAsync().ConfigureAwait(false);
            state[instanceId] = new WebWidgetSettingsRecord
            {
                InstanceId = instanceId,
                WidgetName = widgetName,
                Settings = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase),
                UpdatedUtc = DateTime.UtcNow
            };

            await SaveStateCoreAsync(state).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, WebWidgetSettingsRecord>> LoadStateAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadStateCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, WebWidgetSettingsRecord>> LoadStateCoreAsync()
    {
        if (!File.Exists(_settingsPath))
            return new Dictionary<string, WebWidgetSettingsRecord>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(_settingsPath);
        var state = await JsonSerializer.DeserializeAsync<Dictionary<string, WebWidgetSettingsRecord>>(stream).ConfigureAwait(false);
        return state is null
            ? new Dictionary<string, WebWidgetSettingsRecord>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WebWidgetSettingsRecord>(state, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveStateCoreAsync(Dictionary<string, WebWidgetSettingsRecord> state)
    {
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, state, new JsonSerializerOptions
        {
            WriteIndented = true
        }).ConfigureAwait(false);
    }
}
