namespace CL.WebLogic.Runtime;

/// <summary>
/// Delegate representing the next step in the middleware pipeline.
/// Call this to pass control to the next middleware or the route handler.
/// </summary>
public delegate Task<WebResult> WebMiddlewareNext();

/// <summary>
/// Middleware that can inspect, modify, or short-circuit a request
/// before it reaches the route handler.
/// </summary>
public interface IWebMiddleware
{
    /// <summary>
    /// Process the request. Call <paramref name="next"/> to continue the pipeline,
    /// or return a <see cref="WebResult"/> directly to short-circuit.
    /// </summary>
    Task<WebResult> InvokeAsync(WebRequestContext context, WebMiddlewareNext next);
}

/// <summary>
/// Convenience base class for middleware that wraps a simple delegate.
/// </summary>
public sealed class WebDelegateMiddleware : IWebMiddleware
{
    private readonly Func<WebRequestContext, WebMiddlewareNext, Task<WebResult>> _handler;

    public WebDelegateMiddleware(Func<WebRequestContext, WebMiddlewareNext, Task<WebResult>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Task<WebResult> InvokeAsync(WebRequestContext context, WebMiddlewareNext next) =>
        _handler(context, next);
}
