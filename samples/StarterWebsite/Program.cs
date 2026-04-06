using CL.WebLogic;
using CodeLogic;
using CodeLogic.Core.Configuration;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application.Plugins;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StarterWebsite.Application;
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

var enableMySql = EnsureSampleDatabaseConfigs();

await WebLogicBootstrap.LoadRecommendedLibrariesAsync(new WebLogicBootstrapOptions
{
    IncludeMySql = enableMySql
});

CodeLogic.CodeLogic.RegisterApplication(new StarterWebsiteApplication());

await CodeLogic.CodeLogic.ConfigureAsync();
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

static bool EnsureSampleDatabaseConfigs()
{
    var host = Environment.GetEnvironmentVariable("STARTER_MYSQL_HOST");
    var database = Environment.GetEnvironmentVariable("STARTER_MYSQL_DATABASE");
    var username = Environment.GetEnvironmentVariable("STARTER_MYSQL_USERNAME");
    var password = Environment.GetEnvironmentVariable("STARTER_MYSQL_PASSWORD");

    if (string.IsNullOrWhiteSpace(host)
        || string.IsNullOrWhiteSpace(database)
        || string.IsNullOrWhiteSpace(username))
    {
        return false;
    }

    var frameworkRoot = Path.Combine(AppContext.BaseDirectory, "data", "codelogic", "Libraries");
    var mySqlDir = Path.Combine(frameworkRoot, "CL.MySQL2");
    var webLogicDir = Path.Combine(frameworkRoot, "CL.WebLogic");
    Directory.CreateDirectory(mySqlDir);
    Directory.CreateDirectory(webLogicDir);

    var mySqlConfigPath = Path.Combine(mySqlDir, "config.mysql.json");
    var mySqlConfig = new
    {
        databases = new Dictionary<string, object?>
        {
            ["Default"] = new
            {
                enabled = true,
                host,
                port = ParseInt(Environment.GetEnvironmentVariable("STARTER_MYSQL_PORT"), 3306),
                database,
                username,
                password = password ?? string.Empty,
                enablePooling = true,
                minPoolSize = 1,
                maxPoolSize = 20,
                connectionLifetime = 300,
                connectionTimeout = 30,
                commandTimeout = 30,
                enableSsl = ParseBool(Environment.GetEnvironmentVariable("STARTER_MYSQL_SSL"), false),
                allowPublicKeyRetrieval = ParseBool(Environment.GetEnvironmentVariable("STARTER_MYSQL_ALLOW_PUBLIC_KEY_RETRIEVAL"), true),
                characterSet = "utf8mb4",
                collation = "utf8mb4_unicode_ci",
                allowDestructiveSync = false,
                backupDirectory = (string?)null,
                slowQueryThresholdMs = 1000
            }
        }
    };

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
        mode = "mySql",
        allowHeaderUserId = true,
        allowHeaderAccessGroups = true,
        allowSessionUserId = true,
        allowSessionAccessGroups = true,
        mySql = new
        {
            enabled = true,
            syncTablesOnStart = true,
            seedDemoRecords = ParseBool(Environment.GetEnvironmentVariable("STARTER_WEBLOGIC_SEED_DEMO_AUTH"), true),
            connectionId = "Default",
            demoAdminUserId = Environment.GetEnvironmentVariable("STARTER_WEBLOGIC_DEMO_ADMIN_USER_ID") ?? "demo-admin"
        }
    };

    File.WriteAllText(mySqlConfigPath, JsonSerializer.Serialize(mySqlConfig, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(webLogicConfigPath, JsonSerializer.Serialize(webLogicConfig, new JsonSerializerOptions { WriteIndented = true }));
    return true;
}

static int ParseInt(string? raw, int fallback) =>
    int.TryParse(raw, out var value) ? value : fallback;

static bool ParseBool(string? raw, bool fallback) =>
    bool.TryParse(raw, out var value) ? value : fallback;
