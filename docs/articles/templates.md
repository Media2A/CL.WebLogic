# Template Engine

CL.WebLogic uses a file-based template engine with layouts, partials, conditionals, loops, and model binding.

## Template Syntax

### Model Values

```html
<!-- HTML-escaped output -->
<h1>{model:title}</h1>

<!-- Raw HTML output (no escaping) -->
<div>{raw:model:content_html}</div>
```

### Layouts

Every template can specify a layout:

```html
{layout:layouts/main}

<h1>Page content goes here</h1>
```

The layout uses `{renderbody}` to inject the template content:

```html
<!DOCTYPE html>
<html>
<head>{renderhead}</head>
<body>
    {renderbody}
</body>
</html>
```

### Partials

Include reusable template fragments:

```html
{partial:partials/navigation}
{partial:partials/footer}
```

### Conditionals

```html
<!-- Check model value (truthy) -->
{if:model:show_banner}
    <div class="banner">...</div>
{/if}

<!-- Negative check -->
{ifnot:model:is_locked}
    <button>Submit</button>
{/ifnot}

<!-- Authentication -->
{if:auth}
    <p>Welcome back!</p>
{/if}

{ifnot:auth}
    <a href="/login">Sign in</a>
{/ifnot}

<!-- Access groups -->
{if:accessgroup:admin}
    <a href="/admin">Admin</a>
{/if}

<!-- Permissions -->
{if:permission:content.edit}
    <button>Edit</button>
{/if}
```

Conditionals can be nested to any depth.

### Session Values

```html
<span>{session:display_name}</span>
<span>{session:fraghunt.theme}</span>
```

### CSRF Protection

```html
<form method="post" action="/api/submit">
    {csrf}
    <!-- Renders: <input type="hidden" name="_csrf" value="..."> -->
</form>
```

### Widget Areas

```html
{widgetarea:home.hero}
{widgetarea:sidebar.main}
```

## Meta Tags

Set page metadata via `WebPageMeta` in your handler:

```csharp
Meta = new WebPageMeta
{
    Title = "My Page | Site",
    Description = "Page description",
    CanonicalUrl = "/my-page",
    OpenGraph = new WebOpenGraphMeta
    {
        Title = "My Page",
        Type = "website"
    }
}
```

The layout renders meta tags with `{renderhead}`.

## Theme Root

Each response can optionally specify a different theme directory:

```csharp
return WebResult.Document(new WebPageDocument
{
    TemplatePath = "templates/page.html",
    ThemeRoot = "/path/to/plugin/theme"  // Use plugin's own theme
});
```

## Caching

Templates are cached in production when `Theme.EnableCaching` is set to `true` in the WebLogic config. During development, templates reload on every request.
