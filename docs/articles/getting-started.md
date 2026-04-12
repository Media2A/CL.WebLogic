# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [CodeLogic 3](https://github.com/Media2A/CodeLogic) framework

## Create a New Project

1. Create a new ASP.NET Core project:

```bash
dotnet new web -n MyWebsite
cd MyWebsite
```

2. Add references to CodeLogic and CL.WebLogic.

3. Set up `Program.cs`:

```csharp
using CL.WebLogic;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebLogic(options =>
{
    options.AppVersion = "1.0.0";
    options.FrameworkRootPath = "data/codelogic";
    options.ApplicationRootPath = "data/app";
});

app.Run();
```

## Create Your Application

Implement `IApplication` and `IWebRouteContributor`:

```csharp
public sealed class MyApp : IApplication, IWebRouteContributor
{
    public ApplicationManifest Manifest { get; } = new()
    {
        Id = "my-website",
        Name = "My Website",
        Version = "1.0.0"
    };

    public Task RegisterRoutesAsync(WebRegistrationContext context)
    {
        context.RegisterPage("/", new WebRouteOptions
        {
            Name = "Home"
        }, request => Task.FromResult(WebResult.Document(new WebPageDocument
        {
            TemplatePath = "templates/home.html",
            Model = new Dictionary<string, object?>
            {
                ["title"] = "Hello World"
            }
        })), "GET");

        return Task.CompletedTask;
    }
}
```

## Create a Theme

```
theme/
├── layouts/
│   └── main.html
├── templates/
│   └── home.html
├── partials/
│   └── nav.html
└── assets/
    ├── app.css
    └── app.js
```

### Layout (`layouts/main.html`)

```html
<!DOCTYPE html>
<html>
<head>
    {renderhead}
    <link rel="stylesheet" href="/assets/app.css">
</head>
<body>
    {partial:partials/nav}
    <main>
        {renderbody}
    </main>
    <script src="/assets/app.js"></script>
    <script src="/weblogic/client/weblogic.client.js"></script>
</body>
</html>
```

### Template (`templates/home.html`)

```html
{layout:layouts/main}

<h1>{model:title}</h1>
```

## Run

```bash
dotnet run
```

Visit `http://localhost:5000` to see your page.

## Next Steps

- [Template Engine](templates.md) — learn the full template syntax
- [Forms & Validation](forms.md) — build validated forms with C# models
- [Server Commands](commands.md) — return toast notifications and redirects from APIs
- [Plugins](plugins.md) — add modular features via plugins
