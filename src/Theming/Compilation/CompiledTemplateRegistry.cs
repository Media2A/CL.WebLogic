using System.Collections.Concurrent;

namespace CL.WebLogic.Theming;

/// <summary>
/// A template compiled to C# at build time by the CL.WebLogic.TemplateCompiler
/// source generator. Rendering appends to <c>context.Output</c> — no file read,
/// no parsing.
/// </summary>
public interface ICompiledTemplate
{
    /// <summary>Theme-relative normalized path, e.g. <c>templates/home.html</c>. Registry key.</summary>
    string NormalizedPath { get; }

    /// <summary>
    /// Hash of the exact template source this class was compiled from
    /// (<see cref="CompiledTemplateRegistry.ComputeSourceHash"/>). The engine
    /// only uses the compiled form when the live file matches — a theme updated
    /// at runtime (git pull in the container entrypoint) silently falls back to
    /// the interpreter for the changed files.
    /// </summary>
    string SourceHash { get; }

    /// <summary>Raw <c>{layout:…}</c> path as written, or null. Normalized by the engine.</summary>
    string? LayoutPath { get; }

    /// <summary>Renders the template body (sections excluded) into <c>context.Output</c>.</summary>
    Task RenderBodyAsync(WebTemplateContext context);

    /// <summary>Renders each <c>{section:…}</c> into <paramref name="sections"/> for the layout.</summary>
    Task RenderSectionsAsync(WebTemplateContext context, IDictionary<string, string> sections);
}

/// <summary>
/// Process-wide registry of compiled templates, keyed by normalized path.
/// Populated by <c>[ModuleInitializer]</c> methods the source generator emits —
/// loading an assembly that contains compiled templates registers them.
/// </summary>
public static class CompiledTemplateRegistry
{
    private static readonly ConcurrentDictionary<string, ICompiledTemplate> _templates =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ICompiledTemplate template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.NormalizedPath))
            return;
        _templates[template.NormalizedPath] = template;
    }

    public static bool TryGet(string normalizedPath, out ICompiledTemplate template)
    {
        if (_templates.TryGetValue(normalizedPath, out var found))
        {
            template = found;
            return true;
        }
        template = null!;
        return false;
    }

    public static int Count => _templates.Count;

    /// <summary>Registered paths — for diagnostics/boot logging.</summary>
    public static IReadOnlyCollection<string> Paths => _templates.Keys.ToArray();

    /// <summary>
    /// Canonical source hash used by both the generator (build time) and the
    /// engine (render time) — see <see cref="Templates.Parsing.TemplateSourceHash"/>.
    /// </summary>
    public static string ComputeSourceHash(string source) =>
        Templates.Parsing.TemplateSourceHash.Compute(source);
}
