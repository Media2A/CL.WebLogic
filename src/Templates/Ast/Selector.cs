namespace CL.WebLogic.Templates.Ast;

/// <summary>
/// The resolution scope of a template selector. Mirrors the scope prefixes the
/// legacy <c>ThemeManager</c> regex engine understood (<c>model:</c>, <c>page:</c>,
/// …) plus the scopes introduced by the AST engine (<c>param:</c>, <c>loop:</c>).
/// </summary>
public enum TemplateScope
{
    /// <summary>No explicit scope — a bare selector resolved as a direct top-level model key.</summary>
    None,
    Model,
    Page,
    Query,
    Cookie,
    Session,
    Header,
    Form,
    Route,
    Item,
    /// <summary>Parameters passed into a parameterised partial.</summary>
    Param,
    /// <summary>Loop metadata inside a <c>{foreach}</c> (index / number / first / last / count / even / odd).</summary>
    Loop,
    /// <summary>
    /// A <c>word:rest</c> token whose scope word isn't recognised. The legacy regex
    /// engine matched these as tokens and resolved them to empty; preserving that
    /// keeps byte-parity (e.g. an inline <c>{color:red}</c> in CSS renders empty).
    /// </summary>
    Unknown
}

/// <summary>
/// A parsed selector such as <c>model:user.name</c>, <c>item:score</c>, or a bare
/// <c>rows</c>. <see cref="Key"/> is everything after the scope prefix and may be a
/// dotted path (walked by the runtime against dictionaries then reflection).
/// </summary>
public sealed class TemplateSelector
{
    public TemplateSelector(TemplateScope scope, string key, string? rawScope = null)
    {
        Scope = scope;
        Key = key ?? string.Empty;
        RawScope = rawScope;
    }

    public TemplateScope Scope { get; }

    /// <summary>The path after the scope prefix, e.g. <c>user.name</c>. Empty for scope-only selectors.</summary>
    public string Key { get; }

    /// <summary>
    /// The scope word exactly as written (e.g. <c>row</c>), or null when scopeless.
    /// Needed so an <see cref="TemplateScope.Unknown"/> scope can still be matched
    /// against an active <c>{foreach … as row}</c> alias at render time.
    /// </summary>
    public string? RawScope { get; }

    public override string ToString() => Scope == TemplateScope.None ? Key : $"{RawScope ?? Scope.ToString()}:{Key}";
}

/// <summary>A single filter in a token's pipe chain, e.g. <c>truncate:100</c> or <c>uppercase</c>.</summary>
public sealed class TemplateFilter
{
    public TemplateFilter(string name, string? arg)
    {
        Name = name;
        Arg = arg;
    }

    public string Name { get; }
    public string? Arg { get; }
}
