using System.Text;
using CL.WebLogic.Configuration;
using CL.WebLogic.Templates.Parsing;
using CL.WebLogic.Theming;
using Xunit;

namespace CL.WebLogic.Tests.Templates;

/// <summary>
/// End-to-end tests of the source generator: TestTheme/**/*.html is compiled
/// into THIS assembly at build time (see the csproj's analyzer reference), so
/// these tests exercise the real MSBuild → generator → registry → dispatch
/// path, not a simulated one.
/// </summary>
public sealed class CompiledTemplateTests : IDisposable
{
    private static readonly string[] CompiledKeys =
    {
        "templates/compiled-page.html",
        "templates/compiled-flat.html",
        "layouts/test-main.html",
        "partials/test-card.html"
    };

    private readonly string _emptyRoot;
    private readonly string _scratchRoot;
    private readonly ThemeManager _compiled;
    private readonly ThemeManager _interpreter;

    /// <summary>The TestTheme copied next to the test assembly at build time.</summary>
    private static string OnDiskTheme => Path.Combine(AppContext.BaseDirectory, "TestTheme");

    public CompiledTemplateTests()
    {
        _emptyRoot = Path.Combine(Path.GetTempPath(), "weblogic-empty-" + Guid.NewGuid().ToString("N"));
        _scratchRoot = Path.Combine(Path.GetTempPath(), "weblogic-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_emptyRoot);
        Directory.CreateDirectory(Path.Combine(_scratchRoot, "templates"));

        var compiledConfig = new WebLogicConfig();
        compiledConfig.Theme.EnableCaching = false;
        _compiled = new ThemeManager(null!, compiledConfig, new WebWidgetRegistry(), null);

        var interpreterConfig = new WebLogicConfig();
        interpreterConfig.Theme.EnableCaching = false;
        interpreterConfig.Theme.UseCompiledTemplates = false;
        _interpreter = new ThemeManager(null!, interpreterConfig, new WebWidgetRegistry(), null);
    }

    public void Dispose()
    {
        try { Directory.Delete(_emptyRoot, recursive: true); } catch { }
        try { Directory.Delete(_scratchRoot, recursive: true); } catch { }
    }

    private static Dictionary<string, object?> PageModel() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "Test & Go",
        ["count"] = 3,
        ["kind"] = "a",
        ["n"] = 1234.5,
        ["x"] = "v",
        ["html"] = "<b>B</b>",
        ["rows"] = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "a",
                ["tags"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["t"] = "x" },
                    new Dictionary<string, object?> { ["t"] = "y" }
                }
            },
            new Dictionary<string, object?> { ["name"] = "b", ["tags"] = new List<object?>() }
        }
    };

    [Fact]
    public void Registry_ContainsTestTemplates_WithMatchingSourceHashes()
    {
        foreach (var key in CompiledKeys)
        {
            Assert.True(CompiledTemplateRegistry.TryGet(key, out var compiled), $"not registered: {key}");

            var file = Path.Combine(OnDiskTheme, key.Replace('/', Path.DirectorySeparatorChar));
            var expected = TemplateSourceHash.Compute(File.ReadAllText(file, Encoding.UTF8));
            Assert.Equal(expected, compiled.SourceHash);
        }

        Assert.True(CompiledTemplateRegistry.TryGet("templates/compiled-page.html", out var page));
        Assert.Equal("layouts/test-main", page.LayoutPath);
    }

    [Fact]
    public async Task DisklessRender_PageWithLayoutAndPartial_UsesCompiledOnly()
    {
        // themeRoot is an EMPTY directory: any file read returns null, so the
        // interpreter path would yield "Template not found". A full render here
        // proves the page, its layout AND its partial all came from compiled
        // code — zero template reads.
        var html = await _compiled.RenderTemplateAsync("templates/compiled-page.html", PageModel(), _emptyRoot);

        Assert.Contains("<html><head><meta name=\"x\" content=\"Test &amp; Go\">", html);
        Assert.Contains("<h1>TEST &amp; GO</h1>", html);
        Assert.Contains("MANY", html);
        Assert.DoesNotContain("FEW", html);
        Assert.Contains("<li class=\"\">1:a[x@a][y@a]</li>", html);
        Assert.Contains("<li class=\"odd\">2:b</li>", html);
        Assert.Contains("KA", html);
        Assert.Contains("<div class=\"card\"><h5>Hello</h5><p>a</p><p>b</p></div>", html);
        Assert.Contains("<script>var x={a:1};</script>", html);
        Assert.Contains("1,234.50", html);
        Assert.Contains("none", html);
        Assert.Contains("ANON", html);
    }

    [Fact]
    public async Task Compiled_RendersByteIdentical_ToInterpreter()
    {
        var model = PageModel();
        foreach (var key in new[] { "templates/compiled-page.html", "templates/compiled-flat.html" })
        {
            var compiled = await _compiled.RenderTemplateAsync(key, model, OnDiskTheme);
            var interpreted = await _interpreter.RenderTemplateAsync(key, model, OnDiskTheme);
            Assert.Equal(interpreted, compiled);
        }
    }

    [Fact]
    public async Task StaleSource_FallsBackToInterpreter()
    {
        // A theme refreshed at runtime (entrypoint git pull) replaces files on
        // disk; the hash gate must serve the LIVE content, not the compiled one.
        File.WriteAllText(
            Path.Combine(_scratchRoot, "templates", "compiled-flat.html"),
            "UPDATED {model:x}!", new UTF8Encoding(false));

        var html = await _compiled.RenderTemplateAsync("templates/compiled-flat.html",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["x"] = "live" }, _scratchRoot);

        Assert.Equal("UPDATED live!", html);
    }

    [Fact]
    public async Task UseCompiledTemplatesOff_AlwaysInterprets()
    {
        // With the flag off and no file on disk, even a registered compiled
        // template must NOT render.
        var html = await _interpreter.RenderTemplateAsync("templates/compiled-flat.html", PageModel(), _emptyRoot);
        Assert.Contains("Template not found", html);
    }
}
