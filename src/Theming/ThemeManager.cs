using System.Text;
using System.Text.RegularExpressions;
using CL.Common.FileHandling;
using CL.Common.Web;
using CL.GitHelper;
using CL.GitHelper.Models;
using CL.StorageS3;
using CL.WebLogic.Configuration;
using CL.WebLogic.Runtime;
using CodeLogic;
using CodeLogic.Framework.Libraries;
using Microsoft.AspNetCore.StaticFiles;

namespace CL.WebLogic.Theming;

public sealed class ThemeManager
{
    private static readonly Regex AuthGroupBlockRegex = new(
        @"\{page:auth:requireaccessgroup:([^\}]+)\}(.*?)\{/page:auth\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AuthenticatedBlockRegex = new(
        @"\{page:auth:requireauthenticated\}(.*?)\{/page:auth\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public ThemeManager(LibraryContext context, WebLogicConfig config)
    {
        _context = context;
        _config = config;
    }

    public async Task<string> ResolveThemeRootAsync()
    {
        string root = _config.Theme.Source == ThemeSource.Git
            ? await EnsureGitThemeAsync().ConfigureAwait(false)
            : ResolveLocalThemePath(_config.Theme.LocalPath);

        _context.Events.Publish(new ThemeSynchronizedEvent(_config.Theme.Source.ToString(), root));
        return root;
    }

    public async Task<string> RenderTemplateAsync(
        string templatePath,
        IReadOnlyDictionary<string, object?>? model,
        string? themeRoot,
        WebRequestContext? pageContext = null)
    {
        var text = await ReadTextAsync(templatePath, themeRoot).ConfigureAwait(false);
        if (text is null)
            return $"<html><body><h1>Template not found</h1><p>{HtmlHelper.Encode(templatePath)}</p></body></html>";

        return ApplyTemplateModel(text, model, pageContext);
    }

    public async Task<WebResult?> TryReadAssetAsync(string requestPath, string? themeRoot)
    {
        var normalized = requestPath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var bytes = await ReadBytesAsync(normalized, themeRoot).ConfigureAwait(false);
        if (bytes is null)
            return null;

        if (!_contentTypes.TryGetContentType(normalized, out var contentType))
            contentType = "application/octet-stream";

        return WebResult.Bytes(bytes, contentType);
    }

    private async Task<string> EnsureGitThemeAsync()
    {
        var git = Libraries.Get<GitHelperLibrary>()
            ?? throw new InvalidOperationException("Theme source is Git but CL.GitHelper is not loaded.");

        try
        {
            git.RegisterRepository(new RepositoryConfiguration
            {
                Id = _config.Theme.RepositoryId,
                Name = $"{_config.SiteName} Theme",
                RepositoryUrl = _config.Theme.RepositoryUrl,
                LocalPath = Path.Combine(_context.DataDirectory, "themes", _config.Theme.RepositoryId),
                DefaultBranch = _config.Theme.Branch
            });
        }
        catch (ArgumentException)
        {
        }

        var repo = await git.GetRepositoryAsync(_config.Theme.RepositoryId).ConfigureAwait(false);
        var info = await repo.GetRepositoryInfoAsync().ConfigureAwait(false);

        if (!info.IsSuccess)
        {
            var clone = await repo.CloneAsync().ConfigureAwait(false);
            if (!clone.IsSuccess)
                throw new InvalidOperationException(clone.ErrorMessage ?? "Theme clone failed.");

            info = await repo.GetRepositoryInfoAsync().ConfigureAwait(false);
        }
        else if (_config.Theme.AutoSyncOnStart)
        {
            await repo.PullAsync().ConfigureAwait(false);
            info = await repo.GetRepositoryInfoAsync().ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(_config.Theme.Branch))
            await repo.CheckoutBranchAsync(_config.Theme.Branch).ConfigureAwait(false);

        var root = info.Value?.LocalPath
            ?? throw new InvalidOperationException("Theme repository did not produce a local path.");

        if (!string.IsNullOrWhiteSpace(_config.Theme.ThemeSubPath))
            root = Path.Combine(root, _config.Theme.ThemeSubPath);

        return root;
    }

    private string ResolveLocalThemePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private async Task<string?> ReadTextAsync(string relativePath, string? themeRoot)
    {
        var bytes = await ReadBytesAsync(relativePath, themeRoot).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]?> ReadBytesAsync(string relativePath, string? themeRoot)
    {
        if (_config.Storage.Mode == WebStorageMode.S3)
            return await ReadBytesFromS3Async(relativePath).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(themeRoot))
            return null;

        var fullPath = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var result = await FileSystem.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<byte[]?> ReadBytesFromS3Async(string relativePath)
    {
        var storage = Libraries.Get<StorageS3Library>();
        if (storage is null)
            return null;

        var service = storage.GetService(_config.Storage.S3ConnectionId);
        var key = string.IsNullOrWhiteSpace(_config.Storage.S3Prefix)
            ? relativePath
            : $"{_config.Storage.S3Prefix.TrimEnd('/')}/{relativePath}";

        var result = await service.GetObjectAsync(_config.Storage.S3Bucket, key).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    private static string ApplyTemplateModel(
        string template,
        IReadOnlyDictionary<string, object?>? model,
        WebRequestContext? pageContext)
    {
        var html = template;
        html = ApplyAuthBlocks(html, pageContext);

        if (pageContext is not null)
            html = ApplyPageTokens(html, pageContext);

        if (model is null || model.Count == 0)
            return html;

        foreach (var pair in model)
        {
            var encoded = pair.Value?.ToString() ?? string.Empty;
            html = html.Replace($"{{{{{pair.Key}}}}}", HtmlHelper.Encode(encoded), StringComparison.Ordinal);
        }

        return html;
    }

    private static string ApplyAuthBlocks(string html, WebRequestContext? pageContext)
    {
        html = AuthGroupBlockRegex.Replace(html, match =>
        {
            if (pageContext is null)
                return string.Empty;

            var groups = match.Groups[1].Value
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return pageContext.HasAnyAccessGroup(groups) ? match.Groups[2].Value : string.Empty;
        });

        html = AuthenticatedBlockRegex.Replace(html, match =>
            pageContext?.IsAuthenticated == true ? match.Groups[1].Value : string.Empty);

        return html;
    }

    private static string ApplyPageTokens(string html, WebRequestContext pageContext)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{page.path}}"] = HtmlHelper.Encode(pageContext.Path),
            ["{{page.method}}"] = HtmlHelper.Encode(pageContext.Method),
            ["{{page.client_ip}}"] = HtmlHelper.Encode(pageContext.ClientIp),
            ["{{page.user_id}}"] = HtmlHelper.Encode(pageContext.UserId),
            ["{{page.query_string}}"] = HtmlHelper.Encode(pageContext.QueryString)
        };

        foreach (var pair in replacements)
            html = html.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return html;
    }
}
