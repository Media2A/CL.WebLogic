using System.Net;
using System.Text;

namespace CL.WebLogic.Runtime;

public sealed class WebPageDocument
{
    public string TemplatePath { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?>? Model { get; init; }
    public WebPageMeta? Meta { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? ThemeRoot { get; init; }
}

public sealed class WebPageMeta
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? Robots { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string? Language { get; init; }
    public IReadOnlyList<WebPageAlternateLanguage> Alternates { get; init; } = [];
    public WebOpenGraphMeta? OpenGraph { get; init; }
    public WebTwitterMeta? Twitter { get; init; }
}

public sealed class WebPageAlternateLanguage
{
    public string Language { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class WebOpenGraphMeta
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Type { get; init; }
    public string? Url { get; init; }
    public string? Image { get; init; }
    public string? SiteName { get; init; }
    public string? Locale { get; init; }
}

public sealed class WebTwitterMeta
{
    public string? Card { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Image { get; init; }
    public string? Site { get; init; }
    public string? Creator { get; init; }
}

internal static class WebPageHeadRenderer
{
    public static string Render(WebPageMeta? meta, IReadOnlyDictionary<string, object?>? model)
    {
        var effectiveMeta = meta ?? new WebPageMeta();
        var title = FirstNonEmpty(effectiveMeta.Title, GetModelValue(model, "page_title"));
        var description = FirstNonEmpty(effectiveMeta.Description, GetModelValue(model, "page_description"));

        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
            builder.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");

        if (!string.IsNullOrWhiteSpace(description))
            builder.AppendLine($"""<meta name="description" content="{WebUtility.HtmlEncode(description)}">""");

        if (!string.IsNullOrWhiteSpace(effectiveMeta.Robots))
            builder.AppendLine($"""<meta name="robots" content="{WebUtility.HtmlEncode(effectiveMeta.Robots)}">""");

        if (effectiveMeta.Keywords.Count > 0)
            builder.AppendLine($"""<meta name="keywords" content="{WebUtility.HtmlEncode(string.Join(", ", effectiveMeta.Keywords))}">""");

        if (!string.IsNullOrWhiteSpace(effectiveMeta.CanonicalUrl))
            builder.AppendLine($"""<link rel="canonical" href="{WebUtility.HtmlEncode(effectiveMeta.CanonicalUrl)}">""");

        if (!string.IsNullOrWhiteSpace(effectiveMeta.Language))
            builder.AppendLine($"""<meta name="language" content="{WebUtility.HtmlEncode(effectiveMeta.Language)}">""");

        foreach (var alternate in effectiveMeta.Alternates.Where(static item =>
                     !string.IsNullOrWhiteSpace(item.Language) && !string.IsNullOrWhiteSpace(item.Url)))
        {
            builder.AppendLine($"""<link rel="alternate" hreflang="{WebUtility.HtmlEncode(alternate.Language)}" href="{WebUtility.HtmlEncode(alternate.Url)}">""");
        }

        AppendProperty(builder, "og:title", FirstNonEmpty(effectiveMeta.OpenGraph?.Title, title));
        AppendProperty(builder, "og:description", FirstNonEmpty(effectiveMeta.OpenGraph?.Description, description));
        AppendProperty(builder, "og:type", effectiveMeta.OpenGraph?.Type);
        AppendProperty(builder, "og:url", FirstNonEmpty(effectiveMeta.OpenGraph?.Url, effectiveMeta.CanonicalUrl));
        AppendProperty(builder, "og:image", effectiveMeta.OpenGraph?.Image);
        AppendProperty(builder, "og:site_name", effectiveMeta.OpenGraph?.SiteName);
        AppendProperty(builder, "og:locale", FirstNonEmpty(effectiveMeta.OpenGraph?.Locale, effectiveMeta.Language));

        AppendName(builder, "twitter:card", effectiveMeta.Twitter?.Card);
        AppendName(builder, "twitter:title", FirstNonEmpty(effectiveMeta.Twitter?.Title, title));
        AppendName(builder, "twitter:description", FirstNonEmpty(effectiveMeta.Twitter?.Description, description));
        AppendName(builder, "twitter:image", FirstNonEmpty(effectiveMeta.Twitter?.Image, effectiveMeta.OpenGraph?.Image));
        AppendName(builder, "twitter:site", effectiveMeta.Twitter?.Site);
        AppendName(builder, "twitter:creator", effectiveMeta.Twitter?.Creator);

        return builder.ToString().Trim();
    }

    private static void AppendProperty(StringBuilder builder, string property, string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
            builder.AppendLine($"""<meta property="{WebUtility.HtmlEncode(property)}" content="{WebUtility.HtmlEncode(content)}">""");
    }

    private static void AppendName(StringBuilder builder, string name, string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
            builder.AppendLine($"""<meta name="{WebUtility.HtmlEncode(name)}" content="{WebUtility.HtmlEncode(content)}">""");
    }

    private static string? GetModelValue(IReadOnlyDictionary<string, object?>? model, string key)
    {
        if (model is null || !model.TryGetValue(key, out var value))
            return null;

        return value?.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
