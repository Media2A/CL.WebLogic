# Server Commands

CL.WebLogic provides a JSON command response pattern that lets the server tell the client what to do after a form submission or API call.

## How It Works

1. Form submits via AJAX (`data-weblogic-form="ajax"`)
2. Server returns JSON with a `commands` array
3. WebLogic client processes each command in order
4. Toasts/overlays show immediately, navigation is deferred

## Command Types

| Command | Description |
|---------|-------------|
| `toast` | Show a toast notification (bottom-right corner) |
| `overlay` | Show a full-screen overlay with icon and message |
| `redirect` | Navigate to URL (full page load) |
| `navigate` | Navigate via SPA (shell swap) |
| `reload` | Reload the current page |
| `replace` | Replace innerHTML of a DOM element |
| `remove` | Remove a DOM element |
| `addClass` | Add a CSS class to an element |
| `removeClass` | Remove a CSS class from an element |

## C# Helpers

```csharp
// Toast notification
WebResult.Commands(
    WebResult.ToastCommand("Saved successfully", "success"));

// Full-screen overlay with animated checkmark, then redirect
WebResult.Commands(
    WebResult.OverlayCommand("Login Successful", "Welcome back!", "success", 3000),
    WebResult.RedirectCommand("/dashboard"));

// Toast + SPA navigation
WebResult.Commands(
    WebResult.ToastCommand("Thread created", "success"),
    WebResult.NavigateCommand("/forums/my-thread"));

// Replace DOM content
WebResult.Commands(
    WebResult.ReplaceCommand("#user-count", "<span>42</span>"));

// Multiple commands
WebResult.Commands(
    WebResult.ToastCommand("Updated", "success"),
    WebResult.RemoveCommand("#old-item"),
    WebResult.ReloadCommand());
```

## JSON Format

```json
{
  "success": true,
  "commands": [
    { "type": "toast", "message": "Saved!", "variant": "success" },
    { "type": "redirect", "url": "/dashboard" }
  ]
}
```

## Timing

When a response contains both a toast/overlay and a navigation command:

1. Toast/overlay shows immediately
2. Navigation is **deferred** until the toast/overlay has been visible
3. For toasts: 1.5 second delay before navigation
4. For overlays: delay matches the overlay's `duration`

This ensures users see the feedback before the page changes.

## Overlay Variants

| Variant | Icon | Use Case |
|---------|------|----------|
| `success` | Animated checkmark | Successful operations |
| `error` | X icon | Failed operations |
| `warning` | Warning triangle | Caution messages |
| `info` | Info circle | Informational messages |
| `processing` | Spinner | Loading/processing state |

```csharp
// Processing overlay (stays until manually removed)
WebResult.OverlayCommand("Processing", "Please wait...", "processing", 0);

// Success with auto-dismiss
WebResult.OverlayCommand("Done!", "Your changes have been saved", "success", 2000);
```

## Client-Side API

You can also trigger commands from JavaScript:

```javascript
// Show toast
window.FH.toast("Hello!", "success");

// Show overlay
window.FH.overlay({
    variant: "success",
    title: "Done",
    message: "Operation complete",
    duration: 2000
});

// Process commands array
WebLogicClient.ui.processCommands([
    { type: "toast", message: "Hi", variant: "info" }
]);
```
