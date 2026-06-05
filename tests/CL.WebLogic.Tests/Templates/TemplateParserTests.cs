using System.Linq;
using CL.WebLogic.Templates.Ast;
using CL.WebLogic.Templates.Parsing;
using Xunit;

namespace CL.WebLogic.Tests.Templates;

public class TemplateParserTests
{
    private static TemplateDocument Parse(string s) => TemplateParser.Parse(s);

    [Fact]
    public void PlainText_IsSingleLiteral()
    {
        var doc = Parse("<p>hello world</p>");
        var literal = Assert.IsType<LiteralNode>(Assert.Single(doc.Nodes));
        Assert.Equal("<p>hello world</p>", literal.Text);
        Assert.Null(doc.LayoutPath);
    }

    [Fact]
    public void Token_WithScopeAndFilters_Parses()
    {
        var doc = Parse("{model:user.name|uppercase|truncate:10}");
        var token = Assert.IsType<TokenNode>(Assert.Single(doc.Nodes));
        Assert.False(token.Raw);
        Assert.Equal(TemplateScope.Model, token.Selector.Scope);
        Assert.Equal("user.name", token.Selector.Key);
        Assert.Collection(token.Filters,
            f => { Assert.Equal("uppercase", f.Name); Assert.Null(f.Arg); },
            f => { Assert.Equal("truncate", f.Name); Assert.Equal("10", f.Arg); });
    }

    [Fact]
    public void RawToken_SetsRawFlag()
    {
        var doc = Parse("{raw:model:body_html}");
        var token = Assert.IsType<TokenNode>(Assert.Single(doc.Nodes));
        Assert.True(token.Raw);
        Assert.Equal(TemplateScope.Model, token.Selector.Scope);
        Assert.Equal("body_html", token.Selector.Key);
    }

    [Fact]
    public void LegacyDoubleBrace_Parses()
    {
        var doc = Parse("{{user.name}}");
        var legacy = Assert.IsType<LegacyTokenNode>(Assert.Single(doc.Nodes));
        Assert.Equal("user.name", legacy.Dotted);
    }

    [Theory]
    [InlineData("{csrf}", CsrfKind.Field)]
    [InlineData("{csrf_token}", CsrfKind.Token)]
    [InlineData("{csrf_meta}", CsrfKind.Meta)]
    public void Csrf_Variants(string src, CsrfKind kind)
    {
        var doc = Parse(src);
        var csrf = Assert.IsType<CsrfNode>(Assert.Single(doc.Nodes));
        Assert.Equal(kind, csrf.Kind);
    }

    [Fact]
    public void Foreach_WithAlias_CapturesSourceAndBody()
    {
        var doc = Parse("{foreach:model:rows as row}<li>{item:name}</li>{/foreach}");
        var loop = Assert.IsType<ForeachNode>(Assert.Single(doc.Nodes));
        Assert.Equal(TemplateScope.Model, loop.Source.Scope);
        Assert.Equal("rows", loop.Source.Key);
        Assert.Equal("row", loop.Alias);
        Assert.Equal(3, loop.Body.Count); // "<li>", token, "</li>"
        Assert.IsType<TokenNode>(loop.Body[1]);
    }

    [Fact]
    public void Foreach_NoAlias_HasNullAlias()
    {
        var doc = Parse("{foreach:items}x{/foreach}");
        var loop = Assert.IsType<ForeachNode>(Assert.Single(doc.Nodes));
        Assert.Null(loop.Alias);
        Assert.Equal(TemplateScope.None, loop.Source.Scope);
        Assert.Equal("items", loop.Source.Key);
    }

    [Fact]
    public void NestedForeach_InnerStaysInside()
    {
        var doc = Parse("{foreach:a as x}{foreach:x:kids as k}{k:n}{/foreach}{/foreach}");
        var outer = Assert.IsType<ForeachNode>(Assert.Single(doc.Nodes));
        var inner = Assert.IsType<ForeachNode>(Assert.Single(outer.Body));
        Assert.Equal("k", inner.Alias);
        Assert.IsType<TokenNode>(Assert.Single(inner.Body));
    }

    [Fact]
    public void If_Else_Branches()
    {
        var doc = Parse("{if:model:n}yes{else}no{/if}");
        var node = Assert.IsType<IfNode>(Assert.Single(doc.Nodes));
        var branch = Assert.Single(node.Branches);
        Assert.IsType<TruthyCondition>(branch.Condition);
        Assert.NotNull(node.Else);
    }

    [Fact]
    public void IfElseif_Chain()
    {
        var doc = Parse("{if:model:a}A{elseif:model:b}B{else}C{/if}");
        var node = Assert.IsType<IfNode>(Assert.Single(doc.Nodes));
        Assert.Equal(2, node.Branches.Count);
        Assert.NotNull(node.Else);
    }

    [Fact]
    public void IfNot_WrapsInNotCondition()
    {
        var doc = Parse("{ifnot:auth}login{/if}");
        var node = Assert.IsType<IfNode>(Assert.Single(doc.Nodes));
        var branch = Assert.Single(node.Branches);
        var not = Assert.IsType<NotCondition>(branch.Condition);
        Assert.IsType<AuthCondition>(not.Inner);
    }

    [Fact]
    public void If_Comparison_Parses()
    {
        var doc = Parse("{if:model:kills gt 100}elite{/if}");
        var node = Assert.IsType<IfNode>(Assert.Single(doc.Nodes));
        var cmp = Assert.IsType<CompareCondition>(node.Branches[0].Condition);
        Assert.Equal(CompareOp.Gt, cmp.Op);
        Assert.Equal("kills", cmp.Left.Key);
        Assert.True(cmp.Right.IsLiteral);
        Assert.Equal("100", cmp.Right.Literal);
    }

    [Fact]
    public void If_PermissionAndAccessGroup()
    {
        var perm = Assert.IsType<IfNode>(Assert.Single(Parse("{if:permission:news.edit}x{/if}").Nodes));
        Assert.Equal("news.edit", Assert.IsType<PermissionCondition>(perm.Branches[0].Condition).Permission);

        var grp = Assert.IsType<IfNode>(Assert.Single(Parse("{if:accessgroup:admin,mod}x{/if}").Nodes));
        var ag = Assert.IsType<AccessGroupCondition>(grp.Branches[0].Condition);
        Assert.Equal(new[] { "admin", "mod" }, ag.Groups.ToArray());
    }

    [Fact]
    public void Switch_CasesAndDefault()
    {
        var doc = Parse("{switch:item:kind}{case:awp}A{/case}{case:knife}K{/case}{default}D{/default}{/switch}");
        var sw = Assert.IsType<SwitchNode>(Assert.Single(doc.Nodes));
        Assert.Equal("kind", sw.Subject.Key);
        Assert.Equal(2, sw.Cases.Count);
        Assert.Equal("awp", sw.Cases[0].Value);
        Assert.Equal("knife", sw.Cases[1].Value);
        Assert.NotNull(sw.Default);
    }

    [Fact]
    public void Verbatim_PreservesBracesUnparsed()
    {
        var doc = Parse("{verbatim}<script>var c={a:{b:1}};</script>{/verbatim}");
        var v = Assert.IsType<VerbatimNode>(Assert.Single(doc.Nodes));
        Assert.Equal("<script>var c={a:{b:1}};</script>", v.Text);
    }

    [Fact]
    public void Partial_WithLiteralAndSelectorArgs()
    {
        var doc = Parse("{partial:partials/card title=\"Top Score\" rows=model:scores}");
        var p = Assert.IsType<PartialNode>(Assert.Single(doc.Nodes));
        Assert.Equal("partials/card", p.Path);
        Assert.Equal(2, p.Arguments.Count);

        var title = p.Arguments[0];
        Assert.Equal("title", title.Name);
        Assert.True(title.IsLiteral);
        Assert.Equal("Top Score", title.Literal);

        var rows = p.Arguments[1];
        Assert.Equal("rows", rows.Name);
        Assert.False(rows.IsLiteral);
        Assert.Equal(TemplateScope.Model, rows.Selector!.Scope);
        Assert.Equal("scores", rows.Selector.Key);
    }

    [Fact]
    public void Partial_NoArgs()
    {
        var doc = Parse("{partial:partials/site-nav}");
        var p = Assert.IsType<PartialNode>(Assert.Single(doc.Nodes));
        Assert.Equal("partials/site-nav", p.Path);
        Assert.Empty(p.Arguments);
    }

    [Fact]
    public void Widget_WithStaticAttribute()
    {
        // Token-bearing attribute values can't contain '}' (the legacy widget regex
        // is non-greedy to the first '}'), so the supported case is a static value.
        var doc = Parse("{widget:news-slider count=\"5\"}");
        var w = Assert.IsType<WidgetNode>(Assert.Single(doc.Nodes));
        Assert.Equal("news-slider", w.Name);
        var attr = Assert.Single(w.Attributes);
        Assert.Equal("count", attr.Name);
        var lit = Assert.IsType<LiteralNode>(Assert.Single(attr.Value));
        Assert.Equal("5", lit.Text);
    }

    [Fact]
    public void Layout_And_Sections()
    {
        var doc = Parse("{layout:layouts/public-main}\n{section:head}<title>x</title>{/section}body");
        Assert.Equal("layouts/public-main", doc.LayoutPath);
        var section = Assert.Single(doc.Sections);
        Assert.Equal("head", section.Name);
        // Section content is removed from the inline body.
        Assert.DoesNotContain(doc.Nodes.OfType<LiteralNode>(), n => n.Text.Contains("<title>"));
        Assert.Contains(doc.Nodes.OfType<LiteralNode>(), n => n.Text.Contains("body"));
    }

    [Fact]
    public void UnknownScope_BecomesUnknownToken_ForParity()
    {
        // Inline CSS-like {color:red} matched the legacy token regex and rendered
        // empty; we reproduce that by parsing it as an Unknown-scope token.
        var doc = Parse("<style>.x{color:red}</style>");
        var token = doc.Nodes.OfType<TokenNode>().Single();
        Assert.Equal(TemplateScope.Unknown, token.Selector.Scope);
        Assert.Equal("red", token.Selector.Key);
    }

    [Fact]
    public void BareBraces_WithoutColon_StayLiteral()
    {
        var doc = Parse("a {b} c");
        Assert.Single(doc.Nodes);
        Assert.Equal("a {b} c", Assert.IsType<LiteralNode>(doc.Nodes[0]).Text);
    }

    [Fact]
    public void StrayCloseTag_AtTopLevel_IsLiteral()
    {
        var doc = Parse("before{/if}after");
        var text = string.Concat(doc.Nodes.OfType<LiteralNode>().Select(n => n.Text));
        Assert.Equal("before{/if}after", text);
    }

    [Fact]
    public void ElseInsideForeach_IsLiteral_NotAStop()
    {
        // {else} only terminates an if-body; inside a foreach it is plain text.
        var doc = Parse("{foreach:x}a{else}b{/foreach}");
        var loop = Assert.IsType<ForeachNode>(Assert.Single(doc.Nodes));
        var text = string.Concat(loop.Body.OfType<LiteralNode>().Select(n => n.Text));
        Assert.Equal("a{else}b", text);
    }
}
