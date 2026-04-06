using System.Security.Claims;
using CL.WebLogic.Configuration;
using CL.WebLogic.Runtime;
using Microsoft.AspNetCore.Http;

namespace CL.WebLogic.Security;

public interface IWebAuthResolver
{
    Task<WebRequestIdentity> ResolveIdentityAsync(HttpContext httpContext);
}

public interface IWebIdentityStore
{
    Task<WebIdentityProfile?> GetIdentityAsync(string userId, CancellationToken cancellationToken = default);
}

public interface IWebCredentialValidator
{
    Task<WebIdentityProfile?> ValidateCredentialsAsync(
        string userId,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class WebIdentitySeed
{
    public required string UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public required string Password { get; init; }
    public IReadOnlyCollection<string> AccessGroups { get; init; } = [];
    public bool IsActive { get; init; } = true;
}

public sealed class WebIdentityProfile
{
    public required string UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public IReadOnlyCollection<string> AccessGroups { get; init; } = [];
}

public sealed class DefaultWebAuthResolver : IWebAuthResolver
{
    private readonly AuthConfig _config;
    private readonly IWebIdentityStore? _identityStore;

    public DefaultWebAuthResolver(AuthConfig config, IWebIdentityStore? identityStore = null)
    {
        _config = config;
        _identityStore = identityStore;
    }

    public async Task<WebRequestIdentity> ResolveIdentityAsync(HttpContext httpContext)
    {
        var accessGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in httpContext.User.Claims)
        {
            if (claim.Type is ClaimTypes.Role or ClaimTypes.GroupSid or "role" or "roles" or "group" or "groups" or "access_group")
                AddValues(accessGroups, claim.Value);
        }

        if (_config.AllowSessionAccessGroups)
            AddValues(accessGroups, httpContext.Session.GetString("weblogic.access_groups"));

        if (_config.AllowHeaderAccessGroups)
            AddValues(accessGroups, httpContext.Request.Headers["X-WebLogic-AccessGroups"].ToString());

        var userId =
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            httpContext.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId) && _config.AllowSessionUserId)
            userId = httpContext.Session.GetString("weblogic.user_id");

        if (string.IsNullOrWhiteSpace(userId) && _config.AllowHeaderUserId)
            userId = httpContext.Request.Headers["X-WebLogic-UserId"].ToString();

        if (!string.IsNullOrWhiteSpace(userId) && _identityStore is not null)
        {
            var profile = await _identityStore.GetIdentityAsync(userId).ConfigureAwait(false);
            if (profile is not null && profile.IsActive)
            {
                userId = profile.UserId;
                foreach (var group in profile.AccessGroups)
                    accessGroups.Add(group);
            }
        }

        return new WebRequestIdentity(userId, accessGroups);
    }

    private static void AddValues(ISet<string> values, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        foreach (var part in raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            values.Add(part);
    }
}
