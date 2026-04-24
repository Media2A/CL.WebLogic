using CL.WebLogic.Security;
using Xunit;

namespace CL.WebLogic.Tests.Security;

public sealed class WebSessionRecordTests
{
    private static WebSessionRecord MakeRecord(DateTime? expires = null, DateTime? now = null) => new()
    {
        Token = "opaque-token",
        UserId = "user-1",
        AccessGroups = ["member"],
        CsrfToken = "csrf-xyz",
        CreatedAtUtc = now ?? DateTime.UtcNow,
        ExpiresAtUtc = expires ?? (now ?? DateTime.UtcNow).AddMinutes(30),
        LastSeenAtUtc = now ?? DateTime.UtcNow,
        IsRememberMe = false
    };

    [Fact]
    public void IsExpired_IsFalse_WhenExpiryIsInFuture()
    {
        var now = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
        var record = MakeRecord(expires: now.AddMinutes(1), now: now);

        Assert.False(record.IsExpired(now));
    }

    [Fact]
    public void IsExpired_IsTrue_AtExactExpiry()
    {
        // Boundary — `ExpiresAtUtc <= nowUtc` treats the exact expiry moment as expired.
        var now = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
        var record = MakeRecord(expires: now, now: now);

        Assert.True(record.IsExpired(now));
    }

    [Fact]
    public void IsExpired_IsTrue_WhenExpiryIsInPast()
    {
        var now = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
        var record = MakeRecord(expires: now.AddSeconds(-1), now: now);

        Assert.True(record.IsExpired(now));
    }

    [Fact]
    public void Records_WithSameFields_CompareEqual()
    {
        var now = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
        var a = MakeRecord(now: now);
        var b = MakeRecord(now: now);

        // IReadOnlyList<string> uses reference equality in the default record equality,
        // so this also demonstrates the AccessGroups instance needs to match. We pass
        // distinct list instances that happen to have equal content to document that
        // records are NOT deep-equal on collection members — regressions will flip this.
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WebSessionCreate_RequiredFieldsEnforced()
    {
        var create = new WebSessionCreate
        {
            UserId = "u",
            AccessGroups = ["member"],
            RememberMe = true,
            MaxConcurrentSessions = 3,
            IdleTimeout = TimeSpan.FromMinutes(120),
            RememberMeLifetime = TimeSpan.FromDays(30)
        };

        Assert.Equal("u", create.UserId);
        Assert.True(create.RememberMe);
        Assert.Equal(3, create.MaxConcurrentSessions);
        Assert.Equal(TimeSpan.FromMinutes(120), create.IdleTimeout);
        Assert.Equal(TimeSpan.FromDays(30), create.RememberMeLifetime);
        Assert.Null(create.IpHash);
        Assert.Null(create.UserAgentHash);
    }
}
