using System.Collections.Concurrent;
using CL.Common.Caching;
using CL.WebLogic.Routing;

namespace CL.WebLogic.Runtime;

/// <summary>
/// Two-layer in-process output cache. Backs both the route-level page cache
/// (driven by <see cref="WebOutputCachePolicy"/>) and the handler-level fragment
/// cache (<see cref="GetOrAddFragmentAsync{T}"/>). One backing
/// <see cref="ICache"/> with prefixed keys keeps invalidation, expiry, and
/// metrics unified.
/// </summary>
public sealed class WebOutputCache
{
    private const string PageKeyPrefix = "page:";
    private const string FragmentKeyPrefix = "frag:";

    private readonly ICache _backing;
    private readonly ConcurrentDictionary<string, Task<object?>> _inflight = new(StringComparer.Ordinal);

    public WebOutputCache(ICache backing)
    {
        _backing = backing ?? throw new ArgumentNullException(nameof(backing));
    }

    // ── Page cache ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the page cache key for a request that matched a policy-bearing route.
    /// </summary>
    public static string BuildPageKey(string path, WebOutputCachePolicy policy, IReadOnlyDictionary<string, string> query, string? userId)
    {
        var sb = new System.Text.StringBuilder(64);
        sb.Append(PageKeyPrefix).Append(path);

        if (policy.VaryByQuery.Length > 0)
        {
            sb.Append("|q:");
            // Sort vary keys for stable composition; missing keys contribute an empty value.
            var ordered = policy.VaryByQuery.OrderBy(k => k, StringComparer.Ordinal);
            var first = true;
            foreach (var key in ordered)
            {
                if (!first) sb.Append('&');
                first = false;
                sb.Append(key).Append('=');
                if (query.TryGetValue(key, out var value)) sb.Append(value);
            }
        }

        switch (policy.Scope)
        {
            case WebOutputCacheScope.PerUser:
                sb.Append("|u:").Append(string.IsNullOrEmpty(userId) ? "anon" : userId);
                break;
            case WebOutputCacheScope.Shared:
                sb.Append("|u:any");
                break;
            default:  // AnonymousOnly — eligibility check upstream filters auth out.
                sb.Append("|u:anon");
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Try to read a cached <see cref="WebResult"/> for the given key.
    /// Returns null on miss.
    /// </summary>
    public Task<WebResult?> TryGetPageAsync(string key) => _backing.GetAsync<WebResult>(key);

    /// <summary>Store a rendered <see cref="WebResult"/> under the given key.</summary>
    public Task SetPageAsync(string key, WebResult value, TimeSpan ttl) =>
        _backing.SetAsync(key, value, ttl);

    // ── Fragment cache ────────────────────────────────────────────────────────

    /// <summary>
    /// Get an HTML / data fragment from the cache, building it via
    /// <paramref name="factory"/> on miss. A single-flight guard ensures that a
    /// burst of requests after expiry triggers exactly one factory invocation per
    /// key — concurrent callers wait on the same task and share the result.
    /// </summary>
    /// <param name="key">
    /// App-supplied stable key. Include any vary axis you need (server id, user id,
    /// locale, …) so misses don't collide. The cache namespaces this internally.
    /// </param>
    /// <param name="ttl">How long to keep the built value.</param>
    /// <param name="factory">Builds the value on a cache miss.</param>
    /// <typeparam name="T">
    /// The fragment's runtime type. Reference types are recommended; for value
    /// types <c>default(T)</c> cannot be distinguished from "not present" through
    /// the underlying cache.
    /// </typeparam>
    public async Task<T> GetOrAddFragmentAsync<T>(
        string key, TimeSpan ttl, Func<Task<T>> factory)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var prefixed = FragmentKeyPrefix + key;

        var cached = await _backing.GetAsync<T>(prefixed).ConfigureAwait(false);
        if (cached is not null) return cached;

        var task = _inflight.GetOrAdd(prefixed, _ => BuildAsync(prefixed, ttl, factory));
        try
        {
            var result = await task.ConfigureAwait(false);
            return (T)result!;
        }
        finally
        {
            // Builders register a single-shot task; clear after completion so the
            // next miss (post-TTL) re-races cleanly.
            _inflight.TryRemove(prefixed, out _);
        }
    }

    private async Task<object?> BuildAsync<T>(string prefixedKey, TimeSpan ttl, Func<Task<T>> factory)
        where T : class
    {
        var value = await factory().ConfigureAwait(false);
        if (value is not null)
            await _backing.SetAsync(prefixedKey, value, ttl).ConfigureAwait(false);
        return value;
    }

    // ── Invalidation ──────────────────────────────────────────────────────────

    /// <summary>Remove a single page-cache entry by full key.</summary>
    public Task InvalidatePageAsync(string key) => _backing.RemoveAsync(key);

    /// <summary>
    /// Remove every page-cache entry whose key starts with the given path prefix.
    /// Pass the route path (e.g. <c>/servers</c>) to drop all variations of a page.
    /// </summary>
    public Task InvalidatePagesByPathPrefixAsync(string pathPrefix) =>
        _backing.RemoveByPrefixAsync(PageKeyPrefix + pathPrefix);

    /// <summary>Remove a single fragment by its app-supplied key.</summary>
    public Task InvalidateFragmentAsync(string key) =>
        _backing.RemoveAsync(FragmentKeyPrefix + key);

    /// <summary>Remove every fragment whose key starts with the given prefix.</summary>
    public Task InvalidateFragmentsByPrefixAsync(string keyPrefix) =>
        _backing.RemoveByPrefixAsync(FragmentKeyPrefix + keyPrefix);
}
