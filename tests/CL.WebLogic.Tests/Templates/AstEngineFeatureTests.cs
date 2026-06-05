using System.Text;
using CL.WebLogic.Configuration;
using CL.WebLogic.Theming;
using Xunit;

namespace CL.WebLogic.Tests.Templates;

/// <summary>
/// Render-level tests for the constructs the AST engine ADDS over the legacy
/// pipeline: {verbatim}, {else}/{elseif}, comparisons, {switch}, loop metadata,
/// foreach aliasing, parameterised partials, and the extended filter set.
/// Runs through the public <see cref="ThemeManager.RenderTemplateAsync"/> with
/// default config — which also proves the UseAstEngine routing.
/// </summary>
public sealed class AstEngineFeatureTests : IDisposable
{
    private readonly string _root;
    private readonly ThemeManager _manager;

    public AstEngineFeatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "weblogic-ast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        Directory.CreateDirectory(Path.Combine(_root, "partials"));

        var config = new WebLogicConfig();
        config.Theme.EnableCaching = false;
        // UseAstEngine left at its default (true) on purpose.
        _manager = new ThemeManager(null!, config, new WebWidgetRegistry(), null);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private Task<string> RenderAsync(string template, Dictionary<string, object?>? model = null)
    {
        Write("templates/t.html", template);
        return _manager.RenderTemplateAsync("templates/t.html", model, _root);
    }

    private static Dictionary<string, object?> Model(params (string Key, object? Value)[] entries)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    [Fact]
    public async Task Verbatim_EmitsJsObjectLiteralsUntouched()
    {
        var result = await RenderAsync(
            "{verbatim}<script>var cfg = { type:'line', data:{ labels: x } };</script>{/verbatim}");
        Assert.Equal("<script>var cfg = { type:'line', data:{ labels: x } };</script>", result);
    }

    [Fact]
    public async Task IfElse_And_Elseif_Render()
    {
        var result = await RenderAsync(
            "{if:model:a}A{elseif:model:b}B{else}C{/if}|{ifnot:model:a}NA{else}HASA{/ifnot}|{ifnot:model:b}NB{else}HASB{/ifnot}",
            Model(("a", false), ("b", true)));
        Assert.Equal("B|NA|HASB", result);
    }

    [Fact]
    public async Task Comparisons_NumericAndString()
    {
        var result = await RenderAsync(
            "{if:model:kills gt 100}elite{/if}{if:model:kills lte 100}casual{/if}" +
            "{if:model:kdr gte 1.5}+{/if}{if:model:name eq \"claus\"}hi{/if}{if:model:kills ne 0}seen{/if}",
            Model(("kills", 250), ("kdr", 1.5), ("name", "Claus")));
        Assert.Equal("elite+hiseen", result);
    }

    [Fact]
    public async Task Comparison_SelectorVsSelector()
    {
        var result = await RenderAsync(
            "{if:model:score gt model:highscore}NEW BEST{else}keep trying{/if}",
            Model(("score", 900), ("highscore", 750)));
        Assert.Equal("NEW BEST", result);
    }

    [Fact]
    public async Task Switch_Case_Default()
    {
        const string template =
            "{switch:model:weapon}{case:awp}sniper{/case}{case:knife}melee{/case}{default}rifle{/default}{/switch}";

        Assert.Equal("sniper", await RenderAsync(template, Model(("weapon", "AWP"))));
        Assert.Equal("melee", await RenderAsync(template, Model(("weapon", "knife"))));
        Assert.Equal("rifle", await RenderAsync(template, Model(("weapon", "ak47"))));
    }

    [Fact]
    public async Task LoopMetadata_IndexNumberFirstLastCount()
    {
        var result = await RenderAsync(
            "{foreach:model:items}[{loop:number}/{loop:count}{if:loop:first}F{/if}{if:loop:last}L{/if}:{item:n}]{/foreach}",
            Model(("items", new List<object?>
            {
                new Dictionary<string, object?> { ["n"] = "a" },
                new Dictionary<string, object?> { ["n"] = "b" },
                new Dictionary<string, object?> { ["n"] = "c" }
            })));
        Assert.Equal("[1/3F:a][2/3:b][3/3L:c]", result);
    }

    [Fact]
    public async Task LoopMetadata_OddEvenStriping()
    {
        var result = await RenderAsync(
            "{foreach:model:items}<tr class=\"{if:loop:odd}stripe{/if}\">{item:n}</tr>{/foreach}",
            Model(("items", new List<object?>
            {
                new Dictionary<string, object?> { ["n"] = 1 },
                new Dictionary<string, object?> { ["n"] = 2 }
            })));
        Assert.Equal("<tr class=\"\">1</tr><tr class=\"stripe\">2</tr>", result);
    }

    [Fact]
    public async Task ForeachAlias_OuterItemVisibleInInnerLoop()
    {
        var result = await RenderAsync(
            "{foreach:model:cards as card}{foreach:card:rows as row}{row:name}@{card:title};{/foreach}{/foreach}",
            Model(("cards", new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["title"] = "Top",
                    ["rows"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["name"] = "a" },
                        new Dictionary<string, object?> { ["name"] = "b" }
                    }
                }
            })));
        Assert.Equal("a@Top;b@Top;", result);
    }

    [Fact]
    public async Task ParameterisedPartial_LiteralAndSelectorArgs()
    {
        Write("partials/card.html",
            "<div class=\"card\"><h5>{param:title}</h5>{foreach:param:rows}<p>{item:name}</p>{/foreach}</div>");

        var result = await RenderAsync(
            "{partial:partials/card title=\"Top Score\" rows=model:scores}",
            Model(("scores", new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "claus" }
            })));
        Assert.Equal("<div class=\"card\"><h5>Top Score</h5><p>claus</p></div>", result);
    }

    [Fact]
    public async Task ParameterisedPartial_CalledTwiceWithDifferentParams()
    {
        Write("partials/badge.html", "[{param:tone}:{param:label}]");
        var result = await RenderAsync(
            "{partial:partials/badge tone=\"ok\" label=\"Online\"}{partial:partials/badge tone=\"err\" label=\"Down\"}");
        Assert.Equal("[ok:Online][err:Down]", result);
    }

    [Fact]
    public async Task NewFilters_NumberPercentRoundAbbreviateFilesize()
    {
        var result = await RenderAsync(
            "{model:big|number} {model:dec|number:2} {model:hs|percent:1} {model:kdr|round:2} " +
            "{model:big|abbreviate} {model:huge|abbreviate} {model:bytes|filesize}",
            Model(("big", 1234567), ("dec", 1234.5), ("hs", 45.26), ("kdr", 1.8349),
                  ("huge", 5_600_000), ("bytes", 1572864)));
        Assert.Equal("1,234,567 1,234.50 45.3% 1.83 1.2M 5.6M 1.5 MB", result);
    }

    [Fact]
    public async Task TimeagoFilter_RecentAndOld()
    {
        var result = await RenderAsync(
            "{model:recent|timeago}|{model:hours|timeago}|{model:old|timeago}",
            Model(("recent", DateTime.UtcNow.AddSeconds(-10)),
                  ("hours", DateTime.UtcNow.AddHours(-3)),
                  ("old", new DateTime(2020, 5, 17, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Equal("just now|3h ago|2020-05-17", result);
    }

    [Fact]
    public async Task NestedForeach_ThroughPublicApi_ProvesAstRouting()
    {
        // Nested foreach is broken in the legacy pipeline; it working through the
        // public RenderTemplateAsync proves UseAstEngine routing is active.
        var result = await RenderAsync(
            "{foreach:model:rows}{item:name}({foreach:item:kids}{item:n}{/foreach}){/foreach}",
            Model(("rows", new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "x",
                    ["kids"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["n"] = 1 },
                        new Dictionary<string, object?> { ["n"] = 2 }
                    }
                }
            })));
        Assert.Equal("x(12)", result);
    }
}
