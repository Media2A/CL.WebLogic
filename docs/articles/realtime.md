# Realtime

CL.WebLogic includes a SignalR bridge for pushing events from the server to connected clients.

## Setup

Realtime is enabled by default. The client auto-connects to the SignalR hub at `/weblogic-hubs/events`.

## Server-Side Events

Push events to connected clients:

```csharp
var realtime = WebLogicLibrary.GetRequired().RealtimeService;
await realtime.SendToUserAsync(userId, "notification.new", new
{
    title = "New message",
    body = "You have a new message"
});
```

## Client-Side Listeners

```javascript
WebLogicClient.on("realtime:event", function(data) {
    var evt = data.event || {};
    var props = evt.properties || {};
    var payload = evt.payload || {};

    if (props.eventType === "notification.new") {
        FH.toast(payload.title, "info");
    }
});
```

## Configuration

```json
{
  "Realtime": {
    "Enabled": true,
    "HubUrl": "/weblogic-hubs/events"
  }
}
```

## Widget Channels

Widgets can subscribe to realtime channels for auto-refresh:

```javascript
WebLogicClient.widgets.emitChannel("dashboard.stats", {
    action: "refresh"
});
```
