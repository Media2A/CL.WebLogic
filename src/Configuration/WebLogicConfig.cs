using System.ComponentModel.DataAnnotations;
using CodeLogic.Core.Configuration;

namespace CL.WebLogic.Configuration;

[ConfigSection("weblogic")]
public sealed class WebLogicConfig : ConfigModelBase
{
    [ConfigField(Label = "Enabled", Description = "Master switch for the WebLogic middleware.", Group = "General", Order = 0)]
    public bool Enabled { get; set; } = true;

    [ConfigField(Label = "Site Name", Description = "Displayed in page titles and emails.",
        Placeholder = "My Site", Group = "General", Order = 1)]
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
    [ConfigField(Label = "Source", Description = "Local folder or a Git repository.",
        RequiresRestart = true, Group = "Theme", Order = 10)]
    public ThemeSource Source { get; set; } = ThemeSource.Local;

    [ConfigField(Label = "Local Path", Description = "Folder under the app's data directory when Source=Local.",
        Placeholder = "theme", RequiresRestart = true, Group = "Theme", Order = 11)]
    public string LocalPath { get; set; } = "theme";

    [ConfigField(Label = "Repository ID", Description = "Stable identifier used to cache the cloned repo.",
        RequiresRestart = true, Group = "Theme", Order = 12, Collapsed = true)]
    public string RepositoryId { get; set; } = "weblogic-theme";

    [ConfigField(Label = "Repository URL", InputType = ConfigInputType.Url,
        Description = "HTTPS or SSH URL of the theme Git repo.",
        Placeholder = "https://github.com/org/theme.git",
        RequiresRestart = true, Group = "Theme", Order = 13)]
    public string RepositoryUrl { get; set; } = "";

    [ConfigField(Label = "Branch", RequiresRestart = true, Group = "Theme", Order = 14)]
    public string Branch { get; set; } = "main";

    [ConfigField(Label = "Auto Sync on Start", Description = "Pull the theme repo at startup.",
        Group = "Theme", Order = 15)]
    public bool AutoSyncOnStart { get; set; } = false;

    [ConfigField(Label = "Theme Sub-path", Description = "Folder inside the repo containing the theme.",
        RequiresRestart = true, Group = "Theme", Order = 16, Collapsed = true)]
    public string ThemeSubPath { get; set; } = "";

    [ConfigField(Label = "Enable Template Cache", Description = "Cache parsed templates in memory.",
        Group = "Theme", Order = 17, Collapsed = true)]
    public bool EnableCaching { get; set; } = true;
}

public enum ThemeSource
{
    Local,
    Git
}

public sealed class SecurityConfig
{
    [ConfigField(Label = "Enforce HTTPS", Description = "Redirect HTTP requests to HTTPS.",
        RequiresRestart = true, Group = "Security", Order = 20)]
    public bool EnforceHttps { get; set; } = false;

    [ConfigField(Label = "Trust Forwarded Headers", Description = "Honor X-Forwarded-* headers from trusted proxies.",
        RequiresRestart = true, Group = "Security", Order = 21)]
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

    [ConfigField(Label = "Enable DNSBL", Description = "Check incoming IPs against DNSBL services.",
        Group = "Security", Order = 22)]
    public bool EnableDnsbl { get; set; } = false;

    /// <summary>
    /// When true, the built-in explorer / demo-signin / widget-persistence
    /// debug endpoints under /api/weblogic/* are registered. They expose
    /// route listings, let anyone set a session user id via
    /// /api/weblogic/auth/demo-signin, and write widget/dashboard state
    /// with no auth — all fine for local dev, dangerous for production.
    /// Default <c>false</c> so production is safe out of the box.
    /// </summary>
    [ConfigField(Label = "Enable Explorer Routes", Description =
        "Development-only debug / demo endpoints under /api/weblogic/*. Leave OFF in production — /api/weblogic/auth/demo-signin otherwise lets any caller impersonate an arbitrary user id.",
        RequiresRestart = true, Group = "Security", Order = 25)]
    public bool EnableExplorerRoutes { get; set; } = false;

    [ConfigField(Label = "Enable CSRF Protection", Description = "Require CSRF tokens on state-changing requests.",
        Group = "Security", Order = 23)]
    public bool EnableCsrf { get; set; } = true;

    [ConfigField(Label = "Enable Response Compression", Group = "Security", Order = 24, Collapsed = true)]
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
    [ConfigField(Label = "Enable CSP", Description = "Send Content-Security-Policy on HTML responses.",
        Group = "CSP", Order = 30)]
    public bool EnableCsp { get; set; } = false;

    /// <summary>
    /// Full CSP directive string. Default is restrictive: scripts/styles from self + inline
    /// (inline allowed because many apps rely on it — tighten per-app if you don't).
    /// </summary>
    [ConfigField(Label = "CSP Directives", InputType = ConfigInputType.Textarea,
        Description = "Full CSP directive string (semicolon-separated).",
        Group = "CSP", Order = 31, Collapsed = true)]
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
    [ConfigField(Label = "CSP Report-Only", Description = "Log violations instead of blocking.",
        Group = "CSP", Order = 32, Collapsed = true)]
    public bool CspReportOnly { get; set; } = false;

    /// <summary>Strict-Transport-Security. Only sent on HTTPS requests.</summary>
    [ConfigField(Label = "Enable HSTS", Description = "Send Strict-Transport-Security on HTTPS responses.",
        Group = "HSTS", Order = 40)]
    public bool EnableHsts { get; set; } = false;

    [ConfigField(Label = "HSTS Max-Age (s)", Min = 0,
        Description = "Default: 31536000 (1 year).", Group = "HSTS", Order = 41, Collapsed = true)]
    public int HstsMaxAgeSeconds { get; set; } = 31536000; // 1 year

    [ConfigField(Label = "HSTS Include Subdomains", Group = "HSTS", Order = 42, Collapsed = true)]
    public bool HstsIncludeSubdomains { get; set; } = true;

    [ConfigField(Label = "HSTS Preload", Description = "Requires submission to the HSTS preload list.",
        Group = "HSTS", Order = 43, Collapsed = true)]
    public bool HstsPreload { get; set; } = false;

    /// <summary>X-Content-Type-Options: nosniff.</summary>
    [ConfigField(Label = "X-Content-Type-Options: nosniff",
        Group = "Response Headers", Order = 50, Collapsed = true)]
    public bool EnableContentTypeOptions { get; set; } = true;

    /// <summary>X-Frame-Options. Valid values: DENY, SAMEORIGIN.</summary>
    [ConfigField(Label = "Enable X-Frame-Options", Group = "Response Headers", Order = 51, Collapsed = true)]
    public bool EnableFrameOptions { get; set; } = true;

    [ConfigField(Label = "X-Frame-Options value", AllowedValues = "DENY,SAMEORIGIN",
        Group = "Response Headers", Order = 52, Collapsed = true)]
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>Referrer-Policy header value.</summary>
    [ConfigField(Label = "Enable Referrer-Policy", Group = "Response Headers", Order = 53, Collapsed = true)]
    public bool EnableReferrerPolicy { get; set; } = true;

    [ConfigField(Label = "Referrer-Policy value", Group = "Response Headers", Order = 54, Collapsed = true)]
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>Permissions-Policy header — restricts browser features.</summary>
    [ConfigField(Label = "Enable Permissions-Policy",
        Description = "Restrict browser features (camera, mic, geolocation, etc.).",
        Group = "Response Headers", Order = 55, Collapsed = true)]
    public bool EnablePermissionsPolicy { get; set; } = false;

    [ConfigField(Label = "Permissions-Policy value", InputType = ConfigInputType.Textarea,
        Group = "Response Headers", Order = 56, Collapsed = true)]
    public string PermissionsPolicy { get; set; } =
        "camera=(), microphone=(), geolocation=()";
}

public sealed class AuthConfig
{
    [ConfigField(Label = "Allow Header User ID", Description = "Read user ID from custom header (dev/testing).",
        Group = "Auth", Order = 60, Collapsed = true)]
    public bool AllowHeaderUserId { get; set; } = true;

    [ConfigField(Label = "Allow Header Access Groups", Description = "Read access groups from custom header.",
        Group = "Auth", Order = 61, Collapsed = true)]
    public bool AllowHeaderAccessGroups { get; set; } = true;

    [ConfigField(Label = "Allow Session User ID", Description = "Read user ID from session (standard).",
        Group = "Auth", Order = 62, Collapsed = true)]
    public bool AllowSessionUserId { get; set; } = true;

    [ConfigField(Label = "Allow Session Access Groups", Group = "Auth", Order = 63, Collapsed = true)]
    public bool AllowSessionAccessGroups { get; set; } = true;
}

public sealed class RateLimitConfig
{
    [Range(1, 10000)]
    [ConfigField(Label = "Requests per Window", Min = 1, Max = 10000,
        Description = "How many requests one IP can make per window before being throttled.",
        Group = "Rate Limit", Order = 70)]
    public int RequestsPerWindow { get; set; } = 120;

    [Range(1, 3600)]
    [ConfigField(Label = "Window Seconds", Min = 1, Max = 3600,
        Description = "Duration of the rate-limit sliding window.",
        Group = "Rate Limit", Order = 71)]
    public int WindowSeconds { get; set; } = 60;
}

public sealed class StorageConfig
{
    [ConfigField(Label = "Storage Mode", Description = "Where static/uploaded files live.",
        RequiresRestart = true, Group = "Storage", Order = 80)]
    public WebStorageMode Mode { get; set; } = WebStorageMode.Local;

    [ConfigField(Label = "Local Root", Description = "Folder under data directory for local storage.",
        RequiresRestart = true, Group = "Storage", Order = 81)]
    public string LocalRoot { get; set; } = "theme";

    [ConfigField(Label = "S3 Connection ID", Description = "Matches a connection registered with CL.StorageS3.",
        RequiresRestart = true, Group = "Storage", Order = 82)]
    public string S3ConnectionId { get; set; } = "Default";

    [ConfigField(Label = "S3 Bucket", Placeholder = "my-app-bucket",
        RequiresRestart = true, Group = "Storage", Order = 83)]
    public string S3Bucket { get; set; } = "";

    [ConfigField(Label = "S3 Key Prefix", Description = "Prepended to every key (for shared buckets).",
        Group = "Storage", Order = 84, Collapsed = true)]
    public string S3Prefix { get; set; } = "";
}

public enum WebStorageMode
{
    Local,
    S3
}

public sealed class WidgetConfig
{
    [ConfigField(Label = "Persist Widget Settings",
        Description = "Save per-widget preferences to disk.",
        Group = "Widgets", Order = 90, Collapsed = true)]
    public bool EnablePersistentSettings { get; set; } = true;

    [ConfigField(Label = "Settings File Name", Group = "Widgets", Order = 91, Collapsed = true)]
    public string SettingsFileName { get; set; } = "widget-settings.json";
}
