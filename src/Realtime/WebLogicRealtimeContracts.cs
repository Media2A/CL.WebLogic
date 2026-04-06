using CodeLogic.Core.Events;

namespace CL.WebLogic.Realtime;

public interface IWebLogicRealtimeBroadcaster
{
    Task BroadcastAsync(WebLogicRealtimeEnvelope envelope, CancellationToken cancellationToken = default);
}

public interface IWebLogicRealtimeBridge : IDisposable
{
    WebLogicRealtimeBuffer Buffer { get; }
    IReadOnlyList<WebLogicRealtimeEnvelope> GetRecent(int take = 100);
    WebLogicRealtimeSnapshot CreateSnapshot(int take = 100);
    Task PublishAsync(WebLogicRealtimeEnvelope envelope, CancellationToken cancellationToken = default);
    void Publish(WebLogicRealtimeEnvelope envelope);
    void RegisterDefaultMappings(IEventBus eventBus);
    IEventSubscription Register<TEvent>(
        IEventBus eventBus,
        Func<TEvent, WebLogicRealtimeEnvelope?> mapper) where TEvent : IEvent;
}

public sealed record WebLogicRealtimeConfiguration
{
    public int BufferSize { get; init; } = 256;
    public bool KeepInternalEvents { get; init; } = true;
}

public sealed record WebLogicRealtimeReplayRequest(
    int Take = 100,
    WebLogicRealtimeSubscriptionFilter? Filter = null);

public sealed record WebLogicRealtimeReplayResponse(
    WebLogicRealtimeSnapshot Snapshot,
    IReadOnlyList<WebLogicRealtimeHubMessage> Events);
