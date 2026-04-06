using System.Collections.ObjectModel;

namespace CL.WebLogic.Realtime;

public enum WebLogicRealtimeKind
{
    Unknown = 0,
    RequestHandled = 1,
    RequestBlocked = 2,
    ThemeSynchronized = 3,
    LibraryStarted = 4,
    LibraryStopped = 5,
    LibraryFailed = 6,
    PluginLoaded = 7,
    PluginUnloaded = 8,
    PluginFailed = 9,
    ConfigReloaded = 10,
    LocalizationReloaded = 11,
    HealthCheckCompleted = 12,
    ShutdownRequested = 13,
    ComponentAlert = 14,
    Custom = 100
}

public enum WebLogicRealtimeAudience
{
    Public = 0,
    Authenticated = 1,
    AccessGroup = 2,
    Internal = 3
}

public sealed record WebLogicRealtimeEnvelope
{
    public required Guid Id { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required WebLogicRealtimeKind Kind { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public WebLogicRealtimeAudience Audience { get; init; } = WebLogicRealtimeAudience.Public;
    public IReadOnlyList<string> AccessGroups { get; init; } = [];
    public string? CorrelationId { get; init; }
    public object? Payload { get; init; }
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    public static WebLogicRealtimeEnvelope Create(
        WebLogicRealtimeKind kind,
        string source,
        string title,
        string? message = null,
        object? payload = null,
        WebLogicRealtimeAudience audience = WebLogicRealtimeAudience.Public,
        IEnumerable<string>? accessGroups = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        DateTimeOffset? timestampUtc = null,
        Guid? id = null)
    {
        return new WebLogicRealtimeEnvelope
        {
            Id = id ?? Guid.NewGuid(),
            TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow,
            Kind = kind,
            Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
            Title = string.IsNullOrWhiteSpace(title) ? kind.ToString() : title.Trim(),
            Message = message,
            Payload = payload,
            Audience = audience,
            AccessGroups = NormalizeStrings(accessGroups),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            Properties = properties ?? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>())
        };
    }

    public WebLogicRealtimeHubMessage ToHubMessage() => new()
    {
        Id = Id,
        TimestampUtc = TimestampUtc,
        Kind = Kind,
        Source = Source,
        Title = Title,
        Message = Message,
        Audience = Audience,
        AccessGroups = AccessGroups,
        CorrelationId = CorrelationId,
        Payload = Payload,
        Properties = Properties
    };

    private static IReadOnlyList<string> NormalizeStrings(IEnumerable<string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record WebLogicRealtimeHubMessage
{
    public required Guid Id { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required WebLogicRealtimeKind Kind { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public WebLogicRealtimeAudience Audience { get; init; }
    public IReadOnlyList<string> AccessGroups { get; init; } = [];
    public string? CorrelationId { get; init; }
    public object? Payload { get; init; }
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    public WebLogicRealtimeEnvelope ToEnvelope() => WebLogicRealtimeEnvelope.Create(
        Kind,
        Source,
        Title,
        Message,
        Payload,
        Audience,
        AccessGroups,
        CorrelationId,
        Properties,
        TimestampUtc,
        Id);
}
