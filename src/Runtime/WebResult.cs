using System.Text.Json;

namespace CL.WebLogic.Runtime;

public sealed class WebResult
{
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "text/plain; charset=utf-8";
    public string? TextBody { get; init; }
    public byte[]? BinaryBody { get; init; }
    public string? TemplatePath { get; init; }
    public IReadOnlyDictionary<string, object?>? Model { get; init; }
    public WebPageMeta? Meta { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? ThemeRoot { get; init; }

    public static WebResult Html(string html, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html; charset=utf-8",
        TextBody = html
    };

    public static WebResult Text(string text, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/plain; charset=utf-8",
        TextBody = text
    };

    public static WebResult Json(object payload, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "application/json; charset=utf-8",
        TextBody = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
    };

    public static WebResult Bytes(byte[] data, string contentType, int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = contentType,
        BinaryBody = data
    };

    public static WebResult Template(
        string templatePath,
        IReadOnlyDictionary<string, object?>? model = null,
        WebPageMeta? meta = null,
        int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html; charset=utf-8",
        TemplatePath = templatePath,
        Model = model,
        Meta = meta
    };

    public static WebResult Document(WebPageDocument document) => new()
    {
        StatusCode = document.StatusCode,
        ContentType = "text/html; charset=utf-8",
        TemplatePath = document.TemplatePath,
        Model = document.Model,
        Meta = document.Meta,
        ThemeRoot = document.ThemeRoot
    };

    /// <summary>
    /// Returns a JSON response with server commands for the WebLogic client to process.
    /// Commands: toast, redirect, navigate, reload, replace, remove, addClass, removeClass.
    /// </summary>
    public static WebResult Commands(params object[] commands) => Json(new
    {
        success = true,
        commands
    });

    public static object ToastCommand(string message, string variant = "success") =>
        new { type = "toast", message, variant };

    public static object RedirectCommand(string url) =>
        new { type = "redirect", url };

    public static object NavigateCommand(string url) =>
        new { type = "navigate", url };

    public static object ReloadCommand() =>
        new { type = "reload" };

    public static object ReplaceCommand(string selector, string html) =>
        new { type = "replace", selector, html };

    public static object RemoveCommand(string selector) =>
        new { type = "remove", selector };

    public static object OverlayCommand(string title, string message, string variant = "success", int duration = 2000) =>
        new { type = "overlay", title, message, variant, duration };
}
