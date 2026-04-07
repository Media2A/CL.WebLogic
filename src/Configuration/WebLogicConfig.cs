using System.ComponentModel.DataAnnotations;
using CodeLogic.Core.Configuration;

namespace CL.WebLogic.Configuration;

[ConfigSection("weblogic")]
public sealed class WebLogicConfig : ConfigModelBase
{
    public bool Enabled { get; set; } = true;
    public string SiteName { get; set; } = "CL.WebLogic Site";
    public ThemeConfig Theme { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public MySqlConfig MySql { get; set; } = new();
    public WidgetConfig Widgets { get; set; } = new();

    public override ConfigValidationResult Validate()
    {
        var errors = new List<string>();
        errors.AddRange(base.Validate().Errors);

        if (Theme.Source == ThemeSource.Git && string.IsNullOrWhiteSpace(Theme.RepositoryUrl))
            errors.Add("Theme.RepositoryUrl is required when Theme.Source is Git.");

        if (Storage.Mode == WebStorageMode.S3 && string.IsNullOrWhiteSpace(Storage.S3Bucket))
            errors.Add("Storage.S3Bucket is required when Storage.Mode is S3.");

        return errors.Count == 0 ? ConfigValidationResult.Valid() : ConfigValidationResult.Invalid(errors);
    }
}

public sealed class ThemeConfig
{
    public ThemeSource Source { get; set; } = ThemeSource.Local;
    public string LocalPath { get; set; } = "theme";
    public string RepositoryId { get; set; } = "weblogic-theme";
    public string RepositoryUrl { get; set; } = "";
    public string Branch { get; set; } = "main";
    public bool AutoSyncOnStart { get; set; } = false;
    public string ThemeSubPath { get; set; } = "";
}

public enum ThemeSource
{
    Local,
    Git
}

public sealed class SecurityConfig
{
    public bool EnforceHttps { get; set; } = false;
    public bool TrustForwardedHeaders { get; set; } = true;
    public bool EnableDnsbl { get; set; } = false;
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
    public RateLimitConfig RateLimit { get; set; } = new();
}

public sealed class AuthConfig
{
    public WebAuthMode Mode { get; set; } = WebAuthMode.None;
    public bool AllowHeaderUserId { get; set; } = true;
    public bool AllowHeaderAccessGroups { get; set; } = true;
    public bool AllowSessionUserId { get; set; } = true;
    public bool AllowSessionAccessGroups { get; set; } = true;
    public MySqlAuthConfig MySql { get; set; } = new();
}

public enum WebAuthMode
{
    None,
    MySql
}

public sealed class MySqlAuthConfig
{
    public bool Enabled { get; set; } = false;
    public bool SyncTablesOnStart { get; set; } = true;
    public bool SeedDemoRecords { get; set; } = false;
    public string ConnectionId { get; set; } = "Default";
    public string DemoAdminUserId { get; set; } = "demo-admin";
}

public sealed class RateLimitConfig
{
    [Range(1, 10000)]
    public int RequestsPerWindow { get; set; } = 120;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}

public sealed class StorageConfig
{
    public WebStorageMode Mode { get; set; } = WebStorageMode.Local;
    public string LocalRoot { get; set; } = "theme";
    public string S3ConnectionId { get; set; } = "Default";
    public string S3Bucket { get; set; } = "";
    public string S3Prefix { get; set; } = "";
}

public enum WebStorageMode
{
    Local,
    S3
}

public sealed class MySqlConfig
{
    public bool EnableRequestLogging { get; set; } = false;
    public bool SyncTablesOnStart { get; set; } = true;
    public string ConnectionId { get; set; } = "Default";
}

public sealed class WidgetConfig
{
    public bool EnablePersistentSettings { get; set; } = true;
    public string SettingsFileName { get; set; } = "widget-settings.json";
}
