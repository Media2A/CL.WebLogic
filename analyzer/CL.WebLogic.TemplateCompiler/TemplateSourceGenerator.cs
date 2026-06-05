using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CL.WebLogic.TemplateCompiler;

/// <summary>
/// Compiles WebLogic <c>.html</c> templates (provided as AdditionalFiles) into
/// C# render classes implementing <c>ICompiledTemplate</c>, registered at module
/// load. At runtime the theme engine prefers a compiled template when its
/// source hash matches the live file, falling back to the AST interpreter
/// otherwise — so generation is always a pure fast path.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor EmitFailed = new(
        id: "WLTC001",
        title: "Template compilation failed",
        messageFormat: "Template '{0}' could not be compiled and will render via the interpreter: {1}",
        category: "CL.WebLogic.TemplateCompiler",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var templates = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => (file.Path, Text: file.GetText(ct)?.ToString()))
            .Where(static t => t.Text is not null);

        context.RegisterSourceOutput(templates, static (spc, template) =>
        {
            var key = TemplatePathMapper.ToTemplateKey(template.Path);
            if (key is null)
                return; // not under templates/layouts/partials/widgets — ignore

            try
            {
                var source = TemplateEmitter.Emit(key, template.Text!);
                spc.AddSource(TemplatePathMapper.ToHintName(key), SourceText.From(source, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(EmitFailed, Location.None, key, ex.Message));
            }
        });
    }
}

/// <summary>
/// Maps an AdditionalFile's absolute path to the theme-relative key the engine
/// uses for registry lookups (e.g. <c>templates/home.html</c>), by locating the
/// last <c>templates/layouts/partials/widgets</c> segment.
/// </summary>
internal static class TemplatePathMapper
{
    private static readonly string[] Roots = { "templates", "layouts", "partials", "widgets" };

    public static string? ToTemplateKey(string filePath)
    {
        var normalized = (filePath ?? string.Empty).Replace('\\', '/');

        var best = -1;
        foreach (var root in Roots)
        {
            var marker = "/" + root + "/";
            var idx = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx + 1 > best)
                best = idx + 1;

            // Theme-relative paths that START with the root folder.
            if (best < 0 && normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                best = 0;
        }

        if (best < 0)
            return null;

        return normalized.Substring(best);
    }

    public static string ToHintName(string key)
    {
        var sb = new StringBuilder(key.Length + 5);
        foreach (var c in key)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        sb.Append(".g.cs");
        return sb.ToString();
    }

    public static string ToClassName(string key)
    {
        var sb = new StringBuilder(key.Length + 4);
        sb.Append("Tpl_");
        foreach (var c in key)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
