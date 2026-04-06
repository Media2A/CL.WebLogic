using System.Collections.ObjectModel;

namespace CL.WebLogic.Realtime;

public sealed class WebLogicRealtimeBuffer
{
    private readonly object _gate = new();
    private readonly Queue<WebLogicRealtimeEnvelope> _items = new();

    public int Capacity { get; }

    public WebLogicRealtimeBuffer(int capacity = 256)
    {
        Capacity = capacity < 1 ? 1 : capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void Add(WebLogicRealtimeEnvelope envelope)
    {
        lock (_gate)
        {
            _items.Enqueue(envelope);
            while (_items.Count > Capacity)
                _items.Dequeue();
        }
    }

    public IReadOnlyList<WebLogicRealtimeEnvelope> GetRecent(int take = 100)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
                return [];

            var count = take < 1 ? 1 : take;
            return _items
                .Skip(Math.Max(0, _items.Count - count))
                .ToArray();
        }
    }

    public WebLogicRealtimeSnapshot CreateSnapshot(int take = 100)
    {
        var events = GetRecent(take);
        return new WebLogicRealtimeSnapshot(DateTimeOffset.UtcNow, events, Count, Capacity);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}

public sealed record WebLogicRealtimeSnapshot(
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<WebLogicRealtimeEnvelope> Events,
    int Count,
    int Capacity);

public sealed record WebLogicRealtimeSubscriptionFilter
{
    public IReadOnlyList<WebLogicRealtimeKind> Kinds { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<string> AccessGroups { get; init; } = [];
    public WebLogicRealtimeAudience? Audience { get; init; }
    public bool IncludeInternal { get; init; }

    public bool Matches(WebLogicRealtimeEnvelope envelope)
    {
        if (!IncludeInternal && envelope.Audience == WebLogicRealtimeAudience.Internal)
            return false;

        if (Audience.HasValue && envelope.Audience != Audience.Value)
            return false;

        if (Kinds.Count > 0 && !Kinds.Contains(envelope.Kind))
            return false;

        if (Sources.Count > 0 && !Sources.Contains(envelope.Source, StringComparer.OrdinalIgnoreCase))
            return false;

        if (AccessGroups.Count == 0)
            return true;

        return envelope.AccessGroups.Count > 0 &&
               envelope.AccessGroups.Any(group => AccessGroups.Contains(group, StringComparer.OrdinalIgnoreCase));
    }
}
