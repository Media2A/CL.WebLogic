using System.Text;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Theming;

/// <summary>
/// Render-time state shared by the AST interpreter and compiled templates.
/// Mutable by design: loops and partials save/restore fields around nested
/// renders instead of allocating a new context per item.
/// </summary>
public sealed class WebTemplateContext
{
    /// <summary>Normalized path of the template currently rendering (e.g. <c>templates/home.html</c>).</summary>
    public required string TemplatePath { get; set; }

    public required string? ThemeRoot { get; init; }

    /// <summary>The page model (global defaults already merged; explicit values win).</summary>
    public required IReadOnlyDictionary<string, object?> Model { get; set; }

    public WebRequestContext? PageContext { get; init; }

    public WebPageMeta? Meta { get; init; }

    /// <summary>Rendered <c>{section:…}</c> bodies, consumed by the layout's <c>{rendersection:…}</c>.</summary>
    public IReadOnlyDictionary<string, string> Sections { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Recursion guard across layouts/partials (shared set, add/remove per template).</summary>
    public required ISet<string> VisitedTemplates { get; init; }

    /// <summary>The child template's rendered body, available to a layout via <c>{renderbody}</c>.</summary>
    public string? RenderBody { get; set; }

    /// <summary>The current <c>{foreach}</c> item — the <c>{item:…}</c> scope.</summary>
    public object? CurrentItem { get; set; }

    /// <summary>Loop metadata for the enclosing <c>{foreach}</c> — the <c>{loop:…}</c> scope.</summary>
    public TemplateLoopState? Loop { get; set; }

    /// <summary>Arguments passed into a parameterised partial — the <c>{param:…}</c> scope.</summary>
    public IReadOnlyDictionary<string, object?>? Params { get; set; }

    /// <summary>Active <c>{foreach … as alias}</c> bindings, by alias name.</summary>
    public IReadOnlyDictionary<string, object?>? Aliases { get; set; }

    /// <summary>The output buffer the current render appends to.</summary>
    public required StringBuilder Output { get; set; }

    /// <summary>Runtime services (partials, widgets, CSRF) provided by the theme engine.</summary>
    public required ITemplateRenderServices Services { get; init; }
}

/// <summary>
/// Engine services that compiled templates (and the interpreter) call back into
/// for the constructs that are inherently dynamic — partial dispatch, widget
/// invocation, CSRF tokens. Implemented by <see cref="ThemeManager"/>.
/// </summary>
public interface ITemplateRenderServices
{
    /// <summary>
    /// Renders another template (compiled when available and source-hash-valid,
    /// interpreted otherwise) into the context's output. <paramref name="path"/>
    /// is as written in the template; it is normalized against the caller.
    /// </summary>
    Task RenderPartialAsync(WebTemplateContext context, string path, IReadOnlyDictionary<string, object?>? parameters);

    /// <summary>Renders a <c>{widget:…}</c> with already-resolved attribute values.</summary>
    Task RenderWidgetAsync(WebTemplateContext context, string name, IReadOnlyDictionary<string, string> attributes);

    /// <summary>Renders a <c>{widgetarea:…}</c>.</summary>
    Task RenderWidgetAreaAsync(WebTemplateContext context, string area);

    /// <summary>Returns the CSRF markup/token for <c>{csrf}</c>/<c>{csrf_token}</c>/<c>{csrf_meta}</c>.</summary>
    string Csrf(WebTemplateContext context, Templates.Ast.CsrfKind kind);
}

/// <summary>Loop metadata exposed inside a <c>{foreach}</c> via the <c>{loop:…}</c> scope.</summary>
public sealed class TemplateLoopState
{
    public TemplateLoopState(int index, int count)
    {
        Index = index;
        Count = count;
    }

    public int Index { get; }
    public int Count { get; }
    public int Number => Index + 1;
    public bool First => Index == 0;
    public bool Last => Index == Count - 1;
    public bool Even => Index % 2 == 0;
    public bool Odd => Index % 2 != 0;
}
