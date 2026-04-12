using CL.WebLogic;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CL.MySQL2.Models;
using CodeLogic;
using CodeLogic.Framework.Libraries;
using MiniBlog.Shared;
using MiniBlog.Shared.Infrastructure;
using MiniBlog.Shared.Models;
using MiniBlog.Shared.Services;
using System.Text;

namespace MiniBlog;

public sealed partial class MiniBlogApplication
{
    private MiniBlogDataService DataService => new(_config.ConnectionId);

    private async Task<WebIdentityProfile?> ValidateLoginAsync(string userId, string password)
    {
        var web = WebLogicLibrary.GetRequired();
        if (web.IdentityStore is IWebCredentialValidator validator)
        {
            var identity = await validator.ValidateCredentialsAsync(userId, password).ConfigureAwait(false);
            if (identity is not null)
                return identity;

            var matchedUser = MiniBlogDemoData.Users.FirstOrDefault(user =>
                string.Equals(user.Handle, userId, StringComparison.OrdinalIgnoreCase));

            if (matchedUser is not null)
                return await validator.ValidateCredentialsAsync(matchedUser.UserId, password).ConfigureAwait(false);
        }

        var fallback = MiniBlogDemoData.Users.FirstOrDefault(user =>
            string.Equals(user.Handle, userId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase));

        return fallback is not null && string.Equals(fallback.Password, password, StringComparison.Ordinal)
            ? new WebIdentityProfile
            {
                UserId = fallback.UserId,
                DisplayName = fallback.DisplayName,
                IsActive = true,
                AccessGroups = fallback.AccessGroups.ToList()
            }
            : null;
    }

    private async Task<IReadOnlyList<MiniBlogPostSummary>> GetPublishedPostsAsync()
    {
        if (Libraries.Get<CL.MySQL2.MySQL2Library>() is null)
            return MiniBlogDemoData.Posts
                .Where(post => string.Equals(post.Status, "published", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(post => post.PublishedUtc)
                .Select(MapSeedSummary)
                .ToArray();

        return await DataService.GetPublishedPostsAsync().ConfigureAwait(false);
    }

    private async Task<PagedResult<MiniBlogPostSummary>> GetPublishedPostsPageAsync(int page, int pageSize)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 3 : pageSize;

        if (Libraries.Get<CL.MySQL2.MySQL2Library>() is null)
        {
            var published = MiniBlogDemoData.Posts
                .Where(post => string.Equals(post.Status, "published", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(post => post.PublishedUtc)
                .Select(MapSeedSummary)
                .ToList();

            var totalItems = published.Count;
            var items = published
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToList();

            return new PagedResult<MiniBlogPostSummary>
            {
                Items = items,
                PageNumber = safePage,
                PageSize = safePageSize,
                TotalItems = totalItems
            };
        }

        return await DataService.GetPublishedPostsPageAsync(safePage, safePageSize).ConfigureAwait(false);
    }

    private async Task<MiniBlogPostDetail?> GetPublishedPostBySlugAsync(string slug)
    {
        if (Libraries.Get<CL.MySQL2.MySQL2Library>() is null)
        {
            var match = MiniBlogDemoData.Posts.FirstOrDefault(post =>
                string.Equals(post.Slug, slug, StringComparison.OrdinalIgnoreCase)
                && string.Equals(post.Status, "published", StringComparison.OrdinalIgnoreCase));
            return match is null ? null : MapSeedDetail(match);
        }

        return await DataService.GetPublishedPostBySlugAsync(slug).ConfigureAwait(false);
    }

    private static string? SanitizeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal)
            ? null
            : returnUrl;

    private static int ParsePositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;

    private static WebResult Redirect(WebRequestContext request, string location)
    {
        request.HttpContext.Response.Headers.Location = location;
        return new WebResult
        {
            StatusCode = 302,
            ContentType = "text/html; charset=utf-8",
            TextBody = $"""<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0;url={System.Net.WebUtility.HtmlEncode(location)}"></head><body>Redirecting to <a href="{System.Net.WebUtility.HtmlEncode(location)}">{System.Net.WebUtility.HtmlEncode(location)}</a>...</body></html>"""
        };
    }

    private Dictionary<string, object?> BuildLoginModel(string returnUrl, string message, bool isError)
    {
        var cards = new StringBuilder();
        foreach (var user in MiniBlogDemoData.Users)
        {
            cards.AppendLine($"""
                <div class="demo-user-card">
                    <p class="demo-user-title">{System.Net.WebUtility.HtmlEncode(user.DisplayName)}</p>
                    <p class="demo-user-meta mb-1">Handle: <code>{System.Net.WebUtility.HtmlEncode(user.Handle)}</code></p>
                    <p class="demo-user-meta mb-1">User ID: <code>{System.Net.WebUtility.HtmlEncode(user.UserId)}</code></p>
                    <p class="demo-user-meta mb-1">Password: <code>{System.Net.WebUtility.HtmlEncode(user.Password)}</code></p>
                    <p class="demo-user-meta mb-0">Groups: <code>{System.Net.WebUtility.HtmlEncode(string.Join(", ", user.AccessGroups))}</code></p>
                </div>
                """);
        }

        return new Dictionary<string, object?>
        {
            ["page_title"] = "Login",
            ["hero_title"] = "Sign in to Northwind Journal",
            ["hero_copy"] = "The public blog stays open, while the editor surface lives inside a plugin-owned admin site.",
            ["return_url"] = returnUrl,
            ["message"] = message,
            ["message_class"] = isError ? "alert-danger" : "alert-success",
            ["message_style"] = string.IsNullOrWhiteSpace(message) ? "display:none;" : string.Empty,
            ["demo_accounts"] = cards.ToString()
        };
    }

    private Dictionary<string, object?> BuildLayoutModel(WebRequestContext request, string pageTitle, string heroTitle, string heroCopy) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["page_title"] = pageTitle,
            ["hero_title"] = heroTitle,
            ["hero_copy"] = heroCopy,
            ["site_title"] = _config.SiteTitle,
            ["tagline"] = _config.Tagline,
            ["current_user"] = string.IsNullOrWhiteSpace(request.UserId)
                ? "Guest"
                : request.GetSessionValue("miniblog.display_name", request.UserId) ?? request.UserId,
            ["current_groups"] = request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", request.AccessGroups)
        };

    private WebPageMeta CreateMeta(WebRequestContext request, string title, string description, IReadOnlyList<string>? keywords = null)
    {
        var canonicalUrl = $"{_config.PublicBaseUrl}{request.Path}";
        return new WebPageMeta
        {
            Title = title,
            Description = description,
            CanonicalUrl = canonicalUrl,
            Language = "en",
            Keywords = keywords ?? [],
            OpenGraph = new WebOpenGraphMeta
            {
                Title = title,
                Description = description,
                Type = "website",
                Url = canonicalUrl,
                SiteName = _config.SiteTitle
            },
            Twitter = new WebTwitterMeta
            {
                Card = "summary_large_image",
                Title = title,
                Description = description
            }
        };
    }

    private static MiniBlogPostSummary MapSeedSummary(MiniBlogSeedPost post) => new()
    {
        Id = post.Id,
        Slug = post.Slug,
        Title = post.Title,
        Summary = post.Summary,
        Status = post.Status,
        AuthorUserId = post.AuthorUserId,
        AuthorDisplayName = post.AuthorDisplayName,
        MetaTitle = post.MetaTitle,
        MetaDescription = post.MetaDescription,
        PublishedUtc = post.PublishedUtc,
        UpdatedUtc = post.PublishedUtc ?? DateTimeOffset.UtcNow
    };

    private static MiniBlogPostDetail MapSeedDetail(MiniBlogSeedPost post) => new()
    {
        Id = post.Id,
        Slug = post.Slug,
        Title = post.Title,
        Summary = post.Summary,
        BodyHtml = post.BodyHtml,
        Status = post.Status,
        AuthorUserId = post.AuthorUserId,
        AuthorDisplayName = post.AuthorDisplayName,
        MetaTitle = post.MetaTitle,
        MetaDescription = post.MetaDescription,
        PublishedUtc = post.PublishedUtc,
        UpdatedUtc = post.PublishedUtc ?? DateTimeOffset.UtcNow
    };

    private static string BuildPostsPagerHtml(PagedResult<MiniBlogPostSummary> page)
    {
        if (page.TotalPages <= 1)
            return string.Empty;

        var parts = new List<string>
        {
            "<nav class=\"posts-pager\" aria-label=\"Posts pagination\">",
            $"<span class=\"posts-pager__summary\">Page {page.PageNumber} of {page.TotalPages}</span>",
            "<div class=\"posts-pager__links\">"
        };

        if (page.HasPreviousPage)
            parts.Add($"<a class=\"btn btn-outline-dark\" href=\"/posts?page={page.PageNumber - 1}\">Newer</a>");
        else
            parts.Add("<span class=\"btn btn-outline-dark disabled\" aria-disabled=\"true\">Newer</span>");

        for (var index = 1; index <= page.TotalPages; index++)
        {
            if (index == page.PageNumber)
                parts.Add($"<span class=\"posts-pager__page posts-pager__page--current\">{index}</span>");
            else
                parts.Add($"<a class=\"posts-pager__page\" href=\"/posts?page={index}\">{index}</a>");
        }

        if (page.HasNextPage)
            parts.Add($"<a class=\"btn btn-outline-dark\" href=\"/posts?page={page.PageNumber + 1}\">Older</a>");
        else
            parts.Add("<span class=\"btn btn-outline-dark disabled\" aria-disabled=\"true\">Older</span>");

        parts.Add("</div>");
        parts.Add("</nav>");
        return string.Join(string.Empty, parts);
    }
}
