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
    public bool EnableCaching { get; set; } = true;
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

    /// <summary>
    /// Explicit proxy IP addresses that the app will trust to set X-Forwarded-*.
    /// If both TrustedProxies and TrustedNetworks are empty and TrustForwardedHeaders is true,
    /// the middleware falls back to loopback + private ranges (RFC1918) with a warning.
    /// </summary>
    public string[] TrustedProxies { get; set; } = [];

    /// <summary>
    /// Explicit proxy network CIDRs (e.g. "172.16.0.0/12") that the app will trust.
    /// </summary>
    public string[] TrustedNetworks { get; set; } = [];

    public bool EnableDnsbl { get; set; } = false;
    public bool EnableCsrf { get; set; } = true;
    public bool EnableCompression { get; set; } = true;
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
    public RateLimitConfig RateLimit { get; set; } = new();
    public SecurityHeadersConfig Headers { get; set; } = new();
}

/// <summary>
/// Response security headers. All are opt-in (disabled by default) so existing apps
/// aren't broken by framework upgrades. Enable via config per header.
/// </summary>
public sealed class SecurityHeadersConfig
{
    /// <summary>Content-Security-Policy. When enabled, sent on every HTML response.</summary>
    public bool EnableCsp { get; set; } = false;

    /// <summary>
    /// Full CSP directive string. Default is restrictive: scripts/styles from self + inline
    /// (inline allowed because many apps rely on it — tighten per-app if you don't).
    /// </summary>
    public string CspDirectives { get; set; } =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    /// <summary>When true, sends Content-Security-Policy-Report-Only instead of enforcing.</summary>
    public bool CspReportOnly { get; set; } = false;

    /// <summary>Strict-Transport-Security. Only sent on HTTPS requests.</summary>
    public bool EnableHsts { get; set; } = false;
    public int HstsMaxAgeSeconds { get; set; } = 31536000; // 1 year
    public bool HstsIncludeSubdomains { get; set; } = true;
    public bool HstsPreload { get; set; } = false;

    /// <summary>X-Content-Type-Options: nosniff.</summary>
    public bool EnableContentTypeOptions { get; set; } = true;

    /// <summary>X-Frame-Options. Valid values: DENY, SAMEORIGIN.</summary>
    public bool EnableFrameOptions { get; set; } = true;
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>Referrer-Policy header value.</summary>
    public bool EnableReferrerPolicy { get; set; } = true;
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>Permissions-Policy header — restricts browser features.</summary>
    public bool EnablePermissionsPolicy { get; set; } = false;
    public string PermissionsPolicy { get; set; } =
        "camera=(), microphone=(), geolocation=(), interest-cohort=()";
}

public sealed class AuthConfig
{
    public bool AllowHeaderUserId { get; set; } = true;
    public bool AllowHeaderAccessGroups { get; set; } = true;
    public bool AllowSessionUserId { get; set; } = true;
    public bool AllowSessionAccessGroups { get; set; } = true;
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

public sealed class WidgetConfig
{
    public bool EnablePersistentSettings { get; set; } = true;
    public string SettingsFileName { get; set; } = "widget-settings.json";
}
