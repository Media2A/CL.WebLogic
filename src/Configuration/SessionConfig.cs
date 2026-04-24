using CodeLogic.Core.Configuration;

namespace CL.WebLogic.Configuration;

/// <summary>
/// Session-cookie and DB-backed session behavior. Consumed by the WebLogic
/// session middleware, the DB-backed <c>IWebSessionStore</c> implementation,
/// and the session sweeper.
/// </summary>
public sealed class SessionConfig
{
    [ConfigField(Label = "Cookie Name", Description = "Name of the opaque session cookie.",
        Group = "Session", Order = 80)]
    public string CookieName { get; set; } = "fh_sid";

    [ConfigField(Label = "Cookie Domain", Description = "Domain scope for the session cookie. Leading '.' applies to all subdomains (e.g. '.fraghunt.eu'). Leave blank for host-only.",
        Placeholder = ".fraghunt.eu", Group = "Session", Order = 81)]
    public string CookieDomain { get; set; } = "";

    [ConfigField(Label = "Cookie Secure", Description = "Mark the session cookie as Secure (HTTPS-only). Should be true in production.",
        Group = "Session", Order = 82)]
    public bool CookieSecure { get; set; } = true;

    [ConfigField(Label = "Cookie SameSite", Description = "SameSite policy — Lax is the right default for session cookies.",
        Group = "Session", Order = 83)]
    public SessionSameSite CookieSameSite { get; set; } = SessionSameSite.Lax;

    [ConfigField(Label = "Idle Timeout (minutes)", Description = "Inactive sliding window before a non-remember-me session expires.",
        Min = 5, Max = 1440, Group = "Session", Order = 84)]
    public int IdleTimeoutMinutes { get; set; } = 120;

    [ConfigField(Label = "Remember-Me (days)", Description = "Absolute lifetime for sessions created with 'remember me' ticked.",
        Min = 1, Max = 365, Group = "Session", Order = 85)]
    public int RememberMeDays { get; set; } = 30;

    [ConfigField(Label = "Max Concurrent Sessions", Description = "Hard cap per user. Creating a new session beyond this evicts the oldest row.",
        Min = 1, Max = 50, Group = "Session", Order = 86)]
    public int MaxConcurrentSessions { get; set; } = 3;

    [ConfigField(Label = "Bind to Client IP", Description = "Reject a session if the request IP differs from the one seen at sign-in. Strong anti-hijack but breaks mobile-network roaming.",
        Group = "Session", Order = 87)]
    public bool BindToClientIp { get; set; } = true;

    [ConfigField(Label = "Sweep Interval (minutes)", Description = "How often the background sweeper deletes expired session rows.",
        Min = 1, Max = 1440, Group = "Session", Order = 88, Collapsed = true)]
    public int SweepIntervalMinutes { get; set; } = 15;
}

public enum SessionSameSite
{
    Lax,
    Strict,
    None
}
