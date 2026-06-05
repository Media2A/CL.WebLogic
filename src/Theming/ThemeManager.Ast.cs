using System.Collections.Concurrent;
using System.Text;
using CL.Common.Web;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using CL.WebLogic.Templates.Ast;
using CL.WebLogic.Templates.Parsing;

namespace CL.WebLogic.Theming;

/// <summary>
/// AST-based template rendering + the compiled-template dispatcher.
/// <para>
/// Every template render flows through <see cref="TryRenderPathAsync"/>: when a
/// build-time compiled class is registered for the path AND its source hash
/// matches the live file, the compiled renderer runs (no read, no parse);
/// otherwise the parse-once cached AST is walked by the interpreter below. Both
/// paths share <see cref="TemplateRuntime"/> for all value semantics, so output
/// is identical — the golden-parity suite enforces it.
/// </para>
/// </summary>
public sealed partial class ThemeManager : ITemplateRenderServices
{
    private readonly ConcurrentDictionary<string, TemplateDocument> _astCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (byte[] Bytes, string Hash)> _sourceHashCache =
        new(StringComparer.OrdinalIgnoreCase);

    internal void ClearAstCache()
    {
        _astCache.Clear();
        _sourceHashCache.Clear();
    }

    internal void InvalidateAstPath(string normalizedPath)
    {
        _astCache.TryRemove(normalizedPath, out _);
        _sourceHashCache.TryRemove(normalizedPath, out _);
    }

    /// <summary>
    /// Renders a template via the AST/compiled engine. Mirrors
    /// <see cref="RenderTemplateAsync"/> (global-model merge, layout/section
    /// handling, recursion guard).
    /// </summary>
    public async Task<string> RenderTemplateViaAstAsync(
        string templatePath,
        IReadOnlyDictionary<string, object?>? model,
        string? themeRoot,
        WebRequestContext? pageContext = null,
        WebPageMeta? meta = null)
    {
        var normalizedPath = NormalizeTemplatePath(templatePath, templatePath);

        var mergedModel = new Dictionary<string, object?>(GlobalModelDefaults, StringComparer.OrdinalIgnoreCase);
        if (model is not null)
            foreach (var kvp in model)
                mergedModel[kvp.Key] = kvp.Value;

        var output = new StringBuilder();
        var context = new WebTemplateContext
        {
            TemplatePath = normalizedPath,
            ThemeRoot = themeRoot,
            Model = mergedModel,
            PageContext = pageContext,
            Meta = meta,
            VisitedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Output = output,
            Services = this
        };

        if (!await TryRenderPathAsync(normalizedPath, context).ConfigureAwait(false))
            return $"<html><body><h1>Template not found</h1><p>{HtmlHelper.Encode(normalizedPath)}</p></body></html>";

        return output.ToString();
    }

    // ── unified dispatch ───────────────────────────────────────────────────

    /// <summary>
    /// Renders the template at <paramref name="normalizedPath"/> into
    /// <c>context.Output</c> — compiled when valid, interpreted otherwise.
    /// Returns false when the template doesn't exist anywhere.
    /// </summary>
    internal async Task<bool> TryRenderPathAsync(string normalizedPath, WebTemplateContext context)
    {
        if (!context.VisitedTemplates.Add(normalizedPath))
        {
            context.Output.Append($"<div class=\"weblogic-template-error\">Template recursion detected for {HtmlHelper.Encode(normalizedPath)}</div>");
            return true;
        }

        var previousPath = context.TemplatePath;
        context.TemplatePath = normalizedPath;
        try
        {
            if (_config.Theme.UseCompiledTemplates &&
                CompiledTemplateRegistry.TryGet(normalizedPath, out var compiled) &&
                await CompiledSourceMatchesAsync(compiled, normalizedPath, context.ThemeRoot).ConfigureAwait(false))
            {
                await RenderCompiledTemplateAsync(compiled, context).ConfigureAwait(false);
                return true;
            }

            var doc = await GetDocumentAsync(normalizedPath, context.ThemeRoot).ConfigureAwait(false);
            if (doc is null)
                return false;

            await RenderParsedTemplateAsync(doc, context).ConfigureAwait(false);
            return true;
        }
        finally
        {
            context.TemplatePath = previousPath;
            context.VisitedTemplates.Remove(normalizedPath);
        }
    }

    /// <summary>
    /// The staleness gate: a compiled template is only used when the on-disk
    /// source still matches what it was compiled from. A theme refreshed at
    /// runtime (git pull in the container entrypoint) therefore falls back to
    /// the interpreter for exactly the files that changed. A template with no
    /// on-disk source at all trusts the build.
    /// </summary>
    private async Task<bool> CompiledSourceMatchesAsync(ICompiledTemplate compiled, string normalizedPath, string? themeRoot)
    {
        var bytes = await ReadBytesAsync(normalizedPath, themeRoot).ConfigureAwait(false);
        if (bytes is null)
            return true;

        if (!_sourceHashCache.TryGetValue(normalizedPath, out var cached) || !ReferenceEquals(cached.Bytes, bytes))
        {
            var hash = TemplateSourceHash.Compute(Encoding.UTF8.GetString(bytes));
            cached = (bytes, hash);
            _sourceHashCache[normalizedPath] = cached;
        }

        return string.Equals(cached.Hash, compiled.SourceHash, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RenderCompiledTemplateAsync(ICompiledTemplate compiled, WebTemplateContext context)
    {
        if (compiled.LayoutPath is null)
        {
            await compiled.RenderBodyAsync(context).ConfigureAwait(false);
            return;
        }

        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await compiled.RenderSectionsAsync(context, sections).ConfigureAwait(false);

        var previousOutput = context.Output;
        context.Output = new StringBuilder();
        await compiled.RenderBodyAsync(context).ConfigureAwait(false);
        var body = context.Output.ToString();
        context.Output = previousOutput;

        await RenderLayoutAsync(compiled.LayoutPath, context, body, sections).ConfigureAwait(false);
    }

    private async Task RenderParsedTemplateAsync(TemplateDocument doc, WebTemplateContext context)
    {
        if (doc.LayoutPath is null)
        {
            await RenderNodesAsync(doc.Nodes, context).ConfigureAwait(false);
            return;
        }

        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previousOutput = context.Output;
        foreach (var section in doc.Sections)
        {
            context.Output = new StringBuilder();
            await RenderNodesAsync(section.Body, context).ConfigureAwait(false);
            sections[section.Name] = context.Output.ToString();
        }

        context.Output = new StringBuilder();
        await RenderNodesAsync(doc.Nodes, context).ConfigureAwait(false);
        var body = context.Output.ToString();
        context.Output = previousOutput;

        await RenderLayoutAsync(doc.LayoutPath, context, body, sections).ConfigureAwait(false);
    }

    private async Task RenderLayoutAsync(string layoutRaw, WebTemplateContext context, string body, Dictionary<string, string> sections)
    {
        var layoutPath = NormalizeTemplatePath(layoutRaw, context.TemplatePath);

        var previousSections = context.Sections;
        var previousBody = context.RenderBody;
        context.Sections = sections;
        context.RenderBody = body;
        try
        {
            if (!await TryRenderPathAsync(layoutPath, context).ConfigureAwait(false))
                context.Output.Append($"<html><body><h1>Layout not found</h1><p>{HtmlHelper.Encode(layoutPath)}</p></body></html>");
        }
        finally
        {
            context.Sections = previousSections;
            context.RenderBody = previousBody;
        }
    }

    private async Task<TemplateDocument?> GetDocumentAsync(string normalizedPath, string? themeRoot)
    {
        if (_config.Theme.EnableCaching && _astCache.TryGetValue(normalizedPath, out var cached))
            return cached;

        var text = await ReadTextAsync(normalizedPath, themeRoot).ConfigureAwait(false);
        if (text is null)
            return null;

        var doc = TemplateParser.Parse(text);
        if (_config.Theme.EnableCaching)
            _astCache[normalizedPath] = doc;
        return doc;
    }

    // ── interpreter (walks the AST; all value semantics via TemplateRuntime) ──

    private async Task RenderNodesAsync(IReadOnlyList<TemplateNode> nodes, WebTemplateContext context)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LiteralNode literal:
                    context.Output.Append(literal.Text);
                    break;

                case VerbatimNode verbatim:
                    context.Output.Append(verbatim.Text);
                    break;

                case TokenNode token:
                {
                    var value = await TemplateRuntime.ResolveAsync(context, token.Selector.Scope, token.Selector.Key, token.Selector.RawScope).ConfigureAwait(false);
                    value = TemplateRuntime.ApplyFilters(value, ConvertFilters(token.Filters));
                    context.Output.Append(TemplateRuntime.Format(value, encode: !token.Raw));
                    break;
                }

                case LegacyTokenNode legacy:
                    context.Output.Append(TemplateRuntime.LegacyToken(context, legacy.Dotted));
                    break;

                case CsrfNode csrf:
                    context.Output.Append(((ITemplateRenderServices)this).Csrf(context, csrf.Kind));
                    break;

                case RenderHeadNode:
                    context.Output.Append(TemplateRuntime.RenderHead(context));
                    break;

                case RenderBodyNode:
                    context.Output.Append(context.RenderBody ?? string.Empty);
                    break;

                case RenderSectionNode section:
                    context.Output.Append(TemplateRuntime.Section(context, section.Name));
                    break;

                case ForeachNode loop:
                    await RenderForeachAsync(loop, context).ConfigureAwait(false);
                    break;

                case IfNode conditional:
                    await RenderIfAsync(conditional, context).ConfigureAwait(false);
                    break;

                case SwitchNode @switch:
                    await RenderSwitchAsync(@switch, context).ConfigureAwait(false);
                    break;

                case PartialNode partial:
                {
                    IReadOnlyDictionary<string, object?>? parameters = null;
                    if (partial.Arguments.Count > 0)
                    {
                        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var arg in partial.Arguments)
                            dict[arg.Name] = arg.IsLiteral
                                ? arg.Literal
                                : await TemplateRuntime.ResolveAsync(context, arg.Selector!.Scope, arg.Selector.Key, arg.Selector.RawScope).ConfigureAwait(false);
                        parameters = dict;
                    }

                    await ((ITemplateRenderServices)this).RenderPartialAsync(context, partial.Path, parameters).ConfigureAwait(false);
                    break;
                }

                case WidgetNode widget:
                {
                    var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var attr in widget.Attributes)
                        attributes[attr.Name] = await RenderInlineRawAsync(attr.Value, context).ConfigureAwait(false);

                    await ((ITemplateRenderServices)this).RenderWidgetAsync(context, widget.Name, attributes).ConfigureAwait(false);
                    break;
                }

                case WidgetAreaNode area:
                    await ((ITemplateRenderServices)this).RenderWidgetAreaAsync(context, area.Area).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task RenderForeachAsync(ForeachNode node, WebTemplateContext context)
    {
        var source = await TemplateRuntime.ResolveAsync(context, node.Source.Scope, node.Source.Key, node.Source.RawScope).ConfigureAwait(false);
        var items = TemplateRuntime.AsItems(source);
        if (items.Count == 0)
            return;

        var previousItem = context.CurrentItem;
        var previousLoop = context.Loop;
        var previousAliases = context.Aliases;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                context.CurrentItem = items[i];
                context.Loop = new TemplateLoopState(i, items.Count);
                if (node.Alias is not null)
                    context.Aliases = TemplateRuntime.WithAlias(previousAliases, node.Alias, items[i]);

                await RenderNodesAsync(node.Body, context).ConfigureAwait(false);
            }
        }
        finally
        {
            context.CurrentItem = previousItem;
            context.Loop = previousLoop;
            context.Aliases = previousAliases;
        }
    }

    private async Task RenderIfAsync(IfNode node, WebTemplateContext context)
    {
        foreach (var branch in node.Branches)
        {
            if (await EvaluateConditionAsync(branch.Condition, context).ConfigureAwait(false))
            {
                await RenderNodesAsync(branch.Body, context).ConfigureAwait(false);
                return;
            }
        }

        if (node.Else is not null)
            await RenderNodesAsync(node.Else, context).ConfigureAwait(false);
    }

    private async Task RenderSwitchAsync(SwitchNode node, WebTemplateContext context)
    {
        var subject = TemplateRuntime.SwitchSubject(
            await TemplateRuntime.ResolveAsync(context, node.Subject.Scope, node.Subject.Key, node.Subject.RawScope).ConfigureAwait(false));

        foreach (var @case in node.Cases)
        {
            if (string.Equals(@case.Value, subject, StringComparison.OrdinalIgnoreCase))
            {
                await RenderNodesAsync(@case.Body, context).ConfigureAwait(false);
                return;
            }
        }

        if (node.Default is not null)
            await RenderNodesAsync(node.Default, context).ConfigureAwait(false);
    }

    private async Task<bool> EvaluateConditionAsync(TemplateCondition condition, WebTemplateContext context)
    {
        switch (condition)
        {
            case NotCondition not:
                return !await EvaluateConditionAsync(not.Inner, context).ConfigureAwait(false);
            case AuthCondition:
                return TemplateRuntime.IsAuthenticated(context);
            case AccessGroupCondition group:
                return TemplateRuntime.HasAnyAccessGroup(context, group.Groups.ToArray());
            case PermissionCondition permission:
                return TemplateRuntime.HasPermission(context, permission.Permission);
            case TruthyCondition truthy:
                return TemplateRuntime.Truthy(
                    await TemplateRuntime.ResolveAsync(context, truthy.Selector.Scope, truthy.Selector.Key, truthy.Selector.RawScope).ConfigureAwait(false));
            case CompareCondition compare:
            {
                var left = await TemplateRuntime.ResolveAsync(context, compare.Left.Scope, compare.Left.Key, compare.Left.RawScope).ConfigureAwait(false);
                object? right = compare.Right.IsLiteral
                    ? compare.Right.Literal
                    : await TemplateRuntime.ResolveAsync(context, compare.Right.Selector!.Scope, compare.Right.Selector.Key, compare.Right.Selector.RawScope).ConfigureAwait(false);
                return TemplateRuntime.Compare(left, compare.Op, right);
            }
            default:
                return false;
        }
    }

    /// <summary>Renders an inline fragment (literals + tokens) unencoded — widget attribute values.</summary>
    private async Task<string> RenderInlineRawAsync(IReadOnlyList<TemplateNode> nodes, WebTemplateContext context)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LiteralNode literal:
                    sb.Append(literal.Text);
                    break;
                case TokenNode token:
                {
                    var value = await TemplateRuntime.ResolveAsync(context, token.Selector.Scope, token.Selector.Key, token.Selector.RawScope).ConfigureAwait(false);
                    value = TemplateRuntime.ApplyFilters(value, ConvertFilters(token.Filters));
                    sb.Append(TemplateRuntime.Format(value, encode: false));
                    break;
                }
            }
        }
        return sb.ToString();
    }

    private static (string Name, string? Arg)[] ConvertFilters(IReadOnlyList<TemplateFilter> filters)
    {
        if (filters.Count == 0)
            return System.Array.Empty<(string, string?)>();

        var array = new (string Name, string? Arg)[filters.Count];
        for (var i = 0; i < filters.Count; i++)
            array[i] = (filters[i].Name, filters[i].Arg);
        return array;
    }

    // ── ITemplateRenderServices (compiled templates call back through these) ──

    async Task ITemplateRenderServices.RenderPartialAsync(WebTemplateContext context, string path, IReadOnlyDictionary<string, object?>? parameters)
    {
        var partialPath = NormalizeTemplatePath(path, context.TemplatePath);
        var previousParams = context.Params;
        context.Params = parameters;
        try
        {
            if (!await TryRenderPathAsync(partialPath, context).ConfigureAwait(false))
                context.Output.Append($"<!-- Partial not found: {HtmlHelper.Encode(partialPath)} -->");
        }
        finally
        {
            context.Params = previousParams;
        }
    }

    async Task ITemplateRenderServices.RenderWidgetAsync(WebTemplateContext context, string name, IReadOnlyDictionary<string, string> attributes)
    {
        if (!_widgets.TryGet(name, out var widget) || widget is null)
        {
            context.Output.Append($"<!-- Widget not found: {HtmlHelper.Encode(name)} -->");
            return;
        }

        if (context.PageContext is null)
            return;
        if (!widget.AllowAnonymous && !context.PageContext.IsAuthenticated)
            return;
        if (widget.RequiredAccessGroups.Length > 0 && !context.PageContext.HasAnyAccessGroup(widget.RequiredAccessGroups))
            return;

        attributes.TryGetValue("instance", out var instanceId);
        var widgetContext = new WebWidgetContext
        {
            Name = widget.Name,
            InstanceId = instanceId,
            Parameters = await MergeWidgetParametersAsync(widget.Name, instanceId, attributes).ConfigureAwait(false),
            Model = context.Model,
            Request = context.PageContext,
            Contributor = widget.Contributor,
            SettingsStore = _settingsStore
        };

        var result = await widget.Handler(widgetContext).ConfigureAwait(false);
        if (result.TemplatePath is not null)
        {
            var widgetPath = NormalizeTemplatePath(result.TemplatePath, context.TemplatePath);
            var previousModel = context.Model;
            context.Model = MergeModel(context.Model, result.Model);
            try
            {
                if (!await TryRenderPathAsync(widgetPath, context).ConfigureAwait(false))
                    context.Output.Append($"<!-- Widget template not found: {HtmlHelper.Encode(widgetPath)} -->");
            }
            finally
            {
                context.Model = previousModel;
            }
            return;
        }

        context.Output.Append(result.Html ?? string.Empty);
    }

    async Task ITemplateRenderServices.RenderWidgetAreaAsync(WebTemplateContext context, string area)
    {
        if (context.PageContext is null)
            return;

        foreach (var registration in _widgets.GetAreaWidgets(area))
        {
            if (!AreaMatchesRequest(registration, context.PageContext, context.PageContext.Path))
                continue;

            var attributes = new Dictionary<string, string>(registration.Parameters, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(registration.InstanceId))
                attributes["instance"] = registration.InstanceId!;

            await ((ITemplateRenderServices)this).RenderWidgetAsync(context, registration.WidgetName, attributes).ConfigureAwait(false);
        }
    }

    string ITemplateRenderServices.Csrf(WebTemplateContext context, CsrfKind kind)
    {
        var token = string.Empty;
        if (context.PageContext is not null && _security is not null)
            token = _security.GetOrCreateCsrfToken(context.PageContext.HttpContext);

        return kind switch
        {
            CsrfKind.Field => $"<input type=\"hidden\" name=\"_csrf\" value=\"{HtmlHelper.Encode(token)}\">",
            CsrfKind.Token => HtmlHelper.Encode(token),
            CsrfKind.Meta => $"<meta name=\"csrf-token\" content=\"{HtmlHelper.Encode(token)}\">",
            _ => string.Empty
        };
    }
}
