using System.Text;
using CL.WebLogic.Configuration;
using CL.WebLogic.Theming;
using Xunit;

namespace CL.WebLogic.Tests.Templates;

/// <summary>
/// The Phase-1 safety gate: every template must render byte-identically through
/// the legacy regex pipeline (<see cref="ThemeManager.RenderTemplateAsync"/>) and
/// the new AST engine (<see cref="ThemeManager.RenderTemplateViaAstAsync"/>).
/// Covers synthetic templates exercising each shared construct, plus — when the
/// sibling WebsiteThemes clone is present — every real fraghunt2026 template.
/// </summary>
public sealed class GoldenParityTests : IDisposable
{
    private readonly string _root;
    private readonly ThemeManager _manager;

    public GoldenParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "weblogic-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        Directory.CreateDirectory(Path.Combine(_root, "layouts"));
        Directory.CreateDirectory(Path.Combine(_root, "partials"));

        var config = new WebLogicConfig();
        config.Theme.EnableCaching = false; // fresh reads; no watcher
        config.Theme.UseAstEngine = false;  // pin RenderTemplateAsync to the LEGACY pipeline for comparison
        config.Theme.UseCompiledTemplates = false; // and keep compiled test templates out of the AST side
        _manager = new ThemeManager(null!, config, new WebWidgetRegistry(), null);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteTemplate(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private async Task AssertParityAsync(string templatePath, IReadOnlyDictionary<string, object?>? model, string? themeRoot = null)
    {
        var root = themeRoot ?? _root;
        var legacy = await _manager.RenderTemplateAsync(templatePath, model, root);
        var ast = await _manager.RenderTemplateViaAstAsync(templatePath, model, root);
        Assert.Equal(legacy, ast);
    }

    private static Dictionary<string, object?> Model(params (string Key, object? Value)[] entries)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    // ── synthetic construct coverage ───────────────────────────────────────

    [Fact]
    public async Task Tokens_Filters_Raw_Legacy_MissingKeys()
    {
        WriteTemplate("templates/t.html",
            "Hello {model:name|uppercase}! raw={raw:model:html} enc={model:html} " +
            "miss=[{model:missing}] legacy={{name}} legacymiss={{missing}} dotted={{user.name}} " +
            "trunc={model:longtext|truncate:5} def={model:missing|default:\"n/a\"}");

        await AssertParityAsync("templates/t.html", Model(
            ("name", "World"),
            ("html", "<b>x</b>"),
            ("longtext", "abcdefghij"),
            ("user", new Dictionary<string, object?> { ["name"] = "Claus" })));
    }

    [Fact]
    public async Task Foreach_Flat_WithDictItems()
    {
        WriteTemplate("templates/t.html",
            "<ul>{foreach:model:rows}<li>{item:name} ({item:meta.role})</li>{/foreach}</ul>" +
            "{foreach:model:empty}never{/foreach}{foreach:model:missing}never{/foreach}");

        await AssertParityAsync("templates/t.html", Model(
            ("rows", new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "a", ["meta"] = new Dictionary<string, object?> { ["role"] = "admin" } },
                new Dictionary<string, object?> { ["name"] = "b", ["meta"] = new Dictionary<string, object?> { ["role"] = "mod" } }
            }),
            ("empty", new List<object?>())));
    }

    [Fact]
    public async Task Foreach_Nested_AstOnly_WorksCorrectly()
    {
        // Legacy's non-greedy regex pairs the outer {foreach} with the INNER
        // {/foreach}, so nesting was simply broken pre-AST. This documents the
        // intentional improvement — correctness asserted against the AST engine
        // only (no parity expected or wanted).
        WriteTemplate("templates/t.html",
            "<ul>{foreach:model:rows}<li>{item:name}:{foreach:item:kids}[{item:n}]{/foreach}</li>{/foreach}</ul>");

        var ast = await _manager.RenderTemplateViaAstAsync("templates/t.html", Model(
            ("rows", new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "a", ["kids"] = new List<object?> { new Dictionary<string, object?> { ["n"] = 1 }, new Dictionary<string, object?> { ["n"] = 2 } } },
                new Dictionary<string, object?> { ["name"] = "b", ["kids"] = new List<object?>() }
            })), _root);

        Assert.Equal("<ul><li>a:[1][2]</li><li>b:</li></ul>", ast);
    }

    [Fact]
    public async Task If_IfNot_Truthiness()
    {
        WriteTemplate("templates/t.html",
            "{if:model:yes}YES{/if}{if:model:no}NO{/if}{if:model:emptylist}EL{/if}" +
            "{ifnot:model:no}NOTNO{/ifnot}{ifnot:model:yes}NOTYES{/ifnot}" +
            "{if:model:items}HAS{/if}{if:auth}AUTH{/if}{ifnot:auth}ANON{/ifnot}" +
            "{if:model:nested}{if:model:yes}BOTH{/if}{/if}" +
            "{if:auth}{ifnot:model:no}MIXED{/ifnot}{/if}");

        await AssertParityAsync("templates/t.html", Model(
            ("yes", true), ("no", false), ("emptylist", new List<object?>()),
            ("items", new List<object?> { 1 }), ("nested", "x")));
    }

    [Fact]
    public async Task Partial_Inclusion()
    {
        WriteTemplate("partials/badge.html", "<span class=\"badge\">{model:label}</span>");
        WriteTemplate("templates/t.html", "before {partial:partials/badge} after");
        await AssertParityAsync("templates/t.html", Model(("label", "Hi & <b>")));
    }

    [Fact]
    public async Task Layout_Sections_RenderBody()
    {
        WriteTemplate("layouts/main.html",
            "<html><head>{rendersection:head}</head><body>{renderbody}</body></html>");
        WriteTemplate("templates/t.html",
            "{layout:layouts/main}\n{section:head}<title>{model:title}</title>{/section}<h1>{model:title}</h1>");
        await AssertParityAsync("templates/t.html", Model(("title", "Page & Co")));
    }

    [Fact]
    public async Task InlineCssBraces_UnknownScopeQuirk()
    {
        // Legacy eats {color:red} as an unknown-scope token; AST must match.
        WriteTemplate("templates/t.html", "<style>.x{color:red} .y{margin:0}</style><div>{b}</div>");
        await AssertParityAsync("templates/t.html", Model());
    }

    [Fact]
    public async Task Csrf_WithoutSecurityService_EmitsEmptyToken()
    {
        WriteTemplate("templates/t.html", "<form>{csrf}</form> meta={csrf_meta} tok=[{csrf_token}]");
        await AssertParityAsync("templates/t.html", Model());
    }

    [Fact]
    public async Task UnknownWidget_EmitsComment()
    {
        WriteTemplate("templates/t.html", "x{widget:nope}y");
        await AssertParityAsync("templates/t.html", Model());
    }

    [Fact]
    public async Task FilterChain_AllLegacyFilters()
    {
        WriteTemplate("templates/t.html",
            "{model:s|lowercase} {model:s|capitalize} {model:s|trim} {model:s|reverse} " +
            "{model:s|length} {model:s|wordcount} {model:s|slug} {model:s|urlencode} " +
            "{model:s|prefix:\">\"} {model:s|suffix:\"<\"} {model:s|replace:\"L->W\"} " +
            "{model:n|format:\"000\"} {model:multi|nl2br}");

        await AssertParityAsync("templates/t.html", Model(
            ("s", " Hello Little WorLd "), ("n", 7), ("multi", "a\nb")));
    }

    [Fact]
    public async Task PageScopes_WithNullContext_ResolveEmpty()
    {
        WriteTemplate("templates/t.html",
            "p=[{page:path}] q=[{query:x}] c=[{cookie:x}] s=[{session:x}] h=[{header:x}] r=[{route:path}]");
        await AssertParityAsync("templates/t.html", Model());
    }

    [Fact]
    public async Task MissingTemplate_SameErrorPage()
    {
        var legacy = await _manager.RenderTemplateAsync("templates/nope.html", null, _root);
        var ast = await _manager.RenderTemplateViaAstAsync("templates/nope.html", null, _root);
        Assert.Equal(legacy, ast);
    }

    // ── real theme sweep ───────────────────────────────────────────────────

    public static readonly string RealThemeRoot =
        @"C:\Users\claus.HLAB-DC\Documents\GitHub\WebsiteThemes\fraghunt2026";

    /// <summary>
    /// Templates that use AST-era constructs the legacy regex engine can't
    /// render ({verbatim}, {switch}, {else}/{elseif}, comparisons, loop
    /// metadata, foreach aliases, parameterised partials) are EXPECTED to
    /// diverge — they're excluded from the parity sweep. The sweep keeps
    /// guarding the legacy-compatible remainder.
    /// </summary>
    private static bool UsesPostLegacyConstructs(string text) =>
        text.Contains("{verbatim}", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{switch:", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{else}", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{elseif:", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{loop:", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("{param:", StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\{foreach:[^}]+\sas\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\{ifn?o?t?:[^}]+\s(eq|ne|gt|gte|lt|lte)\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\{partial:[^}\s]+\s+[^}]*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Legacy's non-greedy regex mis-pairs nested {foreach} blocks — structural divergence by design.</summary>
    private static bool HasNestedForeach(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 9 <= text.Length && text.AsSpan(i).StartsWith("{foreach:", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                if (depth >= 2)
                    return true;
            }
            else if (i + 10 <= text.Length && text.AsSpan(i).StartsWith("{/foreach}", StringComparison.OrdinalIgnoreCase))
            {
                depth = Math.Max(0, depth - 1);
            }
        }
        return false;
    }

    [Fact]
    public async Task RealTheme_AllTemplates_RenderIdentically()
    {
        if (!Directory.Exists(RealThemeRoot))
            return; // sibling clone not present on this machine — skip silently

        var model = Model(
            ("site_title", "FragHunt"), ("site_tagline", "Frag harder"),
            ("page_title", "Parity"), ("asset_base", "/assets"),
            ("build_version", "test"), ("theme_css", "app.css"), ("theme_js", "app.js"),
            ("title", "T"), ("body", "<p>b</p>"), ("summary", "s"),
            ("breadcrumb_html", "<a href=\"/\">Home</a>"),
            ("items", new List<object?>
            {
                new Dictionary<string, object?> { ["title"] = "One", ["url"] = "/1", ["label"] = "L1" },
                new Dictionary<string, object?> { ["title"] = "Two", ["url"] = "/2", ["label"] = "L2" }
            }));

        // Pass 1: read everything; mark files that use post-legacy constructs or
        // nested {foreach} (which the legacy engine structurally mangles).
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { "templates", "layouts", "partials", "widgets" })
        {
            var full = Path.Combine(RealThemeRoot, dir);
            if (!Directory.Exists(full))
                continue;
            foreach (var file in Directory.EnumerateFiles(full, "*.html", SearchOption.AllDirectories))
                texts[Path.GetRelativePath(RealThemeRoot, file).Replace('\\', '/')] = File.ReadAllText(file);
        }

        var postLegacy = new HashSet<string>(
            texts.Where(t => UsesPostLegacyConstructs(t.Value) || HasNestedForeach(t.Value)).Select(t => t.Key),
            StringComparer.OrdinalIgnoreCase);

        // Pass 2: transitive closure — a file including a post-legacy partial
        // diverges too (and legacy expands partials BEFORE loops, so any
        // {partial:} inside a {foreach} body also diverges by design).
        bool changed;
        do
        {
            changed = false;
            foreach (var (path, text) in texts)
            {
                if (postLegacy.Contains(path))
                    continue;
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(text, @"\{partial:([^}\s]+)"))
                {
                    var reference = m.Groups[1].Value.Trim();
                    if (!reference.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        reference += ".html";
                    if (postLegacy.Contains(reference))
                    {
                        postLegacy.Add(path);
                        changed = true;
                        break;
                    }
                }
            }
        } while (changed);

        var failures = new List<string>();
        foreach (var (relative, _) in texts)
        {
            if (postLegacy.Contains(relative))
                continue; // intentionally beyond the legacy engine

            var legacy = await _manager.RenderTemplateAsync(relative, model, RealThemeRoot);
            var ast = await _manager.RenderTemplateViaAstAsync(relative, model, RealThemeRoot);
            if (!string.Equals(legacy, ast, StringComparison.Ordinal))
                failures.Add(relative);
        }

        Assert.True(failures.Count == 0,
            "AST output diverged from legacy for: " + string.Join(", ", failures));
    }
}
