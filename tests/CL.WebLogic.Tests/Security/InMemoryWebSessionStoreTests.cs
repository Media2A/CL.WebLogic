using System.Security.Cryptography;
using CL.WebLogic.Security;
using Xunit;

namespace CL.WebLogic.Tests.Security;

/// <summary>
/// Pins the <see cref="IWebSessionStore"/> contract with a minimal in-memory
/// implementation. Acts as a reference for the real MySQL-backed store in
/// FragHunt.Shared, and as a regression guard on the interface shape the
/// middleware relies on.
/// </summary>
public sealed class InMemoryWebSessionStoreTests
{
    private static WebSessionCreate MakeCreate(
        string userId = "alice",
        bool rememberMe = false,
        int maxConcurrent = 3) =>
        new()
        {
            UserId = userId,
            AccessGroups = ["member"],
            RememberMe = rememberMe,
            MaxConcurrentSessions = maxConcurrent,
            IdleTimeout = TimeSpan.FromMinutes(120),
            RememberMeLifetime = TimeSpan.FromDays(30)
        };

    [Fact]
    public async Task Create_And_Get_RoundTripsSession()
    {
        var store = new InMemoryStore();
        var created = await store.CreateAsync(MakeCreate());

        var fetched = await store.GetAsync(created.Token);
        Assert.NotNull(fetched);
        Assert.Equal("alice", fetched!.UserId);
        Assert.Contains("member", fetched.AccessGroups);
        Assert.Equal(created.CsrfToken, fetched.CsrfToken);
        Assert.False(fetched.IsRememberMe);
    }

    [Fact]
    public async Task Get_UnknownToken_ReturnsNull()
    {
        var store = new InMemoryStore();
        Assert.Null(await store.GetAsync("never-issued"));
    }

    [Fact]
    public async Task Get_RevokedSession_ReturnsNull()
    {
        var store = new InMemoryStore();
        var created = await store.CreateAsync(MakeCreate());
        await store.RevokeAsync(created.Token);

        Assert.Null(await store.GetAsync(created.Token));
    }

    [Fact]
    public async Task RememberMe_UsesLongerLifetime()
    {
        var store = new InMemoryStore();
        var shortSession = await store.CreateAsync(MakeCreate(rememberMe: false));
        var longSession = await store.CreateAsync(MakeCreate(userId: "bob", rememberMe: true));

        Assert.True(longSession.ExpiresAtUtc - longSession.CreatedAtUtc > TimeSpan.FromDays(7));
        Assert.True(shortSession.ExpiresAtUtc - shortSession.CreatedAtUtc < TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task PerUserCap_EvictsOldestSession_OnOverflow()
    {
        var store = new InMemoryStore();
        var s1 = await store.CreateAsync(MakeCreate(maxConcurrent: 2));
        await Task.Delay(5);
        var s2 = await store.CreateAsync(MakeCreate(maxConcurrent: 2));
        await Task.Delay(5);
        var s3 = await store.CreateAsync(MakeCreate(maxConcurrent: 2));

        // s1 is the oldest, should be evicted.
        Assert.Null(await store.GetAsync(s1.Token));
        Assert.NotNull(await store.GetAsync(s2.Token));
        Assert.NotNull(await store.GetAsync(s3.Token));
    }

    [Fact]
    public async Task RevokeAllForUser_NukesAllOfThatUsersSessions_ButLeavesOthers()
    {
        var store = new InMemoryStore();
        var aliceA = await store.CreateAsync(MakeCreate(userId: "alice"));
        var aliceB = await store.CreateAsync(MakeCreate(userId: "alice"));
        var bob = await store.CreateAsync(MakeCreate(userId: "bob"));

        await store.RevokeAllForUserAsync("alice");

        Assert.Null(await store.GetAsync(aliceA.Token));
        Assert.Null(await store.GetAsync(aliceB.Token));
        Assert.NotNull(await store.GetAsync(bob.Token));
    }

    [Fact]
    public async Task SweepExpired_RemovesPastDueRows_AndLeavesFutureOnes()
    {
        var store = new InMemoryStore();
        var alive = await store.CreateAsync(MakeCreate());
        var dying = await store.CreateAsync(MakeCreate(userId: "stale"));

        store.ForceExpire(dying.Token, DateTime.UtcNow.AddMinutes(-1));
        var removed = await store.SweepExpiredAsync(DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.NotNull(await store.GetAsync(alive.Token));
        Assert.Null(await store.GetAsync(dying.Token));
    }

    [Fact]
    public async Task UpdateCsrfToken_ChangesStoredValue()
    {
        var store = new InMemoryStore();
        var created = await store.CreateAsync(MakeCreate());
        await store.UpdateCsrfTokenAsync(created.Token, "new-csrf-xyz");

        var fetched = await store.GetAsync(created.Token);
        Assert.Equal("new-csrf-xyz", fetched!.CsrfToken);
    }

    // Minimal reference implementation. Not production-quality (no hashing,
    // no concurrency guard) — exists to pin the contract.
    private sealed class InMemoryStore : IWebSessionStore
    {
        private readonly Dictionary<string, WebSessionRecord> _byToken = new(StringComparer.Ordinal);

        public Task<WebSessionRecord?> GetAsync(string sessionToken, CancellationToken cancellationToken = default)
        {
            _byToken.TryGetValue(sessionToken, out var record);
            if (record is not null && record.IsExpired(DateTime.UtcNow))
                return Task.FromResult<WebSessionRecord?>(null);
            return Task.FromResult(record);
        }

        public Task<WebSessionRecord> CreateAsync(WebSessionCreate input, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var lifetime = input.RememberMe ? input.RememberMeLifetime : input.IdleTimeout;
            var record = new WebSessionRecord
            {
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                UserId = input.UserId,
                AccessGroups = input.AccessGroups,
                CsrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                CreatedAtUtc = now,
                ExpiresAtUtc = now + lifetime,
                LastSeenAtUtc = now,
                IsRememberMe = input.RememberMe,
                IpHash = input.IpHash,
                UserAgentHash = input.UserAgentHash
            };

            var existing = _byToken.Values
                .Where(s => s.UserId == input.UserId)
                .OrderBy(s => s.CreatedAtUtc)
                .ToList();

            while (existing.Count >= input.MaxConcurrentSessions)
            {
                var toEvict = existing[0];
                _byToken.Remove(toEvict.Token);
                existing.RemoveAt(0);
            }

            _byToken[record.Token] = record;
            return Task.FromResult(record);
        }

        public Task TouchAsync(string sessionToken, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            if (_byToken.TryGetValue(sessionToken, out var record))
                _byToken[sessionToken] = record with { LastSeenAtUtc = nowUtc };
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string sessionToken, CancellationToken cancellationToken = default)
        {
            _byToken.Remove(sessionToken);
            return Task.CompletedTask;
        }

        public Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            var victims = _byToken.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
            foreach (var token in victims) _byToken.Remove(token);
            return Task.CompletedTask;
        }

        public Task<int> SweepExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var victims = _byToken.Where(kv => kv.Value.ExpiresAtUtc <= nowUtc).Select(kv => kv.Key).ToList();
            foreach (var token in victims) _byToken.Remove(token);
            return Task.FromResult(victims.Count);
        }

        public Task UpdateCsrfTokenAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken = default)
        {
            if (_byToken.TryGetValue(sessionToken, out var record))
                _byToken[sessionToken] = record with { CsrfToken = csrfToken };
            return Task.CompletedTask;
        }

        public void ForceExpire(string sessionToken, DateTime newExpiry)
        {
            if (_byToken.TryGetValue(sessionToken, out var record))
                _byToken[sessionToken] = record with { ExpiresAtUtc = newExpiry };
        }
    }
}
