using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using CL.Common.FileHandling;
using CL.Common.Web;
using CL.GitHelper;
using CL.GitHelper.Models;
using CL.StorageS3;
using CL.WebLogic.Configuration;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Security;
using CodeLogic;
using CodeLogic.Framework.Libraries;
using Microsoft.AspNetCore.StaticFiles;

namespace CL.WebLogic.Theming;

public sealed partial class ThemeManager
{
    private static readonly Regex LayoutRegex = new(
        @"^\s*\{layout:([^\}]+)\}\s*",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SectionRegex = new(
        @"\{section:([^\}]+)\}(.*?)\{/section\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RenderSectionRegex = new(
        @"\{rendersection:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RenderHeadRegex = new(
        @"\{renderhead\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PartialRegex = new(
        @"\{partial:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WidgetRegex = new(
        @"\{widget:([^\s\}]+)(.*?)\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WidgetAreaRegex = new(
        @"\{widgetarea:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IfOpenRegex = new(
        @"\{if:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IfNotOpenRegex = new(
        @"\{ifnot:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LegacyAuthGroupBlockRegex = new(
        @"\{page:auth:requireaccessgroup:([^\}]+)\}(.*?)\{/page:auth\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LegacyAuthenticatedBlockRegex = new(
        @"\{page:auth:requireauthenticated\}(.*?)\{/page:auth\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ForeachRegex = new(
        @"\{foreach:([^\}]+)\}(.*?)\{/foreach\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RawTokenRegex = new(
        @"\{raw:([^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TokenRegex = new(
        @"\{([a-z]+:[^\}]+)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LegacyModelTokenRegex = new(
        @"\{\{([a-zA-Z0-9_\.\-]+)\}\}",
        RegexOptions.Compiled);

    private static readonly Regex WidgetAttributeRegex = new(
        "([a-zA-Z0-9_\\-]+)\\s*=\\s*\"(.*?)\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private readonly WebWidgetRegistry _widgets;
    private readonly IWebWidgetSettingsStore? _settingsStore;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();
    private readonly ConcurrentDictionary<string, CachedContent> _cache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private WebSecurityService? _security;

    public ThemeManager(LibraryContext context, WebLogicConfig config, WebWidgetRegistry widgets, IWebWidgetSettingsStore? settingsStore)
    {
        _context = context;
        _config = config;
        _widgets = widgets;
        _settingsStore = settingsStore;
    }

    public void SetSecurityService(WebSecurityService security)
    {
        _security = security;
    }

    public void InitializeCaching(string themeRoot)
    {
        if (!_config.Theme.EnableCaching || _config.Storage.Mode != WebStorageMode.Local)
            return;

        if (!Directory.Exists(themeRoot))
            return;

        _watcher = new FileSystemWatcher(themeRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnThemeFileChanged;
        _watcher.Created += OnThemeFileChanged;
        _watcher.Deleted += OnThemeFileChanged;
        _watcher.Renamed += OnThemeFileRenamed;
        _watcher.EnableRaisingEvents = true;

        _context.Logger.Info($"Template caching enabled with file watcher on: {themeRoot}");
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    public void DisposeCaching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _cache.Clear();
    }

    private void OnThemeFileChanged(object sender, FileSystemEventArgs e)
    {
        InvalidateCacheForPath(e.FullPath);
    }

    private void OnThemeFileRenamed(object sender, RenamedEventArgs e)
    {
        InvalidateCacheForPath(e.OldFullPath);
        InvalidateCacheForPath(e.FullPath);
    }

    private void InvalidateCacheForPath(string fullPath)
    {
        var keysToRemove = _cache.Keys
            .Where(key => fullPath.EndsWith(key.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);

        if (keysToRemove.Length > 0)
            _context.Logger.Debug($"Template cache invalidated: {string.Join(", ", keysToRemove)}");
    }

    private sealed record CachedContent(byte[] Bytes, DateTime CachedAtUtc);

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
        WebRequestContext? pageContext = null,
        WebPageMeta? meta = null)
    {
        var normalizedPath = NormalizeTemplatePath(templatePath, templatePath);
        var text = await ReadTextAsync(normalizedPath, themeRoot).ConfigureAwait(false);
        if (text is null)
            return $"<html><body><h1>Template not found</h1><p>{HtmlHelper.Encode(normalizedPath)}</p></body></html>";

        var renderContext = new TemplateRenderContext
        {
            TemplatePath = normalizedPath,
            ThemeRoot = themeRoot,
            Model = model ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            PageContext = pageContext,
            Meta = meta,
            Sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            VisitedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        return await RenderTemplateCoreAsync(text, renderContext).ConfigureAwait(false);
    }

    public async Task<string> RenderWidgetAsync(
        string widgetName,
        IReadOnlyDictionary<string, string>? parameters,
        IReadOnlyDictionary<string, object?>? model,
        string? themeRoot,
        WebRequestContext pageContext,
        string? instanceId = null)
    {
        if (!_widgets.TryGet(widgetName, out var widget) || widget is null)
            return $"<!-- Widget not found: {HtmlHelper.Encode(widgetName)} -->";

        if (!widget.AllowAnonymous && !pageContext.IsAuthenticated)
            return string.Empty;

        if (widget.RequiredAccessGroups.Length > 0 && !pageContext.HasAnyAccessGroup(widget.RequiredAccessGroups))
            return string.Empty;

        var widgetContext = new WebWidgetContext
        {
            Name = widget.Name,
            InstanceId = instanceId,
            Parameters = await MergeWidgetParametersAsync(widget.Name, instanceId, parameters).ConfigureAwait(false),
            Model = model ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            Request = pageContext,
            Contributor = widget.Contributor,
            SettingsStore = _settingsStore
        };

        return await RenderWidgetDefinitionAsync(widget, widgetContext, new TemplateRenderContext
        {
            TemplatePath = $"widgets/{widget.Name}.html",
            ThemeRoot = themeRoot,
            Model = model ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            PageContext = pageContext,
            Sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            VisitedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        }).ConfigureAwait(false);
    }

    public async Task<string> RenderWidgetAreaAsync(
        string areaName,
        IReadOnlyDictionary<string, object?>? model,
        string? themeRoot,
        WebRequestContext pageContext,
        string? targetPathOverride = null)
    {
        var registrations = _widgets.GetAreaWidgets(areaName);
        if (registrations.Count == 0)
            return string.Empty;

        var effectivePath = string.IsNullOrWhiteSpace(targetPathOverride) ? pageContext.Path : targetPathOverride!;
        var builder = new StringBuilder();
        foreach (var area in registrations)
        {
            if (!AreaMatchesRequest(area, pageContext, effectivePath))
                continue;

            var mergedParameters = new Dictionary<string, string>(area.Parameters, StringComparer.OrdinalIgnoreCase);
            var html = await RenderWidgetAsync(
                area.WidgetName,
                mergedParameters,
                model,
                themeRoot,
                pageContext,
                area.InstanceId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(html))
                builder.Append(html);
        }

        return builder.ToString();
    }

    public async Task<WebResult?> TryReadAssetAsync(string requestPath, string? themeRoot, string? ifNoneMatch = null, string? ifModifiedSince = null)
    {
        var normalized = requestPath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var bytes = await ReadBytesAsync(normalized, themeRoot).ConfigureAwait(false);
        if (bytes is null)
            return null;

        if (!_contentTypes.TryGetContentType(normalized, out var contentType))
            contentType = "application/octet-stream";

        var etag = ComputeETag(bytes);
        var lastModified = GetFileLastModified(normalized, themeRoot);
        var headers = BuildAssetCacheHeaders(etag, lastModified, normalized);

        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && string.Equals(ifNoneMatch.Trim('"'), etag.Trim('"'), StringComparison.Ordinal))
        {
            return new WebResult
            {
                StatusCode = 304,
                ContentType = contentType,
                Headers = headers
            };
        }

        if (lastModified.HasValue && !string.IsNullOrWhiteSpace(ifModifiedSince)
            && DateTimeOffset.TryParse(ifModifiedSince, out var since)
            && lastModified.Value <= since)
        {
            return new WebResult
            {
                StatusCode = 304,
                ContentType = contentType,
                Headers = headers
            };
        }

        return new WebResult
        {
            StatusCode = 200,
            ContentType = contentType,
            BinaryBody = bytes,
            Headers = headers
        };
    }

    private static string ComputeETag(byte[] content)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(content);
        return $"\"{Convert.ToHexString(hash[..8]).ToLowerInvariant()}\"";
    }

    private DateTimeOffset? GetFileLastModified(string relativePath, string? themeRoot)
    {
        if (_config.Storage.Mode != WebStorageMode.Local || string.IsNullOrWhiteSpace(themeRoot))
            return null;

        var fullPath = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero) : null;
    }

    private static Dictionary<string, string> BuildAssetCacheHeaders(string etag, DateTimeOffset? lastModified, string path)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ETag"] = etag,
            ["Cache-Control"] = IsImmutableAsset(path) ? "public, max-age=31536000, immutable" : "public, max-age=3600",
            ["Vary"] = "Accept-Encoding"
        };

        if (lastModified.HasValue)
            headers["Last-Modified"] = lastModified.Value.ToString("R");

        return headers;
    }

    private static bool IsImmutableAsset(string path) =>
        path.Contains("/vendor/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(".min.", StringComparison.OrdinalIgnoreCase);

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

        if (_config.Theme.EnableCaching && _cache.TryGetValue(relativePath, out var cached))
            return cached.Bytes;

        var fullPath = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var result = await FileSystem.ReadAllBytesAsync(fullPath).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value is null)
            return null;

        if (_config.Theme.EnableCaching)
            _cache[relativePath] = new CachedContent(result.Value, DateTime.UtcNow);

        return result.Value;
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

    private async Task<string> RenderTemplateCoreAsync(string template, TemplateRenderContext context)
    {
        if (!context.VisitedTemplates.Add(context.TemplatePath))
            return $"<div class=\"weblogic-template-error\">Template recursion detected for {HtmlHelper.Encode(context.TemplatePath)}</div>";

        try
        {
            var layoutMatch = LayoutRegex.Match(template);
            if (layoutMatch.Success)
            {
                var layoutPath = NormalizeTemplatePath(layoutMatch.Groups[1].Value, context.TemplatePath);
                var pageTemplate = LayoutRegex.Replace(template, string.Empty, 1);

                var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match sectionMatch in SectionRegex.Matches(pageTemplate))
                {
                    sections[sectionMatch.Groups[1].Value.Trim()] =
                        await RenderFragmentAsync(sectionMatch.Groups[2].Value, context).ConfigureAwait(false);
                }

                pageTemplate = SectionRegex.Replace(pageTemplate, string.Empty);
                var body = await RenderFragmentAsync(pageTemplate, context).ConfigureAwait(false);
                var layoutText = await ReadTextAsync(layoutPath, context.ThemeRoot).ConfigureAwait(false);

                if (layoutText is null)
                    return $"<html><body><h1>Layout not found</h1><p>{HtmlHelper.Encode(layoutPath)}</p></body></html>";

                var layoutContext = context with
                {
                    TemplatePath = layoutPath,
                    RenderBody = body,
                    Sections = sections
                };

                return await RenderTemplateCoreAsync(layoutText, layoutContext).ConfigureAwait(false);
            }

            return await RenderFragmentAsync(template, context).ConfigureAwait(false);
        }
        finally
        {
            context.VisitedTemplates.Remove(context.TemplatePath);
        }
    }

    private async Task<string> RenderFragmentAsync(string template, TemplateRenderContext context)
    {
        var html = template;
        html = RenderHeadRegex.Replace(html, WebPageHeadRenderer.Render(context.Meta, context.Model));
        html = html.Replace("{renderbody}", context.RenderBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        html = ReplaceCsrfTokens(html, context);
        html = RenderSectionRegex.Replace(html, match =>
            context.Sections.TryGetValue(match.Groups[1].Value.Trim(), out var value) ? value : string.Empty);

        html = await ReplaceMatchesAsync(html, PartialRegex, async match =>
        {
            var partialPath = NormalizeTemplatePath(match.Groups[1].Value, context.TemplatePath);
            var partialText = await ReadTextAsync(partialPath, context.ThemeRoot).ConfigureAwait(false);
            if (partialText is null)
                return $"<!-- Partial not found: {HtmlHelper.Encode(partialPath)} -->";

            return await RenderTemplateCoreAsync(partialText, context with { TemplatePath = partialPath }).ConfigureAwait(false);
        }).ConfigureAwait(false);

        html = await ReplaceMatchesAsync(html, ForeachRegex, async match =>
        {
            var value = await ResolveSelectorValueAsync(match.Groups[1].Value.Trim(), context).ConfigureAwait(false);
            if (value is string)
                return string.Empty;

            IEnumerable<object?> values = value switch
            {
                IEnumerable<object?> objectItems => objectItems,
                IEnumerable enumerable => enumerable.Cast<object?>(),
                _ => []
            };

            var builder = new StringBuilder();
            foreach (var item in values)
            {
                builder.Append(await RenderFragmentAsync(match.Groups[2].Value, context with { CurrentItem = item }).ConfigureAwait(false));
            }

            return builder.ToString();
        }).ConfigureAwait(false);

        html = await ProcessConditionalBlocksAsync(html, "if", false, context).ConfigureAwait(false);
        html = await ProcessConditionalBlocksAsync(html, "ifnot", true, context).ConfigureAwait(false);

        html = await ReplaceMatchesAsync(html, WidgetRegex, async match =>
        {
            var widgetName = match.Groups[1].Value.Trim();
            if (!_widgets.TryGet(widgetName, out var widget) || widget is null)
                return $"<!-- Widget not found: {HtmlHelper.Encode(widgetName)} -->";

            if (context.PageContext is null)
                return string.Empty;

            if (!widget.AllowAnonymous && !context.PageContext.IsAuthenticated)
                return string.Empty;

            if (widget.RequiredAccessGroups.Length > 0 && !context.PageContext.HasAnyAccessGroup(widget.RequiredAccessGroups))
                return string.Empty;

            var parameters = await ParseWidgetParametersAsync(match.Groups[2].Value, context).ConfigureAwait(false);
            parameters.TryGetValue("instance", out var instanceId);
            var widgetContext = new WebWidgetContext
            {
                Name = widget.Name,
                InstanceId = instanceId,
                Parameters = await MergeWidgetParametersAsync(widget.Name, instanceId, parameters).ConfigureAwait(false),
                Model = context.Model,
                Request = context.PageContext,
                Contributor = widget.Contributor,
                SettingsStore = _settingsStore
            };

            return await RenderWidgetDefinitionAsync(widget, widgetContext, context).ConfigureAwait(false);
        }).ConfigureAwait(false);

        html = await ReplaceMatchesAsync(html, WidgetAreaRegex, async match =>
        {
            if (context.PageContext is null)
                return string.Empty;

            return await RenderWidgetAreaAsync(
                match.Groups[1].Value.Trim(),
                context.Model,
                context.ThemeRoot,
                context.PageContext,
                context.PageContext.Path).ConfigureAwait(false);
        }).ConfigureAwait(false);

        html = LegacyAuthGroupBlockRegex.Replace(html, match =>
        {
            if (context.PageContext is null)
                return string.Empty;

            return context.PageContext.HasAnyAccessGroup(SplitValues(match.Groups[1].Value))
                ? match.Groups[2].Value
                : string.Empty;
        });

        html = LegacyAuthenticatedBlockRegex.Replace(html, match =>
            context.PageContext?.IsAuthenticated == true ? match.Groups[1].Value : string.Empty);

        html = await ReplaceMatchesAsync(html, RawTokenRegex, async match =>
        {
            var (value, filters) = ParseFilters(match.Groups[1].Value.Trim());
            var resolved = await ResolveSelectorValueAsync(value, context).ConfigureAwait(false);
            return FormatValue(ApplyFilters(resolved, filters), encode: false);
        }).ConfigureAwait(false);

        html = await ReplaceMatchesAsync(html, TokenRegex, async match =>
        {
            var (value, filters) = ParseFilters(match.Groups[1].Value.Trim());
            var resolved = await ResolveSelectorValueAsync(value, context).ConfigureAwait(false);
            return FormatValue(ApplyFilters(resolved, filters), encode: true);
        }).ConfigureAwait(false);

        html = LegacyModelTokenRegex.Replace(html, match =>
        {
            var token = match.Groups[1].Value;
            if (token.Contains('.', StringComparison.Ordinal))
            {
                var dottedValue = ResolveLegacyDottedToken(token, context);
                return FormatValue(dottedValue, encode: true);
            }

            return context.Model.TryGetValue(token, out var value)
                ? FormatValue(value, encode: true)
                : match.Value;
        });

        return html;
    }

    private async Task<string> ProcessConditionalBlocksAsync(string html, string tag, bool negate, TemplateRenderContext context)
    {
        var openTag = $"{{{tag}:";
        var closeTag = $"{{/{tag}}}";
        var result = html;

        while (true)
        {
            var openIndex = result.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            if (openIndex < 0)
                break;

            var tagEnd = result.IndexOf('}', openIndex + openTag.Length);
            if (tagEnd < 0)
                break;

            var expression = result[(openIndex + openTag.Length)..tagEnd].Trim();

            var depth = 1;
            var searchFrom = tagEnd + 1;
            var closeIndex = -1;

            while (depth > 0 && searchFrom < result.Length)
            {
                var nextOpen = result.IndexOf(openTag, searchFrom, StringComparison.OrdinalIgnoreCase);
                var nextClose = result.IndexOf(closeTag, searchFrom, StringComparison.OrdinalIgnoreCase);

                if (nextClose < 0)
                    break;

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    searchFrom = nextOpen + openTag.Length;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeIndex = nextClose;
                    }
                    else
                    {
                        searchFrom = nextClose + closeTag.Length;
                    }
                }
            }

            if (closeIndex < 0)
                break;

            var innerContent = result[(tagEnd + 1)..closeIndex];
            var condition = await EvaluateConditionAsync(expression, context).ConfigureAwait(false);
            var show = negate ? !condition : condition;
            var replacement = show
                ? await RenderFragmentAsync(innerContent, context).ConfigureAwait(false)
                : string.Empty;

            result = string.Concat(result.AsSpan(0, openIndex), replacement, result.AsSpan(closeIndex + closeTag.Length));
        }

        return result;
    }

    private async Task<bool> EvaluateConditionAsync(string expression, TemplateRenderContext context)
    {
        if (string.Equals(expression, "auth", StringComparison.OrdinalIgnoreCase))
            return context.PageContext?.IsAuthenticated == true;

        if (expression.StartsWith("accessgroup:", StringComparison.OrdinalIgnoreCase))
            return context.PageContext?.HasAnyAccessGroup(SplitValues(expression["accessgroup:".Length..])) == true;

        if (expression.StartsWith("permission:", StringComparison.OrdinalIgnoreCase))
            return context.PageContext?.HasPermission(expression["permission:".Length..].Trim()) == true;

        return IsTruthy(await ResolveSelectorValueAsync(expression, context).ConfigureAwait(false));
    }

    private static object? ResolveLegacyDottedToken(string token, TemplateRenderContext context)
    {
        if (token.StartsWith("page.", StringComparison.OrdinalIgnoreCase))
            return ResolvePageValue(context.PageContext, token["page.".Length..]);

        if (token.StartsWith("item.", StringComparison.OrdinalIgnoreCase))
            return ResolvePathValue(context.CurrentItem, token["item.".Length..]);

        return ResolvePathValue(context.Model, token);
    }

    private async Task<object?> ResolveSelectorValueAsync(string selector, TemplateRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return null;

        var separator = selector.IndexOf(':');
        if (separator < 0)
            return context.Model.TryGetValue(selector, out var value) ? value : null;

        var scope = selector[..separator];
        var key = selector[(separator + 1)..];

        return scope.ToLowerInvariant() switch
        {
            "model" => ResolvePathValue(context.Model, key),
            "page" => ResolvePageValue(context.PageContext, key),
            "query" => context.PageContext?.GetQuery(key),
            "cookie" => context.PageContext?.GetCookie(key),
            "session" => context.PageContext?.GetSessionValue(key),
            "header" => context.PageContext?.GetHeader(key),
            "form" => context.PageContext is null ? null : await context.PageContext.GetFormValueAsync(key).ConfigureAwait(false),
            "route" => ResolveRouteValue(context.PageContext?.Route, key),
            "item" => ResolvePathValue(context.CurrentItem, key),
            _ => null
        };
    }

    private static object? ResolveRouteValue(WebRouteDefinition? route, string key) => key.ToLowerInvariant() switch
    {
        "path" => route?.Path,
        "kind" => route?.Kind.ToString(),
        "name" => route?.Name,
        "description" => route?.Description,
        _ => null
    };

    private static object? ResolvePageValue(WebRequestContext? pageContext, string key)
    {
        if (pageContext is null)
            return null;

        return key.ToLowerInvariant() switch
        {
            "path" => pageContext.Path,
            "method" => pageContext.Method,
            "client_ip" => pageContext.ClientIp,
            "user_id" => string.IsNullOrWhiteSpace(pageContext.UserId) ? "anonymous" : pageContext.UserId,
            "query_string" => pageContext.QueryString,
            "host" => pageContext.Host,
            "scheme" => pageContext.Scheme,
            "content_type" => pageContext.ContentType,
            "is_https" => pageContext.IsHttps,
            "is_authenticated" => pageContext.IsAuthenticated,
            "access_groups" => pageContext.AccessGroups,
            _ => ResolvePathValue(pageContext, key)
        };
    }

    private static object? ResolvePathValue(object? source, string path)
    {
        if (source is null)
            return null;

        object? current = source;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
                return null;

            if (current is IReadOnlyDictionary<string, object?> readOnlyObjectDictionary &&
                readOnlyObjectDictionary.TryGetValue(segment, out var objectValue))
            {
                current = objectValue;
                continue;
            }

            if (current is IDictionary<string, object?> objectDictionary &&
                objectDictionary.TryGetValue(segment, out var objectDictionaryValue))
            {
                current = objectDictionaryValue;
                continue;
            }

            if (current is IReadOnlyDictionary<string, string> readOnlyStringDictionary &&
                readOnlyStringDictionary.TryGetValue(segment, out var stringValue))
            {
                current = stringValue;
                continue;
            }

            if (current is IDictionary<string, string> stringDictionary &&
                stringDictionary.TryGetValue(segment, out var dictionaryStringValue))
            {
                current = dictionaryStringValue;
                continue;
            }

            var property = current.GetType().GetProperties()
                .FirstOrDefault(prop => string.Equals(prop.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (property is null)
                return null;

            current = property.GetValue(current);
        }

        return current;
    }

    private string ReplaceCsrfTokens(string html, TemplateRenderContext context)
    {
        if (!html.Contains("{csrf", StringComparison.OrdinalIgnoreCase))
            return html;

        var token = string.Empty;
        if (context.PageContext is not null && _security is not null)
        {
            token = _security.GetOrCreateCsrfToken(context.PageContext.HttpContext);
        }

        html = html.Replace("{csrf}", $"<input type=\"hidden\" name=\"_csrf\" value=\"{CL.Common.Web.HtmlHelper.Encode(token)}\">", StringComparison.OrdinalIgnoreCase);
        html = html.Replace("{csrf_token}", CL.Common.Web.HtmlHelper.Encode(token), StringComparison.OrdinalIgnoreCase);
        html = html.Replace("{csrf_meta}", $"<meta name=\"csrf-token\" content=\"{CL.Common.Web.HtmlHelper.Encode(token)}\">", StringComparison.OrdinalIgnoreCase);

        return html;
    }

    private static (string Selector, (string Name, string? Arg)[] Filters) ParseFilters(string expression)
    {
        var pipeIndex = expression.IndexOf('|');
        if (pipeIndex < 0)
            return (expression, []);

        var selector = expression[..pipeIndex].Trim();
        var filterPart = expression[(pipeIndex + 1)..];
        var filters = new List<(string Name, string? Arg)>();

        foreach (var raw in SplitFilterChain(filterPart))
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0)
            {
                filters.Add((trimmed, null));
            }
            else
            {
                var name = trimmed[..colonIndex].Trim();
                var arg = trimmed[(colonIndex + 1)..].Trim().Trim('"').Trim('\'');
                filters.Add((name, arg));
            }
        }

        return (selector, filters.ToArray());
    }

    private static IEnumerable<string> SplitFilterChain(string input)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            switch (input[i])
            {
                case '"' or '\'':
                    depth = depth == 0 ? 1 : 0;
                    break;
                case '|' when depth == 0:
                    yield return input[start..i];
                    start = i + 1;
                    break;
            }
        }

        if (start < input.Length)
            yield return input[start..];
    }

    private static object? ApplyFilters(object? value, (string Name, string? Arg)[] filters)
    {
        if (filters.Length == 0)
            return value;

        foreach (var (name, arg) in filters)
        {
            value = name.ToLowerInvariant() switch
            {
                "uppercase" or "upper" => value?.ToString()?.ToUpperInvariant(),
                "lowercase" or "lower" => value?.ToString()?.ToLowerInvariant(),
                "trim" => value?.ToString()?.Trim(),
                "capitalize" => Capitalize(value?.ToString()),
                "truncate" => Truncate(value?.ToString(), int.TryParse(arg, out var len) ? len : 100),
                "replace" => ApplyReplace(value?.ToString(), arg),
                "default" => string.IsNullOrWhiteSpace(value?.ToString()) ? arg : value,
                "prefix" => value is null || string.IsNullOrEmpty(value.ToString()) ? value : $"{arg}{value}",
                "suffix" => value is null || string.IsNullOrEmpty(value.ToString()) ? value : $"{value}{arg}",
                "format" => ApplyFormat(value, arg),
                "length" or "count" => value?.ToString()?.Length ?? 0,
                "reverse" => value is string s ? new string(s.Reverse().ToArray()) : value,
                "wordcount" => value?.ToString()?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
                "nl2br" => value?.ToString()?.Replace("\n", "<br>", StringComparison.Ordinal),
                "urlencode" => Uri.EscapeDataString(value?.ToString() ?? string.Empty),
                "slug" => Slugify(value?.ToString()),
                _ => value
            };
        }

        return value;
    }

    private static string? Capitalize(string? text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text[..maxLength].TrimEnd() + "...";
    }

    private static string? ApplyReplace(string? text, string? arg)
    {
        if (text is null || string.IsNullOrEmpty(arg))
            return text;

        var parts = arg.Split("→", 2, StringSplitOptions.None);
        if (parts.Length < 2)
            parts = arg.Split("->", 2, StringSplitOptions.None);

        return parts.Length == 2
            ? text.Replace(parts[0], parts[1], StringComparison.Ordinal)
            : text;
    }

    private static object? ApplyFormat(object? value, string? format)
    {
        if (value is null || string.IsNullOrWhiteSpace(format))
            return value;

        return value switch
        {
            DateTime dt => dt.ToString(format),
            DateTimeOffset dto => dto.ToString(format),
            DateOnly d => d.ToString(format),
            int i => i.ToString(format),
            long l => l.ToString(format),
            double d => d.ToString(format),
            decimal d => d.ToString(format),
            _ => value
        };
    }

    private static string? Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var slug = text.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[\s-]+", "-");
        return slug.Trim('-');
    }

    private static string FormatValue(object? value, bool encode)
    {
        if (value is null)
            return string.Empty;

        var text = value switch
        {
            IReadOnlyCollection<string> strings => string.Join(", ", strings),
            IEnumerable<string> strings => string.Join(", ", strings),
            _ => value.ToString() ?? string.Empty
        };

        return encode ? HtmlHelper.Encode(text) : text;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool boolean => boolean,
        string text => !string.IsNullOrWhiteSpace(text) &&
                       !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase),
        int number => number != 0,
        long number => number != 0,
        IEnumerable<object?> enumerable => enumerable.Any(),
        IEnumerable enumerable => enumerable.Cast<object?>().Any(),
        _ => true
    };

    private static IReadOnlyDictionary<string, object?> MergeModel(
        IReadOnlyDictionary<string, object?> baseModel,
        IReadOnlyDictionary<string, object?>? overlay)
    {
        if (overlay is null || overlay.Count == 0)
            return baseModel;

        var merged = new Dictionary<string, object?>(baseModel, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overlay)
            merged[pair.Key] = pair.Value;

        return merged;
    }

    private async Task<IReadOnlyDictionary<string, string>> ParseWidgetParametersAsync(string raw, TemplateRenderContext context)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WidgetAttributeRegex.Matches(raw))
        {
            values[match.Groups[1].Value] = await ResolveInlineTextAsync(match.Groups[2].Value, context).ConfigureAwait(false);
        }

        return values;
    }

    private async Task<string> RenderWidgetDefinitionAsync(
        WebWidgetDefinition widget,
        WebWidgetContext widgetContext,
        TemplateRenderContext templateContext)
    {
        var result = await widget.Handler(widgetContext).ConfigureAwait(false);

        if (result.TemplatePath is not null)
        {
            var widgetPath = NormalizeTemplatePath(result.TemplatePath, templateContext.TemplatePath);
            var widgetText = await ReadTextAsync(widgetPath, templateContext.ThemeRoot).ConfigureAwait(false);
            if (widgetText is null)
                return $"<!-- Widget template not found: {HtmlHelper.Encode(widgetPath)} -->";

            return await RenderTemplateCoreAsync(widgetText, templateContext with
            {
                TemplatePath = widgetPath,
                Model = MergeModel(templateContext.Model, result.Model)
            }).ConfigureAwait(false);
        }

        return result.Html ?? string.Empty;
    }

    private async Task<IReadOnlyDictionary<string, string>> MergeWidgetParametersAsync(
        string widgetName,
        string? instanceId,
        IReadOnlyDictionary<string, string>? parameters)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                if (string.Equals(pair.Key, "instance", StringComparison.OrdinalIgnoreCase))
                    continue;

                merged[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(instanceId) && _settingsStore is not null)
        {
            var record = await _settingsStore.GetAsync(instanceId).ConfigureAwait(false);
            if (record is not null && (string.IsNullOrWhiteSpace(record.WidgetName) || string.Equals(record.WidgetName, widgetName, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var pair in record.Settings)
                    merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private async Task<string> ResolveInlineTextAsync(string value, TemplateRenderContext context)
    {
        var withRaw = await ReplaceMatchesAsync(value, RawTokenRegex, async match =>
        {
            var resolved = await ResolveSelectorValueAsync(match.Groups[1].Value.Trim(), context).ConfigureAwait(false);
            return FormatValue(resolved, encode: false);
        }).ConfigureAwait(false);

        return await ReplaceMatchesAsync(withRaw, TokenRegex, async match =>
        {
            var resolved = await ResolveSelectorValueAsync(match.Groups[1].Value.Trim(), context).ConfigureAwait(false);
            return FormatValue(resolved, encode: false);
        }).ConfigureAwait(false);
    }

    private static async Task<string> ReplaceMatchesAsync(
        string input,
        Regex regex,
        Func<Match, Task<string>> replacementFactory)
    {
        var matches = regex.Matches(input).Cast<Match>().ToArray();
        if (matches.Length == 0)
            return input;

        var builder = new StringBuilder();
        var currentIndex = 0;
        foreach (var match in matches)
        {
            builder.Append(input, currentIndex, match.Index - currentIndex);
            builder.Append(await replacementFactory(match).ConfigureAwait(false));
            currentIndex = match.Index + match.Length;
        }

        builder.Append(input, currentIndex, input.Length - currentIndex);
        return builder.ToString();
    }

    private static string[] SplitValues(string value) =>
        value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool AreaMatchesRequest(WebWidgetAreaDefinition area, WebRequestContext request, string effectivePath)
    {
        if (!area.AllowAnonymous && !request.IsAuthenticated)
            return false;

        if (area.RequiredAccessGroups.Length > 0 && !request.HasAnyAccessGroup(area.RequiredAccessGroups))
            return false;

        if (area.IncludeRoutePatterns.Length > 0 && !area.IncludeRoutePatterns.Any(pattern => RouteMatches(pattern, effectivePath)))
            return false;

        if (area.ExcludeRoutePatterns.Any(pattern => RouteMatches(pattern, effectivePath)))
            return false;

        return true;
    }

    private static bool RouteMatches(string pattern, string path)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPattern = pattern.Trim();
        if (string.Equals(normalizedPattern, "*", StringComparison.Ordinal))
            return true;

        if (!normalizedPattern.StartsWith('/'))
            normalizedPattern = "/" + normalizedPattern;

        if (normalizedPattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = normalizedPattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
        }

        return string.Equals(path, normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTemplatePath(string path, string currentTemplatePath)
    {
        var trimmed = path.Trim().Replace('\\', '/');
        if (!trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            trimmed += ".html";

        if (trimmed.StartsWith('/'))
            return trimmed.TrimStart('/');

        if (trimmed.StartsWith("templates/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("layouts/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("partials/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("widgets/", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var currentDirectory = Path.GetDirectoryName(currentTemplatePath.Replace('/', Path.DirectorySeparatorChar))
            ?.Replace('\\', '/')
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(currentDirectory))
            return trimmed;

        var relativeParts = currentDirectory
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var part in trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (relativeParts.Count > 0)
                    relativeParts.RemoveAt(relativeParts.Count - 1);
                continue;
            }

            relativeParts.Add(part);
        }

        return string.Join('/', relativeParts);
    }

    private sealed record TemplateRenderContext
    {
        public required string TemplatePath { get; init; }
        public required string? ThemeRoot { get; init; }
        public required IReadOnlyDictionary<string, object?> Model { get; init; }
        public required WebRequestContext? PageContext { get; init; }
        public WebPageMeta? Meta { get; init; }
        public required IReadOnlyDictionary<string, string> Sections { get; init; }
        public required HashSet<string> VisitedTemplates { get; init; }
        public string? RenderBody { get; init; }
        public object? CurrentItem { get; init; }
    }
}
