namespace CL.WebLogic.Security;

/// <summary>
/// HttpContext feature attached by the WebLogic session middleware. The auth
/// resolver, CSRF service, and realtime hub all read identity from here rather
/// than from ASP.NET's built-in session or request headers.
/// </summary>
public sealed class WebSessionFeature
{
    public required WebSessionRecord Session { get; init; }
}
