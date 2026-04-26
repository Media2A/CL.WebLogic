using CL.WebLogic.Runtime;
using Xunit;

namespace CL.WebLogic.Tests.Runtime;

public sealed class WebRequestIdentityTests
{
    [Fact]
    public void HasPermission_ExactKey_Matches()
    {
        var id = new WebRequestIdentity("u1", ["member"], ["news.manage"]);
        Assert.True(id.HasPermission("news.manage"));
        Assert.True(id.HasPermission("NEWS.MANAGE"));
        Assert.False(id.HasPermission("news.delete"));
    }

    [Fact]
    public void HasPermission_StarWildcard_MatchesEverything()
    {
        var id = new WebRequestIdentity("u1", ["admin"], ["*"]);
        Assert.True(id.HasPermission("admin.access"));
        Assert.True(id.HasPermission("anything.at.all"));
    }

    [Fact]
    public void HasPermission_PrefixGlob_MatchesSegment()
    {
        var id = new WebRequestIdentity("u1", ["admin"], ["admin.*"]);
        Assert.True(id.HasPermission("admin.settings"));
        Assert.True(id.HasPermission("admin.security"));
        Assert.False(id.HasPermission("news.manage"));
    }

    [Fact]
    public void HasPermission_EmptyOrNullPermission_IsFalse()
    {
        var id = new WebRequestIdentity("u1", ["admin"], ["*"]);
        Assert.False(id.HasPermission(""));
        Assert.False(id.HasPermission("   "));
    }

    [Fact]
    public void HasPermission_AnonymousIdentity_IsFalse()
    {
        var id = new WebRequestIdentity(null, null, null);
        Assert.False(id.HasPermission("news.manage"));
    }
}
