using CL.WebLogic.Templates.Ast;

namespace CL.WebLogic.Templates.Parsing;

/// <summary>
/// Parsing helpers for the small expression grammars embedded in tokens and
/// conditions — selectors (<c>scope:path</c>), filter chains (<c>|name:arg</c>),
/// and <c>{if:…}</c> conditions. Kept separate from the block parser so the rules
/// match the legacy <c>ThemeManager</c> exactly.
/// </summary>
internal static class SelectorSyntax
{
    /// <summary>Parses <c>scope:path</c> (or a bare <c>path</c>) into a selector.</summary>
    public static TemplateSelector ParseSelector(string raw)
    {
        var selector = (raw ?? string.Empty).Trim();
        var sep = selector.IndexOf(':');
        if (sep < 0)
            return new TemplateSelector(TemplateScope.None, selector);

        var scopeWord = selector.Substring(0, sep);
        var key = selector.Substring(sep + 1);
        return new TemplateSelector(MapScope(scopeWord), key, scopeWord.Trim());
    }

    public static TemplateScope MapScope(string scopeWord) => scopeWord.Trim().ToLowerInvariant() switch
    {
        "model" => TemplateScope.Model,
        "page" => TemplateScope.Page,
        "query" => TemplateScope.Query,
        "cookie" => TemplateScope.Cookie,
        "session" => TemplateScope.Session,
        "header" => TemplateScope.Header,
        "form" => TemplateScope.Form,
        "route" => TemplateScope.Route,
        "item" => TemplateScope.Item,
        "param" => TemplateScope.Param,
        "loop" => TemplateScope.Loop,
        _ => TemplateScope.Unknown
    };

    /// <summary>Whether a word is a recognised scope (used to disambiguate comparison operands).</summary>
    public static bool IsKnownScope(string scopeWord) => MapScope(scopeWord) != TemplateScope.Unknown;

    /// <summary>
    /// Splits a token expression into its selector and filter chain. Mirrors
    /// <c>ThemeManager.ParseFilters</c>: the first pipe-delimited segment is the
    /// selector, the rest are filters; pipes inside quotes are ignored.
    /// </summary>
    public static (string Selector, IReadOnlyList<TemplateFilter> Filters) ParseExpression(string expression)
    {
        var pipe = expression.IndexOf('|');
        if (pipe < 0)
            return (expression.Trim(), System.Array.Empty<TemplateFilter>());

        var selector = expression.Substring(0, pipe).Trim();
        var filterPart = expression.Substring(pipe + 1);
        var filters = new List<TemplateFilter>();

        foreach (var raw in SplitFilterChain(filterPart))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon < 0)
            {
                filters.Add(new TemplateFilter(trimmed, null));
            }
            else
            {
                var name = trimmed.Substring(0, colon).Trim();
                var arg = trimmed.Substring(colon + 1).Trim().Trim('"').Trim('\'');
                filters.Add(new TemplateFilter(name, arg));
            }
        }

        return (selector, filters);
    }

    private static IEnumerable<string> SplitFilterChain(string input)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"' || c == '\'')
                depth = depth == 0 ? 1 : 0;
            else if (c == '|' && depth == 0)
            {
                yield return input.Substring(start, i - start);
                start = i + 1;
            }
        }

        if (start < input.Length)
            yield return input.Substring(start);
    }

    /// <summary>
    /// Parses an <c>{if:…}</c> condition expression. Supports <c>auth</c>,
    /// <c>accessgroup:…</c>, <c>permission:…</c>, comparison (<c>lhs op rhs</c>) and
    /// plain selector truthiness.
    /// </summary>
    public static TemplateCondition ParseCondition(string expression)
    {
        var expr = (expression ?? string.Empty).Trim();

        if (string.Equals(expr, "auth", System.StringComparison.OrdinalIgnoreCase))
            return new AuthCondition();

        if (expr.StartsWith("accessgroup:", System.StringComparison.OrdinalIgnoreCase))
            return new AccessGroupCondition(SplitValues(expr.Substring("accessgroup:".Length)));

        if (expr.StartsWith("permission:", System.StringComparison.OrdinalIgnoreCase))
            return new PermissionCondition(expr.Substring("permission:".Length).Trim());

        if (TryParseComparison(expr, out var comparison))
            return comparison!;

        return new TruthyCondition(ParseSelector(expr));
    }

    private static bool TryParseComparison(string expr, out CompareCondition? condition)
    {
        condition = null;
        var parts = expr.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!TryParseOp(parts[1], out var op))
            return false;

        var left = ParseSelector(parts[0]);
        var right = ParseOperand(parts[2]);
        condition = new CompareCondition(left, op, right);
        return true;
    }

    private static bool TryParseOp(string word, out CompareOp op)
    {
        switch (word.Trim().ToLowerInvariant())
        {
            case "eq": op = CompareOp.Eq; return true;
            case "ne": op = CompareOp.Ne; return true;
            case "gt": op = CompareOp.Gt; return true;
            case "gte": op = CompareOp.Gte; return true;
            case "lt": op = CompareOp.Lt; return true;
            case "lte": op = CompareOp.Lte; return true;
            default: op = CompareOp.Eq; return false;
        }
    }

    private static TemplateOperand ParseOperand(string raw)
    {
        var token = raw.Trim();

        // Quoted literal.
        if (token.Length >= 2 &&
            ((token[0] == '"' && token[token.Length - 1] == '"') ||
             (token[0] == '\'' && token[token.Length - 1] == '\'')))
            return TemplateOperand.FromLiteral(token.Substring(1, token.Length - 2));

        // scope:path → selector (only when the scope word is recognised, so a
        // bare "12:30"-style literal isn't mistaken for a selector).
        var sep = token.IndexOf(':');
        if (sep > 0 && IsKnownScope(token.Substring(0, sep)))
            return TemplateOperand.FromSelector(ParseSelector(token));

        return TemplateOperand.FromLiteral(token);
    }

    public static IReadOnlyList<string> SplitValues(string value) =>
        value.Split(new[] { ',', ';', '|' }, System.StringSplitOptions.RemoveEmptyEntries)
             .Select(v => v.Trim())
             .Where(v => v.Length > 0)
             .ToList();
}
