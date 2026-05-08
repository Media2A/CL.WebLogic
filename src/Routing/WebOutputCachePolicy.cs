namespace CL.WebLogic.Routing;

/// <summary>
/// Who shares a cache entry. Picked carefully — mis-keyed personalised content
/// is the failure mode this layer most needs to avoid, so the default is the
/// most defensive option.
/// </summary>
public enum WebOutputCacheScope
{
    /// <summary>
    /// Anonymous requests share one cache entry. Authenticated requests bypass
    /// the cache entirely — neither read nor write. The defensive default; pick
    /// this when the page might have a per-user element (a navbar, a banner, a
    /// "logged in as …" line) that anonymous and authenticated visitors should
    /// not see swapped.
    /// </summary>
    AnonymousOnly,

    /// <summary>
    /// One cache entry shared by every visitor regardless of auth. Pick this only
    /// when the rendered HTML genuinely contains nothing that varies by user —
    /// no greeting, no per-user CTA, no auth-conditional UI. Good for purely
    /// data-driven pages (server browsers, news indexes).
    /// </summary>
    Shared,

    /// <summary>
    /// Every authenticated user gets their own cache entry; anonymous traffic
    /// gets a single shared entry. Use sparingly — it scales the cache by user
    /// count.
    /// </summary>
    PerUser
}

/// <summary>
/// Per-route output-caching policy. Set via <see cref="WebRouteOptions.OutputCache"/>
/// on a page or API registration to opt the route into rendered-response caching.
/// </summary>
/// <remarks>
/// <para>The runtime caches the full <c>WebResult</c> returned by the handler chain.
/// On a cache hit the handler does not run at all — middleware after the cache
/// layer is also skipped. Caching is intentionally conservative:</para>
/// <list type="bullet">
/// <item>Only <c>GET</c> requests are eligible.</item>
/// <item>Only <c>200 OK</c> responses with no <c>Set-Cookie</c> header are stored.</item>
/// <item>The default <see cref="Scope"/> is
/// <see cref="WebOutputCacheScope.AnonymousOnly"/>: authenticated requests bypass
/// the cache unless you explicitly broaden the scope.</item>
/// </list>
/// </remarks>
public sealed class WebOutputCachePolicy
{
    /// <summary>How long a rendered response stays cached. Required.</summary>
    public required TimeSpan Ttl { get; init; }

    /// <summary>
    /// Query-string keys that distinguish cache entries. Other keys are ignored,
    /// so tracking parameters like <c>?utm_source=…</c> collapse with the
    /// canonical hit. Empty array = path-only key (still scoped by
    /// <see cref="Scope"/>).
    /// </summary>
    public string[] VaryByQuery { get; init; } = [];

    /// <summary>
    /// Who shares this cache entry. Defaults to
    /// <see cref="WebOutputCacheScope.AnonymousOnly"/> — authenticated requests
    /// bypass. Override to <see cref="WebOutputCacheScope.Shared"/> only when
    /// the page genuinely has no per-user content.
    /// </summary>
    public WebOutputCacheScope Scope { get; init; } = WebOutputCacheScope.AnonymousOnly;

    /// <summary>
    /// When true (default), a <c>Cache-Control: public, max-age={ttl}</c> header
    /// is added to the cached response so an upstream CDN / browser can also
    /// cache it. Disabled automatically when <see cref="Scope"/> is
    /// <see cref="WebOutputCacheScope.PerUser"/>.
    /// </summary>
    public bool SetClientCacheHeaders { get; init; } = true;
}
