using System.Collections;
using System.Globalization;
using CL.WebLogic.Runtime;
using CL.WebLogic.Templates.Ast;

namespace CL.WebLogic.Theming;

/// <summary>
/// The single runtime implementation of template value semantics — selector
/// resolution, filters, encoding, truthiness, comparison, loop metadata and
/// legacy tokens. Called by BOTH the AST interpreter and generator-emitted
/// compiled templates, which is what guarantees the two render identically.
/// Forwards to the legacy <see cref="ThemeManager"/> statics where the regex
/// pipeline already defined the behaviour.
/// </summary>
public static class TemplateRuntime
{
    /// <summary>Resolves a selector against the context. Mirrors the legacy scope semantics exactly.</summary>
    public static async Task<object?> ResolveAsync(WebTemplateContext ctx, TemplateScope scope, string key, string? rawScope)
    {
        switch (scope)
        {
            case TemplateScope.None:
                return ctx.Model.TryGetValue(key, out var direct) ? direct : null;
            case TemplateScope.Model:
                return ThemeManager.ResolvePathValue(ctx.Model, key);
            case TemplateScope.Item:
                return ThemeManager.ResolvePathValue(ctx.CurrentItem, key);
            case TemplateScope.Page:
                return ThemeManager.ResolvePageValue(ctx.PageContext, key);
            case TemplateScope.Query:
                return ctx.PageContext?.GetQuery(key);
            case TemplateScope.Cookie:
                return ctx.PageContext?.GetCookie(key);
            case TemplateScope.Session:
                return ctx.PageContext?.GetSessionValue(key);
            case TemplateScope.Header:
                return ctx.PageContext?.GetHeader(key);
            case TemplateScope.Form:
                return ctx.PageContext is null
                    ? null
                    : await ctx.PageContext.GetFormValueAsync(key).ConfigureAwait(false);
            case TemplateScope.Route:
                return ThemeManager.ResolveRouteValue(ctx.PageContext?.Route, key);
            case TemplateScope.Param:
                return ctx.Params is null ? null : ThemeManager.ResolvePathValue(ctx.Params, key);
            case TemplateScope.Loop:
                return LoopValue(key, ctx.Loop);
            case TemplateScope.Unknown:
                // Unknown scope words resolve to an active {foreach … as alias}
                // binding when one matches, otherwise empty (legacy parity).
                if (rawScope is not null && ctx.Aliases is not null &&
                    ctx.Aliases.TryGetValue(rawScope, out var aliased))
                    return ThemeManager.ResolvePathValue(aliased, key);
                return null;
            default:
                return null;
        }
    }

    public static object? ApplyFilters(object? value, (string Name, string? Arg)[] filters) =>
        ThemeManager.ApplyFilters(value, filters);

    public static string Format(object? value, bool encode) =>
        ThemeManager.FormatValue(value, encode);

    public static bool Truthy(object? value) => ThemeManager.IsTruthy(value);

    public static object? LoopValue(string key, TemplateLoopState? loop)
    {
        if (loop is null)
            return null;

        return key.Trim().ToLowerInvariant() switch
        {
            "index" => loop.Index,
            "number" => loop.Number,
            "first" => loop.First,
            "last" => loop.Last,
            "count" => loop.Count,
            "even" => loop.Even,
            "odd" => loop.Odd,
            _ => null
        };
    }

    /// <summary>Materializes a foreach source: strings and non-enumerables yield no items.</summary>
    public static IReadOnlyList<object?> AsItems(object? source)
    {
        if (source is string || source is not IEnumerable enumerable)
            return System.Array.Empty<object?>();
        return enumerable.Cast<object?>().ToList();
    }

    public static IReadOnlyDictionary<string, object?> WithAlias(
        IReadOnlyDictionary<string, object?>? aliases, string name, object? item)
    {
        var dict = aliases is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(aliases, StringComparer.OrdinalIgnoreCase);
        dict[name] = item;
        return dict;
    }

    /// <summary>Numeric-aware comparison for <c>{if:a gt b}</c>; falls back to case-insensitive string compare.</summary>
    public static bool Compare(object? left, CompareOp op, object? right)
    {
        var ln = ToNumber(left);
        var rn = ToNumber(right);
        int result;
        if (ln.HasValue && rn.HasValue)
            result = ln.Value.CompareTo(rn.Value);
        else
            result = string.Compare(left?.ToString() ?? string.Empty, right?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            CompareOp.Eq => result == 0,
            CompareOp.Ne => result != 0,
            CompareOp.Gt => result > 0,
            CompareOp.Gte => result >= 0,
            CompareOp.Lt => result < 0,
            CompareOp.Lte => result <= 0,
            _ => false
        };
    }

    public static bool IsAuthenticated(WebTemplateContext ctx) =>
        ctx.PageContext?.IsAuthenticated == true;

    public static bool HasAnyAccessGroup(WebTemplateContext ctx, string[] groups) =>
        ctx.PageContext?.HasAnyAccessGroup(groups) == true;

    public static bool HasPermission(WebTemplateContext ctx, string permission) =>
        ctx.PageContext?.HasPermission(permission) == true;

    /// <summary>Switch subject normalization (string, case-insensitive matching is done by the caller).</summary>
    public static string SwitchSubject(object? value) => value?.ToString() ?? string.Empty;

    /// <summary>
    /// Legacy <c>{{dotted}}</c> token semantics: dotted paths resolve via
    /// page./item./model; plain keys do a direct model lookup and, when missing,
    /// emit the literal token back (legacy behaviour).
    /// </summary>
    public static string LegacyToken(WebTemplateContext ctx, string token)
    {
        if (token.Contains('.'))
        {
            object? value;
            if (token.StartsWith("page.", StringComparison.OrdinalIgnoreCase))
                value = ThemeManager.ResolvePageValue(ctx.PageContext, token.Substring("page.".Length));
            else if (token.StartsWith("item.", StringComparison.OrdinalIgnoreCase))
                value = ThemeManager.ResolvePathValue(ctx.CurrentItem, token.Substring("item.".Length));
            else
                value = ThemeManager.ResolvePathValue(ctx.Model, token);

            return ThemeManager.FormatValue(value, encode: true);
        }

        return ctx.Model.TryGetValue(token, out var modelValue)
            ? ThemeManager.FormatValue(modelValue, encode: true)
            : "{{" + token + "}}";
    }

    public static string RenderHead(WebTemplateContext ctx) =>
        WebPageHeadRenderer.Render(ctx.Meta, ctx.Model);

    public static string Section(WebTemplateContext ctx, string name) =>
        ctx.Sections.TryGetValue(name, out var value) ? value : string.Empty;

    private static double? ToNumber(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => l,
        double d => d,
        decimal m => (double)m,
        float f => f,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };
}
