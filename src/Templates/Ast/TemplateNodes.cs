namespace CL.WebLogic.Templates.Ast;

/// <summary>Base type for every node in a parsed template tree.</summary>
public abstract class TemplateNode
{
}

/// <summary>
/// A fully parsed template: an optional layout directive, the body nodes (with
/// <c>{section}</c> definitions removed and collected into <see cref="Sections"/>),
/// and the section definitions the layout can pull via <c>{rendersection}</c>.
/// </summary>
public sealed class TemplateDocument
{
    public TemplateDocument(
        string? layoutPath,
        IReadOnlyList<TemplateNode> nodes,
        IReadOnlyList<SectionDefinition> sections)
    {
        LayoutPath = layoutPath;
        Nodes = nodes;
        Sections = sections;
    }

    /// <summary>Normalized layout path from a leading <c>{layout:…}</c>, or null.</summary>
    public string? LayoutPath { get; }

    public IReadOnlyList<TemplateNode> Nodes { get; }

    public IReadOnlyList<SectionDefinition> Sections { get; }
}

/// <summary>A named <c>{section:name}…{/section}</c> block captured for the layout.</summary>
public sealed class SectionDefinition
{
    public SectionDefinition(string name, IReadOnlyList<TemplateNode> body)
    {
        Name = name;
        Body = body;
    }

    public string Name { get; }
    public IReadOnlyList<TemplateNode> Body { get; }
}

/// <summary>Raw HTML/text emitted verbatim (after which no token processing occurs).</summary>
public sealed class LiteralNode : TemplateNode
{
    public LiteralNode(string text) => Text = text;
    public string Text { get; }
}

/// <summary>
/// A <c>{scope:selector|filter:arg|…}</c> token. When <see cref="Raw"/> is true the
/// resolved value is emitted without HTML-encoding (<c>{raw:…}</c>).
/// </summary>
public sealed class TokenNode : TemplateNode
{
    public TokenNode(TemplateSelector selector, IReadOnlyList<TemplateFilter> filters, bool raw)
    {
        Selector = selector;
        Filters = filters;
        Raw = raw;
    }

    public TemplateSelector Selector { get; }
    public IReadOnlyList<TemplateFilter> Filters { get; }
    public bool Raw { get; }
}

/// <summary>Legacy <c>{{dotted.path}}</c> model token (always HTML-encoded).</summary>
public sealed class LegacyTokenNode : TemplateNode
{
    public LegacyTokenNode(string dotted) => Dotted = dotted;
    public string Dotted { get; }
}

public enum CsrfKind
{
    /// <summary><c>{csrf}</c> — a hidden input.</summary>
    Field,
    /// <summary><c>{csrf_token}</c> — the raw token value.</summary>
    Token,
    /// <summary><c>{csrf_meta}</c> — a meta tag.</summary>
    Meta
}

public sealed class CsrfNode : TemplateNode
{
    public CsrfNode(CsrfKind kind) => Kind = kind;
    public CsrfKind Kind { get; }
}

/// <summary><c>{renderbody}</c> — the child template's body inside a layout.</summary>
public sealed class RenderBodyNode : TemplateNode
{
}

/// <summary><c>{renderhead}</c> — the page head block.</summary>
public sealed class RenderHeadNode : TemplateNode
{
}

/// <summary><c>{rendersection:name}</c> — emits a named section captured from the child.</summary>
public sealed class RenderSectionNode : TemplateNode
{
    public RenderSectionNode(string name) => Name = name;
    public string Name { get; }
}

/// <summary>One <c>key="value"</c> attribute on a <c>{widget:…}</c>; the value may itself contain tokens.</summary>
public sealed class WidgetAttribute
{
    public WidgetAttribute(string name, IReadOnlyList<TemplateNode> value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    /// <summary>The attribute value parsed as an inline fragment (literals + tokens).</summary>
    public IReadOnlyList<TemplateNode> Value { get; }
}

/// <summary><c>{widget:name attr="v"}</c> — a dynamic, runtime-resolved widget.</summary>
public sealed class WidgetNode : TemplateNode
{
    public WidgetNode(string name, IReadOnlyList<WidgetAttribute> attributes)
    {
        Name = name;
        Attributes = attributes;
    }

    public string Name { get; }
    public IReadOnlyList<WidgetAttribute> Attributes { get; }
}

/// <summary><c>{widgetarea:name}</c> — renders all widgets registered for an area.</summary>
public sealed class WidgetAreaNode : TemplateNode
{
    public WidgetAreaNode(string area) => Area = area;
    public string Area { get; }
}

/// <summary>An argument passed into a parameterised partial: either a literal or a selector.</summary>
public sealed class PartialArgument
{
    private PartialArgument(string name, TemplateSelector? selector, string? literal)
    {
        Name = name;
        Selector = selector;
        Literal = literal;
    }

    public static PartialArgument FromSelector(string name, TemplateSelector selector) => new(name, selector, null);
    public static PartialArgument FromLiteral(string name, string literal) => new(name, null, literal);

    public string Name { get; }
    public TemplateSelector? Selector { get; }
    public string? Literal { get; }
    public bool IsLiteral => Selector is null;
}

/// <summary><c>{partial:path key="v" rows=model:x}</c> — includes another template, optionally with params.</summary>
public sealed class PartialNode : TemplateNode
{
    public PartialNode(string path, IReadOnlyList<PartialArgument> arguments)
    {
        Path = path;
        Arguments = arguments;
    }

    public string Path { get; }
    public IReadOnlyList<PartialArgument> Arguments { get; }
}

/// <summary>
/// <c>{foreach:source as alias}…{/foreach}</c>. Inside the body, the current item is
/// available via <c>{item:…}</c> and, when <see cref="Alias"/> is set, via
/// <c>{alias:…}</c>; loop metadata is available via <c>{loop:…}</c>.
/// </summary>
public sealed class ForeachNode : TemplateNode
{
    public ForeachNode(TemplateSelector source, string? alias, IReadOnlyList<TemplateNode> body)
    {
        Source = source;
        Alias = alias;
        Body = body;
    }

    public TemplateSelector Source { get; }
    public string? Alias { get; }
    public IReadOnlyList<TemplateNode> Body { get; }
}

/// <summary>One branch of an if/elseif chain: a condition and the body to render when it holds.</summary>
public sealed class ConditionalBranch
{
    public ConditionalBranch(TemplateCondition condition, IReadOnlyList<TemplateNode> body)
    {
        Condition = condition;
        Body = body;
    }

    public TemplateCondition Condition { get; }
    public IReadOnlyList<TemplateNode> Body { get; }
}

/// <summary>
/// <c>{if:…}…{else}…{/if}</c> (and <c>{ifnot:…}</c>). The first satisfied branch
/// renders; otherwise <see cref="Else"/> renders if present.
/// </summary>
public sealed class IfNode : TemplateNode
{
    public IfNode(IReadOnlyList<ConditionalBranch> branches, IReadOnlyList<TemplateNode>? @else)
    {
        Branches = branches;
        Else = @else;
    }

    public IReadOnlyList<ConditionalBranch> Branches { get; }
    public IReadOnlyList<TemplateNode>? Else { get; }
}

/// <summary>One <c>{case:value}…{/case}</c> in a switch.</summary>
public sealed class SwitchCase
{
    public SwitchCase(string value, IReadOnlyList<TemplateNode> body)
    {
        Value = value;
        Body = body;
    }

    public string Value { get; }
    public IReadOnlyList<TemplateNode> Body { get; }
}

/// <summary><c>{switch:sel}{case:v}…{/case}{default}…{/default}{/switch}</c>.</summary>
public sealed class SwitchNode : TemplateNode
{
    public SwitchNode(TemplateSelector subject, IReadOnlyList<SwitchCase> cases, IReadOnlyList<TemplateNode>? @default)
    {
        Subject = subject;
        Cases = cases;
        Default = @default;
    }

    public TemplateSelector Subject { get; }
    public IReadOnlyList<SwitchCase> Cases { get; }
    public IReadOnlyList<TemplateNode>? Default { get; }
}

/// <summary><c>{verbatim}…{/verbatim}</c> — emitted exactly, with no token parsing (for JS/JSON).</summary>
public sealed class VerbatimNode : TemplateNode
{
    public VerbatimNode(string text) => Text = text;
    public string Text { get; }
}
