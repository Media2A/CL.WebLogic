using CL.WebLogic;
using CodeLogic;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application.Plugins;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StarterWebsite.Application;
using StarterWebsite.Application.Infrastructure;
using StarterWebsite.Plugins;

var clResult = await CodeLogic.CodeLogic.InitializeAsync(opts =>
{
    opts.FrameworkRootPath = "data/codelogic";
    opts.ApplicationRootPath = "data/app";
    opts.AppVersion = "1.0.0";
    opts.HandleShutdownSignals = false;
});

if (!clResult.Success || clResult.ShouldExit)
{
    Console.Error.WriteLine($"CodeLogic init failed: {clResult.Message}");
    return;
}

EnsureSampleConfiguration();

await WebLogicBootstrap.LoadRecommendedLibrariesAsync(new WebLogicBootstrapOptions
{
});

CodeLogic.CodeLogic.RegisterApplication(new StarterWebsiteApplication());

await CodeLogic.CodeLogic.ConfigureAsync();
ConfigureWebLogicPersistence();
await CodeLogic.CodeLogic.StartAsync();

var pluginMgr = new PluginManager(
    CodeLogic.CodeLogic.GetEventBus(),
    new PluginOptions
    {
        PluginsDirectory = "data/plugins",
        EnableHotReload = false
    });

await LoadInProcessPluginAsync(pluginMgr, new ThemeShowcasePlugin());
await LoadInProcessPluginAsync(pluginMgr, new PluginApiPlugin());
CodeLogic.CodeLogic.SetPluginManager(pluginMgr);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Services.AddSingleton<IEventBus>(CodeLogic.CodeLogic.GetEventBus());
builder.Services.AddCodeLogicWebLogic();
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "data", "keys")));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();
app.Urls.Clear();
app.Urls.Add("http://127.0.0.1:53248");
app.UseSession();
app.UseCodeLogicWebLogic();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
    CodeLogic.CodeLogic.StopAsync().GetAwaiter().GetResult());

app.Run();

static async Task LoadInProcessPluginAsync(PluginManager manager, IPlugin plugin)
{
    var ctx = CodeLogic.CodeLogic.GetApplicationContext()
        ?? throw new InvalidOperationException("Application context not available.");

    var pluginDir = Path.Combine("data/plugins", plugin.Manifest.Id);
    Directory.CreateDirectory(Path.Combine(pluginDir, "logs"));

    var pluginCtx = new PluginContext
    {
        PluginId = plugin.Manifest.Id,
        PluginDirectory = pluginDir,
        ConfigDirectory = pluginDir,
        LocalizationDirectory = Path.Combine(pluginDir, "localization"),
        LogsDirectory = Path.Combine(pluginDir, "logs"),
        DataDirectory = Path.Combine(pluginDir, "data"),
        Logger = ctx.Logger,
        Configuration = new CodeLogic.Core.Configuration.ConfigurationManager(pluginDir),
        Localization = ctx.Localization,
        Events = ctx.Events
    };

    await plugin.OnConfigureAsync(pluginCtx);
    await pluginCtx.Configuration.GenerateAllDefaultsAsync();
    await pluginCtx.Configuration.LoadAllAsync();
    await plugin.OnInitializeAsync(pluginCtx);
    await plugin.OnStartAsync(pluginCtx);
    await manager.RegisterInProcessPluginAsync(plugin, pluginCtx);
}

static void EnsureSampleConfiguration()
{
    var frameworkRoot = Path.Combine(AppContext.BaseDirectory, "data", "codelogic", "Libraries");
    var webLogicDir = Path.Combine(frameworkRoot, "CL.WebLogic");
    Directory.CreateDirectory(webLogicDir);

    var webLogicConfigPath = Path.Combine(webLogicDir, "config.weblogic.json");
    Dictionary<string, object?> webLogicConfig;
    if (File.Exists(webLogicConfigPath))
    {
        webLogicConfig = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(webLogicConfigPath))
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }
    else
    {
        webLogicConfig = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    webLogicConfig["enabled"] = true;
    webLogicConfig["siteName"] ??= "CL.WebLogic Site";
    webLogicConfig["auth"] = new
    {
        allowHeaderUserId = true,
        allowHeaderAccessGroups = true,
        allowSessionUserId = true,
        allowSessionAccessGroups = true
    };

    File.WriteAllText(webLogicConfigPath, JsonSerializer.Serialize(webLogicConfig, new JsonSerializerOptions { WriteIndented = true }));
}

static void ConfigureWebLogicPersistence()
{
    var web = WebLogicLibrary.GetRequired();
    var appDataRoot = Path.Combine(AppContext.BaseDirectory, "data", "app", "starter");
    var layoutFilePath = Path.Combine(appDataRoot, "dashboard-layouts.json");
    web.UseDashboardLayoutStore(new JsonDashboardLayoutStore(layoutFilePath));
}
