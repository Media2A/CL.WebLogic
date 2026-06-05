using System;
using System.Collections.Generic;
using System.Text;
using CL.WebLogic.Templates.Ast;

namespace CL.WebLogic.Templates.Parsing;

/// <summary>
/// Recursive-descent parser that turns WebLogic template text into a
/// <see cref="TemplateDocument"/> AST. It reproduces the legacy
/// <c>ThemeManager</c> regex grammar (layout / section / partial / widget /
/// foreach / if / tokens / filters / legacy <c>{{…}}</c> / csrf) and adds the
/// AST-era constructs (<c>{verbatim}</c>, parameterised partials, loop aliasing,
/// <c>{else}</c>/<c>{elseif}</c>, comparisons, <c>{switch}</c>).
/// <para>
/// Fidelity rule that matters for byte-parity: a <c>{</c> only starts a directive
/// when a known keyword/scope follows it immediately (no leading space), exactly
/// like the anchored legacy regexes. Anything else is literal text — so inline
/// CSS/JS braces behave as they did before (and can be made literal explicitly
/// with <c>{verbatim}</c>).
/// </para>
/// </summary>
public sealed class TemplateParser
{
    private readonly string _src;
    private int _pos;
    private readonly List<SectionDefinition> _sections = new();

    private TemplateParser(string source) => _src = source ?? string.Empty;

    /// <summary>Parses a full template (honouring a leading <c>{layout:…}</c> directive).</summary>
    public static TemplateDocument Parse(string source)
    {
        var parser = new TemplateParser(source);
        return parser.ParseDocument();
    }

    /// <summary>Parses a fragment (no layout/section semantics) — used for inline attribute values.</summary>
    public static IReadOnlyList<TemplateNode> ParseFragment(string source)
    {
        var parser = new TemplateParser(source);
        var nodes = parser.ParseNodes(StopKind.Document, out _);
        return nodes;
    }

    private TemplateDocument ParseDocument()
    {
        var layout = TryConsumeLayout();
        var nodes = new List<TemplateNode>();

        // Top level never "stops" on a marker; any stray close/branch marker is
        // emitted as literal text (matching the legacy unmatched-tag behaviour).
        while (_pos < _src.Length)
        {
            nodes.AddRange(ParseNodes(StopKind.Document, out var terminator));
            if (terminator is null)
                break;
            nodes.Add(new LiteralNode("{" + terminator + "}"));
        }

        return new TemplateDocument(layout, nodes, _sections);
    }

    private string? TryConsumeLayout()
    {
        var i = 0;
        while (i < _src.Length && char.IsWhiteSpace(_src[i])) i++;
        if (i >= _src.Length || !StartsAt(i, "{layout:"))
            return null;

        var close = _src.IndexOf('}', i);
        if (close < 0)
            return null;

        var body = _src.Substring(i + "{layout:".Length, close - (i + "{layout:".Length)).Trim();
        var j = close + 1;
        while (j < _src.Length && char.IsWhiteSpace(_src[j])) j++;
        _pos = j;
        return body;
    }

    private enum StopKind { Document, Foreach, Section, IfBody, IfNotBody, SwitchBody, CaseBody, DefaultBody }

    private List<TemplateNode> ParseNodes(StopKind stop, out string? terminator)
    {
        terminator = null;
        var nodes = new List<TemplateNode>();
        var literal = new StringBuilder();

        void Flush()
        {
            if (literal.Length > 0)
            {
                nodes.Add(new LiteralNode(literal.ToString()));
                literal.Clear();
            }
        }

        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c != '{')
            {
                literal.Append(c);
                _pos++;
                continue;
            }

            // Legacy {{dotted}}
            if (_pos + 1 < _src.Length && _src[_pos + 1] == '{')
            {
                if (TryReadLegacy(out var dotted, out var afterLegacy))
                {
                    Flush();
                    nodes.Add(new LegacyTokenNode(dotted));
                    _pos = afterLegacy;
                    continue;
                }
                literal.Append('{');
                _pos++;
                continue;
            }

            var close = _src.IndexOf('}', _pos + 1);
            if (close < 0)
            {
                literal.Append('{');
                _pos++;
                continue;
            }

            var body = _src.Substring(_pos + 1, close - _pos - 1);
            var tagEnd = close + 1;

            // Control-flow markers: stop if this context owns them, else literal.
            if (IsMarker(body))
            {
                if (IsStop(stop, body))
                {
                    Flush();
                    terminator = body;
                    _pos = tagEnd;
                    return nodes;
                }
                literal.Append('{').Append(body).Append('}');
                _pos = tagEnd;
                continue;
            }

            // Exact keyword nodes.
            if (Eq(body, "renderbody")) { Flush(); nodes.Add(new RenderBodyNode()); _pos = tagEnd; continue; }
            if (Eq(body, "renderhead")) { Flush(); nodes.Add(new RenderHeadNode()); _pos = tagEnd; continue; }
            if (Eq(body, "csrf")) { Flush(); nodes.Add(new CsrfNode(CsrfKind.Field)); _pos = tagEnd; continue; }
            if (Eq(body, "csrf_token")) { Flush(); nodes.Add(new CsrfNode(CsrfKind.Token)); _pos = tagEnd; continue; }
            if (Eq(body, "csrf_meta")) { Flush(); nodes.Add(new CsrfNode(CsrfKind.Meta)); _pos = tagEnd; continue; }

            // Verbatim — raw copy with no token processing.
            if (Eq(body, "verbatim"))
            {
                Flush();
                _pos = tagEnd;
                nodes.Add(ReadVerbatim());
                continue;
            }

            // Openers with arguments.
            if (TryRest(body, "section:", out var sectionRest)) { Flush(); _pos = tagEnd; ParseSectionInto(sectionRest); continue; }
            if (TryRest(body, "rendersection:", out var rsRest)) { Flush(); nodes.Add(new RenderSectionNode(rsRest.Trim())); _pos = tagEnd; continue; }
            if (TryRest(body, "foreach:", out var feRest)) { Flush(); _pos = tagEnd; nodes.Add(ParseForeach(feRest)); continue; }
            if (TryRest(body, "ifnot:", out var ifnotRest)) { Flush(); _pos = tagEnd; nodes.Add(ParseIf(ifnotRest, negate: true)); continue; }
            if (TryRest(body, "if:", out var ifRest)) { Flush(); _pos = tagEnd; nodes.Add(ParseIf(ifRest, negate: false)); continue; }
            if (TryRest(body, "switch:", out var swRest)) { Flush(); _pos = tagEnd; nodes.Add(ParseSwitch(swRest)); continue; }
            if (TryRest(body, "widgetarea:", out var waRest)) { Flush(); nodes.Add(new WidgetAreaNode(waRest.Trim())); _pos = tagEnd; continue; }
            if (TryRest(body, "widget:", out var wRest)) { Flush(); nodes.Add(ParseWidget(wRest)); _pos = tagEnd; continue; }
            if (TryRest(body, "partial:", out var pRest)) { Flush(); nodes.Add(ParsePartial(pRest)); _pos = tagEnd; continue; }
            if (TryRest(body, "raw:", out var rawRest)) { Flush(); nodes.Add(MakeToken(rawRest, raw: true)); _pos = tagEnd; continue; }

            // Generic token ({scope:rest|filters}).
            if (LooksLikeToken(body)) { Flush(); nodes.Add(MakeToken(body, raw: false)); _pos = tagEnd; continue; }

            // Unrecognised "{" — emit literally and rescan one char on (so an inner
            // "{" still gets a chance to start a directive, like the regex engine).
            literal.Append('{');
            _pos++;
        }

        Flush();
        return nodes;
    }

    private ForeachNode ParseForeach(string rest)
    {
        string source = rest.Trim();
        string? alias = null;

        var lower = source.ToLowerInvariant();
        var asIdx = lower.IndexOf(" as ", StringComparison.Ordinal);
        if (asIdx >= 0)
        {
            alias = source.Substring(asIdx + 4).Trim();
            source = source.Substring(0, asIdx).Trim();
        }

        var body = ParseNodes(StopKind.Foreach, out _);
        return new ForeachNode(SelectorSyntax.ParseSelector(source), string.IsNullOrEmpty(alias) ? null : alias, body);
    }

    private void ParseSectionInto(string rest)
    {
        var name = rest.Trim();
        var body = ParseNodes(StopKind.Section, out _);
        _sections.Add(new SectionDefinition(name, body));
    }

    private IfNode ParseIf(string rest, bool negate)
    {
        TemplateCondition cond = SelectorSyntax.ParseCondition(rest);
        if (negate)
            cond = new NotCondition(cond);

        // {if:…} closes with {/if}; {ifnot:…} closes with {/ifnot} — two
        // independent balanced pairs, exactly like the legacy engine.
        var stop = negate ? StopKind.IfNotBody : StopKind.IfBody;

        var branches = new List<ConditionalBranch>();
        IReadOnlyList<TemplateNode>? elseBody = null;

        var body = ParseNodes(stop, out var term);
        branches.Add(new ConditionalBranch(cond, body));

        while (term is not null && StartsWith(term, "elseif:"))
        {
            var c = SelectorSyntax.ParseCondition(term.Substring("elseif:".Length));
            var b = ParseNodes(stop, out term);
            branches.Add(new ConditionalBranch(c, b));
        }

        if (term is not null && Eq(term, "else"))
            elseBody = ParseNodes(stop, out term);

        return new IfNode(branches, elseBody);
    }

    private SwitchNode ParseSwitch(string rest)
    {
        var subject = SelectorSyntax.ParseSelector(rest.Trim());
        var cases = new List<SwitchCase>();
        IReadOnlyList<TemplateNode>? defaultBody = null;

        ParseNodes(StopKind.SwitchBody, out var term); // consume lead (whitespace)

        while (term is not null && !Eq(term, "/switch"))
        {
            if (StartsWith(term, "case:"))
            {
                var value = term.Substring("case:".Length).Trim().Trim('"').Trim('\'');
                var body = ParseNodes(StopKind.CaseBody, out var t);
                cases.Add(new SwitchCase(value, body));
                if (t is not null && Eq(t, "/case"))
                    ParseNodes(StopKind.SwitchBody, out term);
                else
                    term = t;
            }
            else if (Eq(term, "default"))
            {
                var body = ParseNodes(StopKind.DefaultBody, out var t);
                defaultBody = body;
                if (t is not null && Eq(t, "/default"))
                    ParseNodes(StopKind.SwitchBody, out term);
                else
                    term = t;
            }
            else
            {
                break;
            }
        }

        return new SwitchNode(subject, cases, defaultBody);
    }

    private PartialNode ParsePartial(string rest)
    {
        var (path, attrPart) = SplitFirstToken(rest);
        var args = new List<PartialArgument>();
        foreach (var (name, value, quoted) in ParseAttributes(attrPart))
        {
            args.Add(quoted
                ? PartialArgument.FromLiteral(name, value)
                : PartialArgument.FromSelector(name, SelectorSyntax.ParseSelector(value)));
        }
        return new PartialNode(path, args);
    }

    private WidgetNode ParseWidget(string rest)
    {
        var (name, attrPart) = SplitFirstToken(rest);
        var attrs = new List<WidgetAttribute>();
        foreach (var (attrName, value, _) in ParseAttributes(attrPart))
            attrs.Add(new WidgetAttribute(attrName, ParseFragment(value)));
        return new WidgetNode(name, attrs);
    }

    private VerbatimNode ReadVerbatim()
    {
        const string closeTag = "{/verbatim}";
        var idx = IndexOfOrdinalIgnoreCase(_src, closeTag, _pos);
        if (idx < 0)
        {
            var rest = _src.Substring(_pos);
            _pos = _src.Length;
            return new VerbatimNode(rest);
        }

        var text = _src.Substring(_pos, idx - _pos);
        _pos = idx + closeTag.Length;
        return new VerbatimNode(text);
    }

    private static TokenNode MakeToken(string expression, bool raw)
    {
        var (selectorText, filters) = SelectorSyntax.ParseExpression(expression.Trim());
        return new TokenNode(SelectorSyntax.ParseSelector(selectorText), filters, raw);
    }

    private bool TryReadLegacy(out string dotted, out int after)
    {
        dotted = string.Empty;
        after = _pos;
        var i = _pos + 2; // skip "{{"
        var start = i;
        while (i < _src.Length && IsLegacyChar(_src[i])) i++;
        if (i == start) return false;
        if (i + 1 >= _src.Length || _src[i] != '}' || _src[i + 1] != '}') return false;
        dotted = _src.Substring(start, i - start);
        after = i + 2;
        return true;
    }

    // ── classification helpers ─────────────────────────────────────────────

    private static bool IsMarker(string body) =>
        Eq(body, "/foreach") || Eq(body, "/if") || Eq(body, "/ifnot") || Eq(body, "/switch") ||
        Eq(body, "/section") || Eq(body, "/case") || Eq(body, "/default") ||
        Eq(body, "else") || Eq(body, "default") ||
        StartsWith(body, "elseif:") || StartsWith(body, "case:");

    private static bool IsStop(StopKind stop, string body) => stop switch
    {
        StopKind.Foreach => Eq(body, "/foreach"),
        StopKind.Section => Eq(body, "/section"),
        StopKind.IfBody => Eq(body, "/if") || Eq(body, "else") || StartsWith(body, "elseif:"),
        StopKind.IfNotBody => Eq(body, "/ifnot") || Eq(body, "else") || StartsWith(body, "elseif:"),
        StopKind.SwitchBody => Eq(body, "/switch") || Eq(body, "default") || StartsWith(body, "case:"),
        StopKind.CaseBody => Eq(body, "/case") || Eq(body, "/switch") || Eq(body, "default") || StartsWith(body, "case:"),
        StopKind.DefaultBody => Eq(body, "/default") || Eq(body, "/switch") || Eq(body, "default") || StartsWith(body, "case:"),
        _ => false
    };

    private static bool LooksLikeToken(string body)
    {
        var colon = body.IndexOf(':');
        if (colon <= 0 || colon == body.Length - 1)
            return false;
        for (var i = 0; i < colon; i++)
            if (!IsAsciiLetter(body[i]))
                return false;
        return true;
    }

    private static bool TryRest(string body, string prefix, out string rest)
    {
        if (StartsWith(body, prefix))
        {
            rest = body.Substring(prefix.Length);
            return true;
        }
        rest = string.Empty;
        return false;
    }

    private static (string First, string Remainder) SplitFirstToken(string text)
    {
        var s = text.TrimStart();
        var sp = 0;
        while (sp < s.Length && !char.IsWhiteSpace(s[sp])) sp++;
        var first = s.Substring(0, sp);
        var rest = sp < s.Length ? s.Substring(sp + 1) : string.Empty;
        return (first.Trim(), rest);
    }

    /// <summary>Parses <c>name="value"</c> / <c>name=value</c> attribute pairs.</summary>
    private static IEnumerable<(string Name, string Value, bool Quoted)> ParseAttributes(string text)
    {
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n) break;

            var nameStart = i;
            while (i < n && text[i] != '=' && !char.IsWhiteSpace(text[i])) i++;
            var name = text.Substring(nameStart, i - nameStart);
            if (string.IsNullOrEmpty(name)) { i++; continue; }

            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n || text[i] != '=') { yield return (name, string.Empty, true); continue; }
            i++; // skip '='
            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n) { yield return (name, string.Empty, true); break; }

            if (text[i] == '"' || text[i] == '\'')
            {
                var quote = text[i++];
                var valStart = i;
                while (i < n && text[i] != quote) i++;
                var value = text.Substring(valStart, Math.Min(i, n) - valStart);
                if (i < n) i++; // closing quote
                yield return (name, value, true);
            }
            else
            {
                var valStart = i;
                while (i < n && !char.IsWhiteSpace(text[i])) i++;
                yield return (name, text.Substring(valStart, i - valStart), false);
            }
        }
    }

    private bool StartsAt(int index, string value) =>
        index + value.Length <= _src.Length &&
        string.Compare(_src, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool Eq(string body, string keyword) =>
        string.Equals(body, keyword, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string body, string prefix) =>
        body.Length >= prefix.Length &&
        string.Compare(body, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsLegacyChar(char c) =>
        IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';

    private static int IndexOfOrdinalIgnoreCase(string haystack, string needle, int start) =>
        haystack.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
}
