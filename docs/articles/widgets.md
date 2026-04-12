# Widgets

Widgets are server-rendered content blocks that can be placed in named areas across your templates.

## Register a Widget

```csharp
context.RegisterWidget("my-app.latest-posts", new WebWidgetOptions
{
    Description = "Shows the 5 latest posts",
    Tags = ["content", "posts"]
}, async widgetContext =>
{
    var posts = await _postService.GetLatestAsync(5);
    return WebWidgetResult.Template("widgets/latest-posts.html", new Dictionary<string, object?>
    {
        ["posts"] = posts
    });
});
```

## Register a Widget Area

```csharp
context.RegisterWidgetArea("home.sidebar", "my-app.latest-posts");
context.RegisterWidgetArea("home.sidebar", "my-app.popular-tags");
```

## Use in Templates

```html
{widgetarea:home.sidebar}
```

This renders all widgets registered to `home.sidebar` in order.

## Widget Result Types

```csharp
// Render from a template file
WebWidgetResult.Template("widgets/my-widget.html", model);

// Return raw HTML
WebWidgetResult.HtmlContent("<div>Hello</div>");

// Return empty (widget hidden)
WebWidgetResult.HtmlContent("");
```

## Widget Templates

Widget templates are regular HTML files with model binding:

```html
<div class="card">
    <div class="card-header">
        <h5>{model:title}</h5>
    </div>
    <div class="card-body">
        {raw:model:content_html}
    </div>
</div>
```
