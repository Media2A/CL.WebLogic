using CL.WebLogic.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace CL.WebLogic.AspNetCore;

public sealed class WebLogicRealtimeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "audience:public").ConfigureAwait(false);

            var userId = ResolveUserId(httpContext);
            var accessGroups = ResolveAccessGroups(httpContext);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "audience:authenticated").ConfigureAwait(false);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}").ConfigureAwait(false);
            }

            foreach (var accessGroup in accessGroups)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"access:{accessGroup}").ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public Task<WebLogicRealtimeReplayResponse> Replay(WebLogicRealtimeReplayRequest? request)
    {
        var webLogic = WebLogicLibrary.GetRequired();
        var bridge = webLogic.RealtimeBridge
            ?? throw new InvalidOperationException("CL.WebLogic realtime bridge is not available.");

        var take = request?.Take ?? 100;
        var snapshot = bridge.CreateSnapshot(take);
        var filter = request?.Filter;
        var filtered = snapshot.Events
            .Where(evt => filter is null || filter.Matches(evt))
            .Select(static evt => evt.ToHubMessage())
            .ToArray();

        return Task.FromResult(new WebLogicRealtimeReplayResponse(snapshot, filtered));
    }

    private static string ResolveUserId(HttpContext httpContext) =>
        httpContext.Session.GetString("weblogic.user_id")
        ?? httpContext.Request.Query["userId"].ToString()
        ?? string.Empty;

    private static IReadOnlyList<string> ResolveAccessGroups(HttpContext httpContext)
    {
        var raw = httpContext.Session.GetString("weblogic.access_groups");
        if (string.IsNullOrWhiteSpace(raw))
            raw = httpContext.Request.Query["accessGroups"].ToString();

        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class SignalRWebLogicRealtimeBroadcaster : IWebLogicRealtimeBroadcaster
{
    private readonly IHubContext<WebLogicRealtimeHub> _hubContext;

    public SignalRWebLogicRealtimeBroadcaster(IHubContext<WebLogicRealtimeHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastAsync(WebLogicRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var message = envelope.ToHubMessage();

        switch (envelope.Audience)
        {
            case WebLogicRealtimeAudience.Public:
                await _hubContext.Clients.Group("audience:public")
                    .SendAsync("weblogic:event", message, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case WebLogicRealtimeAudience.Authenticated:
                await _hubContext.Clients.Group("audience:authenticated")
                    .SendAsync("weblogic:event", message, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case WebLogicRealtimeAudience.AccessGroup:
                if (envelope.AccessGroups.Count == 0)
                    return;

                foreach (var accessGroup in envelope.AccessGroups)
                {
                    await _hubContext.Clients.Group($"access:{accessGroup}")
                        .SendAsync("weblogic:event", message, cancellationToken)
                        .ConfigureAwait(false);
                }
                break;

            case WebLogicRealtimeAudience.Internal:
                await _hubContext.Clients.Group("audience:authenticated")
                    .SendAsync("weblogic:event", message, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }
}
