namespace CL.WebLogic.Security;

/// <summary>
/// Persistent session store used by the WebLogic session middleware, CSRF service,
/// and realtime hub. Implementations are expected to hash the client-visible token
/// before persistence (so a database compromise does not yield usable cookies) and
/// to enforce the per-user session cap (<see cref="WebSessionCreate.MaxConcurrentSessions"/>)
/// by evicting the oldest row on overflow.
/// </summary>
public interface IWebSessionStore
{
    /// <summary>
    /// Resolve a session by the client-visible token. Returns <c>null</c> when the
    /// token is unknown, expired, or has been revoked. Implementations must treat
    /// expiry checks against UTC.
    /// </summary>
    Task<WebSessionRecord?> GetAsync(string sessionToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new session row, evicting older rows for the same user when the
    /// per-user cap would be exceeded. Returns the persisted record — the caller
    /// writes <see cref="WebSessionRecord.Token"/> into the response cookie.
    /// </summary>
    Task<WebSessionRecord> CreateAsync(WebSessionCreate input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advance a session's sliding expiry and last-seen timestamp. Called on every
    /// request hit. A row that no longer exists (e.g. revoked concurrently) is a
    /// silent no-op; callers should separately treat a subsequent
    /// <see cref="GetAsync(string, CancellationToken)"/> miss as "log out".
    /// </summary>
    Task TouchAsync(string sessionToken, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Revoke a single session (logout on the current device).</summary>
    Task RevokeAsync(string sessionToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke every session for a user (global logout — used on password change,
    /// permission downgrade, or explicit "sign out everywhere").
    /// </summary>
    Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete rows whose <see cref="WebSessionRecord.ExpiresAtUtc"/> is in the past.
    /// Returns the number of rows deleted. Called from a background sweeper.
    /// </summary>
    Task<int> SweepExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist a new CSRF token on the session row without rotating the session
    /// itself. Used after sign-in to rebind the CSRF token to the new identity.
    /// </summary>
    Task UpdateCsrfTokenAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace the session's app-scoped metadata blob. The blob is an arbitrary
    /// string → string dictionary owned by the consuming app (e.g. display name,
    /// theme preference, timezone). The library reads it off the resolved
    /// session and exposes it as <c>WebRequestContext.SessionData</c>, but
    /// otherwise treats it as opaque.
    /// </summary>
    Task UpdateAppDataAsync(string sessionToken, IReadOnlyDictionary<string, string> appData, CancellationToken cancellationToken = default);
}

/// <summary>
/// A persisted session row. The <see cref="Token"/> is the raw, client-visible value;
/// implementations are expected to store only a hash of it server-side but return the
/// raw value to the caller of <see cref="IWebSessionStore.CreateAsync"/>.
/// </summary>
public sealed record WebSessionRecord
{
    /// <summary>Opaque 256-bit token. Placed verbatim in the browser cookie.</summary>
    public required string Token { get; init; }

    public required string UserId { get; init; }

    public required IReadOnlyList<string> AccessGroups { get; init; }

    /// <summary>Random 128-bit hex token bound to this session.</summary>
    public required string CsrfToken { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required DateTime LastSeenAtUtc { get; init; }

    /// <summary>True when the user ticked "remember me" at sign-in.</summary>
    public required bool IsRememberMe { get; init; }

    /// <summary>
    /// SHA-256 of the client IP at session creation. The middleware compares this
    /// against the current request IP and revokes the session on mismatch.
    /// </summary>
    public byte[]? IpHash { get; init; }

    public byte[]? UserAgentHash { get; init; }

    /// <summary>
    /// App-scoped key/value metadata for this session (display name, theme,
    /// timezone, etc.). Populated by the app at sign-in, updatable via
    /// <see cref="IWebSessionStore.UpdateAppDataAsync"/>. The library itself
    /// treats this as opaque.
    /// </summary>
    public IReadOnlyDictionary<string, string> AppData { get; init; } = EmptyAppData;

    /// <summary>Shared empty AppData instance; use as a sentinel for "no session metadata".</summary>
    public static readonly IReadOnlyDictionary<string, string> EmptyAppData =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;
}

/// <summary>Input to <see cref="IWebSessionStore.CreateAsync"/>.</summary>
public sealed record WebSessionCreate
{
    public required string UserId { get; init; }
    public required IReadOnlyList<string> AccessGroups { get; init; }
    public required bool RememberMe { get; init; }

    /// <summary>Hard cap on concurrent sessions per user. The store evicts the oldest row(s) when a new session would exceed this.</summary>
    public required int MaxConcurrentSessions { get; init; }

    /// <summary>Idle timeout for non-remember-me sessions.</summary>
    public required TimeSpan IdleTimeout { get; init; }

    /// <summary>Absolute lifetime for remember-me sessions.</summary>
    public required TimeSpan RememberMeLifetime { get; init; }

    public byte[]? IpHash { get; init; }
    public byte[]? UserAgentHash { get; init; }

    /// <summary>Initial app-scoped metadata for the session row. Defaults to empty.</summary>
    public IReadOnlyDictionary<string, string> AppData { get; init; } = WebSessionRecord.EmptyAppData;
}
