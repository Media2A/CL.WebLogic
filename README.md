# CL.WebLogic

A web application toolkit for [CodeLogic 3](https://github.com/Media2A/CodeLogic) — providing routing, templating, forms, realtime, and SPA navigation for .NET 10 web applications.

## Features

| Feature | Description |
|---------|-------------|
| **Routing** | Attribute-free page and API registration with tags, access groups, and middleware |
| **Template Engine** | File-based HTML templates with layouts, partials, conditionals, loops, filters, and model binding |
| **SPA Navigation** | Client-side shell swap with layout detection, history management, and content transitions |
| **Form System** | C# model-based forms with client+server validation, AJAX submission, and file uploads |
| **Widget System** | Composable widget areas with server-rendered content and client-side refresh |
| **Server Commands** | JSON command responses: toast, overlay, redirect, navigate, reload, replace DOM |
| **Realtime** | SignalR bridge with auto-connect, event channels, and widget refresh |
| **Auth Abstractions** | Pluggable identity store, session auth, RBAC with permission resolver |
| **Plugin Support** | `IWebRouteContributor` for plugins to register routes with their own theme directories |
| **Asset Pipeline** | Static file serving with ETag, Last-Modified, Cache-Control headers |
| **Security** | CSRF protection, rate limiting, response compression |

## Architecture

```
CL.WebLogic/
├── src/                    — Server-side toolkit
│   ├── AspNetCore/         — ASP.NET Core integration
│   ├── Client/             — Browser-side client runtime (weblogic.client.js)
│   ├── Configuration/      — Config models
│   ├── Forms/              — Form binding, validation, rendering, schemas
│   ├── Routing/            — Route registration, contributor contracts
│   ├── Runtime/            — Request context, results, middleware, auditing
│   ├── Security/           — Auth support, CSRF, security service
│   └── Theming/            — Template engine, widget system, dashboard layouts
├── samples/
│   ├── StarterWebsite/     — Reference app with pages, widgets, forms, auth
│   ├── MiniBlog/           — Blog sample with admin plugin
│   └── Plugins/            — Sample plugin projects
└── tests/                  — Toolkit unit tests
```

## Quick Start

```csharp
using CL.WebLogic;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebLogic(options =>
{
    options.FrameworkRootPath = "data/codelogic";
    options.ApplicationRootPath = "data/app";
});

app.Run();
```

### Register Routes

```csharp
public class MyApp : IApplication, IWebRouteContributor
{
    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterPage("/", new WebRouteOptions
        {
            Name = "Home",
            Description = "Homepage"
        }, HandleHomeAsync, "GET");

        return Task.CompletedTask;
    }

    private Task<WebResult> HandleHomeAsync(WebRequestContext request)
    {
        return Task.FromResult(WebResult.Document(new WebPageDocument
        {
            TemplatePath = "templates/home.html",
            Model = new Dictionary<string, object?>
            {
                ["title"] = "Welcome"
            }
        }));
    }
}
```

### Templates

```html
{layout:layouts/main}

<h1>{model:title}</h1>

{if:auth}
    <p>Welcome back, {session:display_name}!</p>
{/if}

{ifnot:auth}
    <a href="/login">Sign in</a>
{/ifnot}

{widgetarea:home.sidebar}
```

### Form Models

```csharp
[WebForm(Id = "contact", Name = "Contact Form")]
public class ContactForm
{
    [WebFormField(Label = "Name", Required = true, MinLength = 2, MaxLength = 100)]
    public string Name { get; set; } = "";

    [WebFormField(Label = "Email", InputType = WebFormInputType.Email, Required = true)]
    public string Email { get; set; } = "";

    [WebFormField(Label = "Message", InputType = WebFormInputType.TextArea, Required = true)]
    public string Message { get; set; } = "";
}
```

Server-side validation:

```csharp
var result = await request.Forms.BindAsync<ContactForm>();
if (!result.IsValid)
    return WebResult.Json(new { errors = result.Errors });
```

Client schema auto-generated:

```csharp
var schema = request.Forms.GetClientSchema<ContactForm>();
```

### Server Commands

Return JSON commands from API handlers — the client processes them automatically:

```csharp
return WebResult.Commands(
    WebResult.ToastCommand("Saved!", "success"),
    WebResult.NavigateCommand("/dashboard"));

// Or full-screen overlay:
return WebResult.Commands(
    WebResult.OverlayCommand("Success", "Your changes have been saved", "success", 2000),
    WebResult.RedirectCommand("/home"));
```

### AJAX Forms

Add `data-weblogic-form="ajax"` to any form — it submits via fetch and processes command responses:

```html
<form method="post" action="/api/contact" data-weblogic-form="ajax"
      data-weblogic-form-schema="schema-contact">
    <input type="text" name="Name" required>
    <button type="submit">Send</button>
</form>
<script type="application/json" id="schema-contact">
    <!-- Auto-generated from C# model -->
</script>
```

### Plugins

Plugins register their own routes and can serve from their own theme directory:

```csharp
public class MyPlugin : IPlugin, IWebRouteContributor
{
    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterPage("/my-plugin", options, async request =>
        {
            return WebResult.Document(new WebPageDocument
            {
                TemplatePath = "templates/page.html",
                ThemeRoot = _pluginThemeRoot  // Serve from plugin's own theme
            });
        }, "GET");

        return Task.CompletedTask;
    }
}
```

## Samples

| Sample | Description |
|--------|-------------|
| **StarterWebsite** | Full reference app: pages, auth, widgets, forms, dashboards |
| **MiniBlog** | Simple blog with posts, admin panel plugin, and widget areas |

Run the starter:

```bash
cd samples/StarterWebsite
dotnet run
```

## Tests

```bash
dotnet test tests/CL.WebLogic.Tests/CL.WebLogic.Tests.csproj
```

## Requirements

- .NET 10
- [CodeLogic 3](https://github.com/Media2A/CodeLogic)

## License

Proprietary — Media2A.
