namespace CL.WebLogic.Templates.Ast;

/// <summary>Comparison operator for <c>{if:a gt b}</c>-style conditions.</summary>
public enum CompareOp
{
    Eq,
    Ne,
    Gt,
    Gte,
    Lt,
    Lte
}

/// <summary>
/// Base type for a parsed <c>{if:…}</c> / <c>{ifnot:…}</c> condition. The grammar
/// supports the same forms the legacy engine did — <c>auth</c>,
/// <c>accessgroup:a,b</c>, <c>permission:x</c>, and plain selector truthiness — plus
/// the new comparison form.
/// </summary>
public abstract class TemplateCondition
{
}

/// <summary>Negates the wrapped condition. Produced by <c>{ifnot:…}</c>.</summary>
public sealed class NotCondition : TemplateCondition
{
    public NotCondition(TemplateCondition inner) => Inner = inner;
    public TemplateCondition Inner { get; }
}

/// <summary><c>{if:auth}</c> — true when the request is authenticated.</summary>
public sealed class AuthCondition : TemplateCondition
{
}

/// <summary><c>{if:accessgroup:admin,mod}</c> — true when the user is in any listed group.</summary>
public sealed class AccessGroupCondition : TemplateCondition
{
    public AccessGroupCondition(IReadOnlyList<string> groups) => Groups = groups;
    public IReadOnlyList<string> Groups { get; }
}

/// <summary><c>{if:permission:news.edit}</c> — true when the user holds the permission.</summary>
public sealed class PermissionCondition : TemplateCondition
{
    public PermissionCondition(string permission) => Permission = permission;
    public string Permission { get; }
}

/// <summary><c>{if:model:items}</c> — true when the selector resolves to a truthy value.</summary>
public sealed class TruthyCondition : TemplateCondition
{
    public TruthyCondition(TemplateSelector selector) => Selector = selector;
    public TemplateSelector Selector { get; }
}

/// <summary>
/// An operand of a comparison: either a selector (resolved at render time) or a
/// literal (a quoted string or an unquoted number/bareword).
/// </summary>
public sealed class TemplateOperand
{
    private TemplateOperand(TemplateSelector? selector, string? literal)
    {
        Selector = selector;
        Literal = literal;
    }

    public static TemplateOperand FromSelector(TemplateSelector selector) => new(selector, null);
    public static TemplateOperand FromLiteral(string literal) => new(null, literal);

    public TemplateSelector? Selector { get; }
    public string? Literal { get; }
    public bool IsLiteral => Selector is null;
}

/// <summary><c>{if:model:kills gt 100}</c> — compares a selector to an operand.</summary>
public sealed class CompareCondition : TemplateCondition
{
    public CompareCondition(TemplateSelector left, CompareOp op, TemplateOperand right)
    {
        Left = left;
        Op = op;
        Right = right;
    }

    public TemplateSelector Left { get; }
    public CompareOp Op { get; }
    public TemplateOperand Right { get; }
}
