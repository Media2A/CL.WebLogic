# Plugins

Plugins are optional modules that register their own routes, templates, and assets. They are loaded at runtime from the `data/plugins/` directory.

## Create a Plugin

```csharp
public class MyPlugin : IPlugin, IWebRouteContributor
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "MyPlugin",
        Name = "My Plugin",
        Version = "1.0.0"
    };

    private string _themeRoot = "";

    public Task OnInitializeAsync(PluginContext context)
    {
        _themeRoot = Path.Combine(context.PluginDirectory, "theme");
        return Task.CompletedTask;
    }

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterPage("/my-plugin", new WebRouteOptions
        {
            Name = "My Plugin Page"
        }, HandlePageAsync, "GET");

        return Task.CompletedTask;
    }

    private Task<WebResult> HandlePageAsync(WebRequestContext request)
    {
        return Task.FromResult(WebResult.Document(new WebPageDocument
        {
            TemplatePath = "templates/page.html",
            ThemeRoot = _themeRoot  // Use plugin's own templates
        }));
    }
}
```

## Plugin Theme Isolation

By setting `ThemeRoot` on `WebPageDocument`, a plugin serves templates from its own directory instead of the main site's theme. This allows plugins to have their own layouts, templates, and assets without conflicting with the host application.

## Plugin Directory Structure

```
data/plugins/MyPlugin/
├── MyPlugin.dll
├── MyPlugin.deps.json
└── theme/
    ├── layouts/
    │   └── plugin-main.html
    ├── templates/
    │   └── page.html
    └── assets/
        └── plugin.css
```

## Using the Main Site Theme

If a plugin doesn't set `ThemeRoot`, it uses the main site's theme — sharing layouts, partials, and styles with the host application.

## Route Registration

Plugins can register:
- **Pages** — full HTML responses with templates
- **APIs** — JSON endpoints
- **Widgets** — content blocks for widget areas

```csharp
context.RegisterPage("/my-plugin/page", options, handler, "GET");
context.RegisterApi("/api/my-plugin/data", options, handler, "GET", "POST");
context.RegisterWidget("my-plugin.widget", widgetOptions, widgetHandler);
```

## Access Control

```csharp
context.RegisterPage("/my-plugin/admin", new WebRouteOptions
{
    AllowAnonymous = false,
    RequiredAccessGroups = ["admin"]
}, handler, "GET");
```
