namespace CL.WebLogic.Security;

/// <summary>
/// Resolves a user profile from a persistent store. Used by the DB-backed
/// session store at sign-in time to fetch canonical access groups for the
/// session row.
/// </summary>
public interface IWebIdentityStore
{
    Task<WebIdentityProfile?> GetIdentityAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Validates raw credentials and returns the resolved profile on success.</summary>
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
    public IReadOnlyCollection<string> Permissions { get; init; } = [];
}
