using CL.WebLogic;
using CL.MySQL2;
using CodeLogic.Core.Configuration;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Application.Plugins;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using MiniBlog;
using MiniBlog.Shared.Infrastructure;
using System.Text.Json;

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
});

if (enableMySql)
    await global::CodeLogic.Libraries.LoadAsync<MySQL2Library>();

CodeLogic.CodeLogic.RegisterApplication(new MiniBlogApplication());

await CodeLogic.CodeLogic.ConfigureAsync();
ConfigureWebLogicPersistence(enableMySql);
await CodeLogic.CodeLogic.StartAsync();

var pluginMgr = new PluginManager(
    CodeLogic.CodeLogic.GetEventBus(),
    new PluginOptions
    {
        PluginsDirectory = ResolvePluginDirectory(),
        EnableHotReload = false,
        WatchForChanges = false
    });

CodeLogic.CodeLogic.SetPluginManager(pluginMgr);
await pluginMgr.LoadAllAsync();
await WebLogicLibrary.GetRequired().RegisterLoadedPluginsAsync(pluginMgr);

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
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();
app.Urls.Clear();
app.Urls.Add("http://127.0.0.1:53260");
app.UseSession();
app.UseCodeLogicWebLogic();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
    CodeLogic.CodeLogic.StopAsync().GetAwaiter().GetResult());

app.Run();

static bool EnsureSampleDatabaseConfigs()
{
    var host = Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_HOST");
    var database = Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_DATABASE");
    var username = Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_USERNAME");
    var password = Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_PASSWORD");

    if (string.IsNullOrWhiteSpace(host)
        || string.IsNullOrWhiteSpace(database)
        || string.IsNullOrWhiteSpace(username))
    {
        return false;
    }

    var frameworkRoot = Path.Combine(AppContext.BaseDirectory, "data", "codelogic", "Libraries");
    var mySqlDir = Path.Combine(frameworkRoot, "CL.MySQL2");
    var webLogicDir = Path.Combine(frameworkRoot, "CL.WebLogic");
    var storageS3Dir = Path.Combine(frameworkRoot, "CL.StorageS3");
    var gitHelperDir = Path.Combine(frameworkRoot, "CL.GitHelper");
    var netUtilsDir = Path.Combine(frameworkRoot, "CL.NetUtils");
    Directory.CreateDirectory(mySqlDir);
    Directory.CreateDirectory(webLogicDir);
    Directory.CreateDirectory(storageS3Dir);
    Directory.CreateDirectory(gitHelperDir);
    Directory.CreateDirectory(netUtilsDir);

    var mySqlConfigPath = Path.Combine(mySqlDir, "config.mysql.json");
    var mySqlConfig = new
    {
        databases = new Dictionary<string, object?>
        {
            ["Default"] = new
            {
                enabled = true,
                host,
                port = ParseInt(Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_PORT"), 3306),
                database,
                username,
                password = password ?? string.Empty,
                enablePooling = true,
                minPoolSize = 1,
                maxPoolSize = 20,
                connectionLifetime = 300,
                connectionTimeout = 30,
                commandTimeout = 30,
                enableSsl = ParseBool(Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_SSL"), false),
                allowPublicKeyRetrieval = ParseBool(Environment.GetEnvironmentVariable("MINIBLOG_MYSQL_ALLOW_PUBLIC_KEY_RETRIEVAL"), true),
                characterSet = "utf8mb4",
                collation = "utf8mb4_unicode_ci",
                allowDestructiveSync = false,
                backupDirectory = (string?)null,
                slowQueryThresholdMs = 1000
            }
        }
    };

    var webLogicConfigPath = Path.Combine(webLogicDir, "config.weblogic.json");
    var storageS3ConfigPath = Path.Combine(storageS3Dir, "config.storages3.json");
    var gitHelperConfigPath = Path.Combine(gitHelperDir, "config.githelper.json");
    var netUtilsConfigPath = Path.Combine(netUtilsDir, "config.netutils.json");
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
    webLogicConfig["siteName"] = "Northwind Journal";
    webLogicConfig["auth"] = new
    {
        allowHeaderUserId = true,
        allowHeaderAccessGroups = true,
        allowSessionUserId = true,
        allowSessionAccessGroups = true
    };

    File.WriteAllText(mySqlConfigPath, JsonSerializer.Serialize(mySqlConfig, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(webLogicConfigPath, JsonSerializer.Serialize(webLogicConfig, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(storageS3ConfigPath, JsonSerializer.Serialize(new
    {
        enabled = false,
        connections = Array.Empty<object>()
    }, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(gitHelperConfigPath, JsonSerializer.Serialize(new
    {
        enabled = false,
        baseDirectory = string.Empty,
        repositories = Array.Empty<object>()
    }, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(netUtilsConfigPath, JsonSerializer.Serialize(new
    {
        enabled = false,
        dnsbl = new
        {
            enabled = false,
            ipv4Services = Array.Empty<string>(),
            ipv4FallbackServices = Array.Empty<string>(),
            ipv6Services = Array.Empty<string>(),
            ipv6FallbackServices = Array.Empty<string>(),
            timeoutSeconds = 5,
            parallelQueries = true,
            detailedLogging = false
        },
        geoIp = new
        {
            enabled = false,
            databasePath = string.Empty,
            autoUpdate = false,
            accountId = 0,
            licenseKey = string.Empty,
            downloadUrl = string.Empty,
            databaseType = "GeoLite2-City",
            timeoutSeconds = 30
        }
    }, new JsonSerializerOptions { WriteIndented = true }));
    return true;
}

static int ParseInt(string? raw, int fallback) =>
    int.TryParse(raw, out var value) ? value : fallback;

static bool ParseBool(string? raw, bool fallback) =>
    bool.TryParse(raw, out var value) ? value : fallback;

static string ResolvePluginDirectory()
{
    var baseDir = AppContext.BaseDirectory;
    var sourcePlugins = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "data", "plugins"));
    return Directory.Exists(sourcePlugins)
        ? sourcePlugins
        : Path.Combine(baseDir, "data", "plugins");
}

static void ConfigureWebLogicPersistence(bool enableMySql)
{
    if (!enableMySql)
        return;

    var web = WebLogicLibrary.GetRequired();
    var identityStore = new MiniBlogMySqlIdentityStore(new MiniBlogMySqlIdentityStoreOptions
    {
        ConnectionId = "Default",
        SyncTablesOnStart = true
    });
    web.UseIdentityStore(identityStore);

    web.UseRequestAuditStore(new MiniBlogMySqlRequestAuditStore(
        connectionId: "Default",
        enabled: ParseBool(Environment.GetEnvironmentVariable("MINIBLOG_WEBLOGIC_REQUEST_LOGGING"), false),
        syncTablesOnStart: true));
}
