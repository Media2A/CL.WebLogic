using CL.WebLogic.Configuration;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CodeLogic.Core.Events;
using CodeLogic.Framework.Libraries;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CL.WebLogic.Tests.Security;

public sealed class WebSecurityRateLimitTests
{
    [Fact]
    public async Task AllowlistedIp_BypassesGlobalRateLimit_WhenDnsblIsDisabled()
    {
        var config = CreateConfig();
        config.Security.EnableDnsbl = false;
        config.Security.IpAllowlistResolver = _ => Task.FromResult(true);
        var service = CreateService(config);

        for (var requestNumber = 0; requestNumber < 5; requestNumber++)
            Assert.Null(await service.ValidateAsync(CreateRequest("203.0.113.10")));
    }

    [Fact]
    public async Task UntrustedIp_RemainsProtectedByGlobalRateLimit()
    {
        var config = CreateConfig();
        config.Security.IpAllowlistResolver = _ => Task.FromResult(false);
        var service = CreateService(config);

        Assert.Null(await service.ValidateAsync(CreateRequest("198.51.100.25")));
        var blocked = await service.ValidateAsync(CreateRequest("198.51.100.25"));

        Assert.NotNull(blocked);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.StatusCode);
    }

    private static WebLogicConfig CreateConfig()
    {
        var config = new WebLogicConfig();
        config.Security.EnforceHttps = false;
        config.Security.RateLimit.RequestsPerWindow = 1;
        config.Security.RateLimit.WindowSeconds = 60;
        return config;
    }

    private static WebSecurityService CreateService(WebLogicConfig config) =>
        new(
            new LibraryContext
            {
                LibraryId = "CL.WebLogic.Tests",
                LibraryDirectory = AppContext.BaseDirectory,
                ConfigDirectory = AppContext.BaseDirectory,
                LocalizationDirectory = AppContext.BaseDirectory,
                LogsDirectory = AppContext.BaseDirectory,
                DataDirectory = AppContext.BaseDirectory,
                Logger = null!,
                Configuration = null!,
                Localization = null!,
                Events = new EventBus()
            },
            config,
            sessionStore: null,
            permissionResolver: null);

    private static WebRequestContext CreateRequest(string clientIp) =>
        new()
        {
            HttpContext = new DefaultHttpContext(),
            Method = "GET",
            Path = "/api/stats/ingest",
            ClientIp = clientIp,
            UserAgent = "test",
            Headers = new Dictionary<string, string>(),
            Query = new Dictionary<string, string>(),
            Cookies = new Dictionary<string, string>(),
            OutputCache = null!,
            Session = new Dictionary<string, string>(),
            Identity = new WebRequestIdentity(null, null)
        };
}
