using CodeLogic.Core.Events;

namespace CL.WebLogic.Runtime;

public sealed record WebRequestHandledEvent(
    string Method,
    string Path,
    string ClientIp,
    int StatusCode,
    long DurationMs) : IEvent;

public sealed record WebRequestBlockedEvent(
    string Method,
    string Path,
    string ClientIp,
    int StatusCode,
    string Reason) : IEvent;

public sealed record ThemeSynchronizedEvent(
    string Source,
    string ThemeRoot) : IEvent;
