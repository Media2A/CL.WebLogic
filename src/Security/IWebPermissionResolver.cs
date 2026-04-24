namespace CL.WebLogic.Security;

/// <summary>
/// Resolves the effective permission set for a user. The WebLogic auth pipeline
/// calls <see cref="GetPermissionsAsync"/> on every request that reaches a route
/// with a <c>RequiredPermission</c> gate, so implementations are expected to cache
/// in process and invalidate via <see cref="InvalidateAsync"/> when the underlying
/// grants change.
/// </summary>
public interface IWebPermissionResolver
{
    /// <summary>
    /// Return the set of permission tokens the user currently holds. The set may
    /// include wildcards (<c>*</c>, <c>admin.*</c>) — the gate logic treats this
    /// opaquely, so implementations own the expansion.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drop any cached grants for <paramref name="userId"/>. Called by the app
    /// when roles / permissions change for that user.
    /// </summary>
    Task InvalidateAsync(string userId, CancellationToken cancellationToken = default);
}
