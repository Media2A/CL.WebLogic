---
_layout: landing
---

# CL.WebLogic

CL.WebLogic is a web application toolkit for [CodeLogic 3](https://github.com/Media2A/CodeLogic) — providing routing, templating, forms, realtime, and SPA navigation for .NET 10 web applications.

---

## Core Concepts

| Concept | Description |
|---------|-------------|
| **Routes** | Register pages and APIs with options, access groups, and handlers |
| **Templates** | File-based HTML with layouts, partials, conditionals, loops, and model binding |
| **Forms** | C# model-driven validation on both client and server |
| **Widgets** | Composable server-rendered content areas |
| **Commands** | JSON response pattern for toasts, redirects, overlays, and DOM updates |
| **Realtime** | SignalR-based event bridge |
| **Plugins** | Route contributors with isolated theme directories |

---

## How It Works

```text
Browser Request
  → ASP.NET Core Pipeline
    → WebLogic Middleware (CSRF, rate limit, compression)
      → Route Matching
        → Handler Function (WebRequestContext → WebResult)
          → Template Rendering (layout + partials + model)
            → Response (HTML or JSON)
```

For AJAX form submissions:

```text
Form Submit (fetch)
  → Handler returns WebResult.Commands(...)
    → Client processes: toast → delay → redirect/reload
```

---

## Template Syntax

| Syntax | Purpose |
|--------|---------|
| `{model:key}` | Output model value (HTML-escaped) |
| `{raw:model:key}` | Output model value (raw HTML) |
| `{if:condition}...{/if}` | Conditional block |
| `{ifnot:condition}...{/ifnot}` | Negative conditional |
| `{if:auth}...{/if}` | Authenticated user check |
| `{if:accessgroup:admin}...{/if}` | Access group check |
| `{session:key}` | Session value |
| `{layout:path}` | Set layout template |
| `{partial:path}` | Include partial template |
| `{widgetarea:name}` | Render widget area |
| `{csrf}` | CSRF hidden input |
| `{renderhead}` | Render head meta tags |
| `{renderbody}` | Render body content in layout |

---

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebLogic(options =>
{
    options.FrameworkRootPath = "data/codelogic";
    options.ApplicationRootPath = "data/app";
});

app.Run();
```

---

## Next Steps

- [Getting Started](articles/getting-started.md)
- [Template Engine](articles/templates.md)
- [Forms & Validation](articles/forms.md)
- [Server Commands](articles/commands.md)
- [Plugins](articles/plugins.md)
- [API Reference](api/index.md)
