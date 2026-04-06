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
        int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        ContentType = "text/html; charset=utf-8",
        TemplatePath = templatePath,
        Model = model
    };
}
