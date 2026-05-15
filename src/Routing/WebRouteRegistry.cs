using System.Collections.Concurrent;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Routing;

public delegate Task<WebResult> WebRouteHandler(WebRequestContext context);

public enum WebRouteKind
{
    Page,
    Api,
    Fallback
}

public sealed class WebRouteDefinition
{
    public required string Path { get; init; }
    public required WebRouteKind Kind { get; init; }
    public required WebRouteHandler Handler { get; init; }
    public required HashSet<string> Methods { get; init; }
    public required WebContributorDescriptor Contributor { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string[] Tags { get; init; }
    public required string[] RequiredAccessGroups { get; init; }
    public string? RequiredPermission { get; init; }
    public required bool AllowAnonymous { get; init; }
    public IWebMiddleware[] Middleware { get; init; } = [];
    public WebOutputCachePolicy? OutputCache { get; init; }
}

public sealed class WebRouteRegistry
{
    private readonly ConcurrentDictionary<string, WebRouteDefinition> _routes = new(StringComparer.OrdinalIgnoreCase);
    private WebRouteDefinition? _fallback;

    public int Count => _routes.Count + (_fallback is null ? 0 : 1);
    public WebRouteDefinition? Fallback => _fallback;

    public void RegisterPage(string path, WebRouteHandler handler, params string[] methods) =>
        Register(path, WebRouteKind.Page, handler, BuiltInContributor("application", "Application"), new WebRouteOptions(), methods);

    public void RegisterApi(string path, WebRouteHandler handler, params string[] methods) =>
        Register(path, WebRouteKind.Api, handler, BuiltInContributor("application", "Application"), new WebRouteOptions(), methods);

    public void RegisterFallback(WebRouteHandler handler, params string[] methods) =>
        RegisterFallback(handler, BuiltInContributor("application", "Application"), new WebRouteOptions(), methods);

    public void Register(
        string path,
        WebRouteKind kind,
        WebRouteHandler handler,
        WebContributorDescriptor contributor,
        WebRouteOptions options,
        params string[] methods)
    {
        var normalized = NormalizePath(path);
        var newMethods = NormalizeMethods(methods);

        // If a route is already registered at this path, merge — the
        // common case is one contributor registering separate handlers
        // for GET and POST on the same path (umbrella + ingest, read +
        // write, etc.). The previous behaviour silently overwrote the
        // first registration, which surfaced as confusing 405s at runtime.
        //
        // Merge rules:
        //   - methods must NOT overlap (each method maps to one handler)
        //   - auth posture must match (AllowAnonymous, RequiredPermission,
        //     RequiredAccessGroups) — different per-method auth isn't
        //     expressible on a single route definition
        //   - the merged handler dispatches by HTTP method
        //   - the first registration's Name / Description / Tags /
        //     Middleware / OutputCache are kept; the second's are folded
        //     into the description for traceability
        if (_routes.TryGetValue(normalized, out var existing))
        {
            var overlap = newMethods.Where(m => existing.Methods.Contains(m)).ToArray();
            if (overlap.Length > 0)
                throw new InvalidOperationException(
                    $"Route '{normalized}' is already registered for method(s) [{string.Join(", ", overlap)}]. " +
                    "Each (path, method) pair can have only one handler. Either combine the handlers and dispatch inside, " +
                    "or use distinct paths.");

            var requiredPermission = string.IsNullOrWhiteSpace(options.RequiredPermission) ? null : options.RequiredPermission;
            if (existing.AllowAnonymous != options.AllowAnonymous
                || !string.Equals(existing.RequiredPermission, requiredPermission, StringComparison.Ordinal)
                || !existing.RequiredAccessGroups.OrderBy(g => g, StringComparer.Ordinal)
                    .SequenceEqual(options.RequiredAccessGroups.OrderBy(g => g, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Route '{normalized}' is already registered with a different auth posture " +
                    "(AllowAnonymous / RequiredPermission / RequiredAccessGroups). " +
                    "Merge the registrations into one handler with matching options, or use distinct paths.");

            var existingHandler = existing.Handler;
            var existingMethods = existing.Methods;
            var newHandler = handler;
            WebRouteHandler dispatcher = ctx =>
                existingMethods.Contains(ctx.HttpContext.Request.Method)
                    ? existingHandler(ctx)
                    : newHandler(ctx);

            var combinedMethods = new HashSet<string>(existing.Methods, StringComparer.OrdinalIgnoreCase);
            foreach (var m in newMethods) combinedMethods.Add(m);

            var newDesc = string.IsNullOrWhiteSpace(options.Description) ? null : options.Description;
            var mergedDescription = newDesc is null || newDesc == existing.Description
                ? existing.Description
                : $"{existing.Description} | {newDesc}";

            _routes[normalized] = new WebRouteDefinition
            {
                Path = normalized,
                Kind = existing.Kind,
                Handler = dispatcher,
                Methods = combinedMethods,
                Contributor = existing.Contributor,
                Name = existing.Name,
                Description = mergedDescription,
                Tags = existing.Tags,
                RequiredAccessGroups = existing.RequiredAccessGroups,
                RequiredPermission = existing.RequiredPermission,
                AllowAnonymous = existing.AllowAnonymous,
                Middleware = existing.Middleware,
                OutputCache = existing.OutputCache
            };
            return;
        }

        _routes[normalized] = new WebRouteDefinition
        {
            Path = normalized,
            Kind = kind,
            Handler = handler,
            Methods = newMethods,
            Contributor = contributor,
            Name = string.IsNullOrWhiteSpace(options.Name) ? normalized : options.Name,
            Description = options.Description,
            Tags = options.Tags,
            RequiredAccessGroups = options.RequiredAccessGroups,
            RequiredPermission = string.IsNullOrWhiteSpace(options.RequiredPermission) ? null : options.RequiredPermission,
            AllowAnonymous = options.AllowAnonymous,
            Middleware = options.Middleware ?? [],
            OutputCache = options.OutputCache
        };
    }

    public void RegisterFallback(
        WebRouteHandler handler,
        WebContributorDescriptor contributor,
        WebRouteOptions options,
        params string[] methods)
    {
        _fallback = new WebRouteDefinition
        {
            Path = "*",
            Kind = WebRouteKind.Fallback,
            Handler = handler,
            Methods = NormalizeMethods(methods),
            Contributor = contributor,
            Name = string.IsNullOrWhiteSpace(options.Name) ? "Fallback" : options.Name,
            Description = options.Description,
            Tags = options.Tags,
            RequiredAccessGroups = options.RequiredAccessGroups,
            RequiredPermission = string.IsNullOrWhiteSpace(options.RequiredPermission) ? null : options.RequiredPermission,
            AllowAnonymous = options.AllowAnonymous
        };
    }

    public bool TryGet(string path, out WebRouteDefinition? route) =>
        TryMatch(NormalizePath(path), out route);

    public IReadOnlyList<WebRouteDefinition> GetAllRoutes() =>
        _routes.Values
            .OrderBy(static route => route.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<WebContributorSummary> GetContributors()
    {
        var routeCounts = _routes.Values
            .Append(_fallback)
            .Where(static route => route is not null)
            .GroupBy(static route => route!.Contributor.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sample = group.First()!;
                return new WebContributorSummary
                {
                    Id = sample.Contributor.Id,
                    Name = sample.Contributor.Name,
                    Kind = sample.Contributor.Kind,
                    Description = sample.Contributor.Description,
                    RouteCount = group.Count()
                };
            })
            .OrderBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return routeCounts;
    }

    public IReadOnlyList<WebRouteDescriptor> GetRouteDescriptors()
    {
        return _routes.Values
            .Append(_fallback)
            .Where(static route => route is not null)
            .Select(static route => new WebRouteDescriptor
            {
                Path = route!.Path,
                Kind = route.Kind.ToString(),
                Methods = route.Methods.OrderBy(static method => method, StringComparer.OrdinalIgnoreCase).ToArray(),
                SourceId = route.Contributor.Id,
                SourceName = route.Contributor.Name,
                SourceKind = route.Contributor.Kind,
                Name = route.Name,
                Description = route.Description,
                Tags = route.Tags,
                RequiredAccessGroups = route.RequiredAccessGroups,
                RequiredPermission = route.RequiredPermission,
                AllowAnonymous = route.AllowAnonymous
            })
            .OrderBy(static route => route.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        if (normalized.Length > 1 && normalized.EndsWith('/'))
            normalized = normalized[..^1];

        return normalized;
    }

    private bool TryMatch(string normalizedPath, out WebRouteDefinition? route)
    {
        if (_routes.TryGetValue(normalizedPath, out route))
            return true;

        route = _routes.Values
            .Where(static candidate => candidate.Path.Contains('*', StringComparison.Ordinal))
            .Where(candidate => IsWildcardMatch(candidate.Path, normalizedPath))
            .OrderByDescending(candidate => candidate.Path.Length)
            .FirstOrDefault();

        return route is not null;
    }

    private static bool IsWildcardMatch(string pattern, string path)
    {
        if (string.Equals(pattern, "*", StringComparison.Ordinal))
            return true;

        if (!pattern.Contains('*', StringComparison.Ordinal))
            return false;

        var wildcardIndex = pattern.IndexOf('*');
        if (wildcardIndex < 0)
            return false;

        var prefix = pattern[..wildcardIndex];
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> NormalizeMethods(string[] methods)
    {
        if (methods.Length == 0)
            methods = ["GET"];

        return methods
            .Select(static m => m.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static WebContributorDescriptor BuiltInContributor(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Kind = "Application",
        Description = string.Empty
    };
}
