using Microsoft.Extensions.DependencyInjection;

namespace CL.WebLogic.Runtime;

public interface IWebPageScript
{
    Task<WebResult> ExecuteAsync(WebPageScriptContext context);
}

public sealed class WebPageScriptContext
{
    public required WebRequestContext Request { get; init; }

    public WebResult Template(
        string templatePath,
        IReadOnlyDictionary<string, object?>? model = null,
        WebPageMeta? meta = null,
        int statusCode = 200) =>
        WebResult.Template(templatePath, model, meta, statusCode);

    public WebResult Document(
        string templatePath,
        IReadOnlyDictionary<string, object?>? model = null,
        WebPageMeta? meta = null,
        int statusCode = 200) =>
        WebResult.Document(new WebPageDocument
        {
            TemplatePath = templatePath,
            Model = model,
            Meta = meta,
            StatusCode = statusCode
        });

    public WebResult Html(string html, int statusCode = 200) =>
        WebResult.Html(html, statusCode);

    public WebResult Json(object payload, int statusCode = 200) =>
        WebResult.Json(payload, statusCode);

    public WebResult Text(string text, int statusCode = 200) =>
        WebResult.Text(text, statusCode);

    public T? GetService<T>() where T : class =>
        Request.GetService<T>();

    public T GetRequiredService<T>() where T : notnull =>
        Request.GetRequiredService<T>();
}

internal static class WebPageScriptExecutor
{
    public static async Task<WebResult> ExecuteAsync<TScript>(WebRequestContext request)
        where TScript : class, IWebPageScript
    {
        var script = ActivatorUtilities.CreateInstance<TScript>(request.HttpContext.RequestServices);
        return await script.ExecuteAsync(new WebPageScriptContext
        {
            Request = request
        }).ConfigureAwait(false);
    }
}
