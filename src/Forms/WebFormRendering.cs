using System.Net;
using System.Text;

namespace CL.WebLogic.Forms;

public sealed class WebFormRenderOptions
{
    public string Action { get; init; } = string.Empty;
    public string Method { get; init; } = "post";
    public string CssClass { get; init; } = "mt-4";
    public string FieldsContainerCssClass { get; init; } = "row g-3";
    public string SubmitLabel { get; init; } = "Submit";
    public string ResetLabel { get; init; } = "Reset";
    public bool IncludeResetButton { get; init; } = true;
    public string? SchemaId { get; init; }
    public WebFormSchemaOptions? SchemaOptions { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WebFormRenderState
{
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<WebFieldValidationError> Errors { get; init; } = [];

    public string? GetValue(string fieldName) =>
        Values.TryGetValue(fieldName, out var value) ? value : null;

    public string? GetError(string fieldName) =>
        Errors.FirstOrDefault(error => string.Equals(error.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))?.Message;
}

public static class WebFormRenderer
{
    public static string RenderForm(
        WebFormDefinition definition,
        WebFormRenderOptions? options = null,
        WebFormRenderState? state = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var renderOptions = options ?? new WebFormRenderOptions();
        var renderState = state ?? new WebFormRenderState();
        var formAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["method"] = string.IsNullOrWhiteSpace(renderOptions.Method) ? "post" : renderOptions.Method,
            ["action"] = renderOptions.Action,
            ["class"] = renderOptions.CssClass
        };

        if (definition.Fields.Any(static field => field.File is not null))
            formAttributes["enctype"] = "multipart/form-data";

        if (!string.IsNullOrWhiteSpace(renderOptions.SchemaId))
            formAttributes["data-weblogic-form-schema"] = renderOptions.SchemaId;
        formAttributes["data-weblogic-form-id"] = definition.Id;

        foreach (var pair in renderOptions.Attributes)
            formAttributes[pair.Key] = pair.Value;

        if (!formAttributes.ContainsKey("data-weblogic-form"))
            formAttributes["data-weblogic-form"] = "ajax";

        var builder = new StringBuilder();
        builder.Append("<form");
        foreach (var pair in formAttributes)
        {
            builder.Append(' ')
                .Append(WebUtility.HtmlEncode(pair.Key))
                .Append("=\"")
                .Append(WebUtility.HtmlEncode(pair.Value))
                .Append('"');
        }
        builder.Append('>');
        builder.Append("""<div class="alert alert-danger d-none" data-form-summary></div>""");
        builder.Append("<div class=\"").Append(WebUtility.HtmlEncode(renderOptions.FieldsContainerCssClass)).Append("\">");

        foreach (var field in definition.Fields)
            builder.Append(RenderField(field, renderState));

        builder.Append("</div>");
        builder.Append("""
            <div class="d-flex gap-2 flex-wrap mt-4">
            """);
        builder.Append("<button class=\"btn btn-warning fw-semibold\" type=\"submit\">")
            .Append(WebUtility.HtmlEncode(renderOptions.SubmitLabel))
            .Append("</button>");

        if (renderOptions.IncludeResetButton)
        {
            builder.Append("<button class=\"btn btn-outline-light\" type=\"reset\">")
                .Append(WebUtility.HtmlEncode(renderOptions.ResetLabel))
                .Append("</button>");
        }

        builder.Append("</div></form>");
        return builder.ToString();
    }

    public static WebFormRenderState FromBindingResult<TModel>(WebFormBindingResult<TModel> result)
        where TModel : class, new() =>
        new()
        {
            Values = result.Values,
            Errors = result.Errors
        };

    private static string RenderField(WebFormFieldDefinition field, WebFormRenderState state)
    {
        if (field.Hidden)
        {
            return $"""<input type="hidden" id="{WebUtility.HtmlEncode(field.Name)}" name="{WebUtility.HtmlEncode(field.Name)}" value="{WebUtility.HtmlEncode(state.GetValue(field.Name) ?? string.Empty)}">""";
        }

        var error = state.GetError(field.Name);
        var value = state.GetValue(field.Name) ?? string.Empty;
        var displayValue = state.GetValue($"{field.Name}__display")
            ?? field.Options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Label
            ?? value;
        var inputId = field.Name;
        var columnClass = $"col-md-{Math.Clamp(field.ColumnSpan, 1, 12)}";

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(field.Section))
        {
            builder.Append("<div class=\"col-12\"><div class=\"pt-2 mt-1 border-top border-secondary-subtle\"><p class=\"section-title mb-1\">")
                .Append(WebUtility.HtmlEncode(field.Section))
                .Append("</p></div></div>");
        }
        builder.Append("<div class=\"").Append(columnClass).Append("\">");
        builder.Append("<label class=\"form-label\" for=\"").Append(WebUtility.HtmlEncode(inputId)).Append("\">")
            .Append(WebUtility.HtmlEncode(field.Label))
            .Append("</label>");

        switch (field.InputType)
        {
            case WebFormInputType.TextArea:
                builder.Append("<textarea class=\"form-control")
                    .Append(string.IsNullOrWhiteSpace(error) ? string.Empty : " is-invalid")
                    .Append("\" id=\"").Append(WebUtility.HtmlEncode(inputId))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\" rows=\"4\"");
                if (field.ReadOnly)
                    builder.Append(" readonly");
                if (!string.IsNullOrWhiteSpace(field.Placeholder))
                    builder.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(field.Placeholder)).Append("\"");
                builder.Append(">")
                    .Append(WebUtility.HtmlEncode(value))
                    .Append("</textarea>");
                break;

            case WebFormInputType.Select:
                builder.Append("<select class=\"form-select")
                    .Append(string.IsNullOrWhiteSpace(error) ? string.Empty : " is-invalid")
                    .Append("\" id=\"").Append(WebUtility.HtmlEncode(inputId))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\"");
                if (!string.IsNullOrWhiteSpace(field.OptionsProvider))
                    builder.Append(" data-weblogic-options-provider=\"").Append(WebUtility.HtmlEncode(field.OptionsProvider)).Append("\"");
                if (!string.IsNullOrWhiteSpace(field.DependsOn))
                    builder.Append(" data-weblogic-options-depends-on=\"").Append(WebUtility.HtmlEncode(field.DependsOn)).Append("\"");
                builder.Append('>');
                builder.Append("<option value=\"\">")
                    .Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(field.SelectPrompt) ? "Pick an option" : field.SelectPrompt))
                    .Append("</option>");
                foreach (var option in field.Options)
                {
                    builder.Append("<option value=\"").Append(WebUtility.HtmlEncode(option.Value)).Append("\"");
                    if (string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
                        builder.Append(" selected");
                    if (field.ReadOnly)
                        builder.Append(" disabled");
                    builder.Append(">")
                        .Append(WebUtility.HtmlEncode(option.Label))
                        .Append("</option>");
                }
                builder.Append("</select>");
                break;

            case WebFormInputType.Autocomplete:
                builder.Append("<div class=\"weblogic-autocomplete\" data-weblogic-autocomplete-field=\"")
                    .Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\">");
                builder.Append("<input type=\"hidden\" id=\"").Append(WebUtility.HtmlEncode(inputId))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\" value=\"").Append(WebUtility.HtmlEncode(value))
                    .Append("\">");
                builder.Append("<input class=\"form-control weblogic-autocomplete-input")
                    .Append(string.IsNullOrWhiteSpace(error) ? string.Empty : " is-invalid")
                    .Append("\" id=\"").Append(WebUtility.HtmlEncode($"{inputId}__display"))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode($"{field.Name}__display"))
                    .Append("\" type=\"text\" autocomplete=\"off\"");
                if (!string.IsNullOrWhiteSpace(field.Placeholder))
                    builder.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(field.Placeholder)).Append("\"");
                if (!string.IsNullOrWhiteSpace(displayValue))
                    builder.Append(" value=\"").Append(WebUtility.HtmlEncode(displayValue)).Append("\"");
                if (!string.IsNullOrWhiteSpace(field.SearchProvider))
                    builder.Append(" data-weblogic-search-provider=\"").Append(WebUtility.HtmlEncode(field.SearchProvider)).Append("\"");
                if (!string.IsNullOrWhiteSpace(field.DependsOn))
                    builder.Append(" data-weblogic-search-depends-on=\"").Append(WebUtility.HtmlEncode(field.DependsOn)).Append("\"");
                if (field.MinSearchLength > 0)
                    builder.Append(" data-weblogic-search-min-length=\"").Append(field.MinSearchLength).Append("\"");
                if (!string.IsNullOrWhiteSpace(field.SearchPlaceholder))
                    builder.Append(" data-weblogic-search-placeholder=\"").Append(WebUtility.HtmlEncode(field.SearchPlaceholder)).Append("\"");
                builder.Append(" data-weblogic-autocomplete=\"true\" data-weblogic-autocomplete-display-for=\"")
                    .Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\"");
                if (field.ReadOnly)
                    builder.Append(" readonly");
                builder.Append(">");
                builder.Append("<div class=\"list-group weblogic-autocomplete-results d-none\" data-weblogic-autocomplete-results></div>");
                builder.Append("</div>");
                break;

            case WebFormInputType.File:
                builder.Append("<input class=\"form-control")
                    .Append(string.IsNullOrWhiteSpace(error) ? string.Empty : " is-invalid")
                    .Append("\" id=\"").Append(WebUtility.HtmlEncode(inputId))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\" type=\"file\"");
                if (field.ReadOnly)
                    builder.Append(" disabled");
                var accept = BuildAcceptValue(field);
                if (!string.IsNullOrWhiteSpace(accept))
                    builder.Append(" accept=\"").Append(WebUtility.HtmlEncode(accept)).Append("\"");
                builder.Append(">");
                if (field.File is not null)
                {
                    builder.Append("<div class=\"form-text\">");
                    if (field.File.MaxFileSizeBytes > 0)
                        builder.Append("Max ").Append(WebUtility.HtmlEncode(FormatFileSize(field.File.MaxFileSizeBytes))).Append(". ");
                    if (field.File.RequireImage)
                        builder.Append("Image validation is enabled. ");
                    if (field.File.MaxImageWidth > 0 || field.File.MaxImageHeight > 0)
                        builder.Append("Image dimensions are checked. ");
                    builder.Append("</div>");
                }
                break;

            default:
                builder.Append("<input class=\"form-control")
                    .Append(string.IsNullOrWhiteSpace(error) ? string.Empty : " is-invalid")
                    .Append("\" id=\"").Append(WebUtility.HtmlEncode(inputId))
                    .Append("\" name=\"").Append(WebUtility.HtmlEncode(field.Name))
                    .Append("\" type=\"").Append(WebUtility.HtmlEncode(MapInputType(field.InputType))).Append("\"");
                if (field.ReadOnly)
                    builder.Append(" readonly");
                if (!string.IsNullOrWhiteSpace(field.Placeholder))
                    builder.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(field.Placeholder)).Append("\"");
                if (!string.IsNullOrWhiteSpace(value))
                    builder.Append(" value=\"").Append(WebUtility.HtmlEncode(value)).Append("\"");
                if (field.MinValue.HasValue)
                    builder.Append(" min=\"").Append(field.MinValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\"");
                if (field.MaxValue.HasValue)
                    builder.Append(" max=\"").Append(field.MaxValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\"");
                builder.Append(">");
                break;
        }

        var helpText = !string.IsNullOrWhiteSpace(field.HelpText) ? field.HelpText : field.Description;
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            builder.Append("<div class=\"form-text\">")
                .Append(WebUtility.HtmlEncode(helpText))
                .Append("</div>");
        }

        builder.Append("<div class=\"invalid-feedback")
            .Append(string.IsNullOrWhiteSpace(error) ? " d-none" : string.Empty)
            .Append("\" data-form-error-for=\"").Append(WebUtility.HtmlEncode(field.Name)).Append("\">")
            .Append(WebUtility.HtmlEncode(error ?? string.Empty))
            .Append("</div>");

        builder.Append("</div>");
        return builder.ToString();
    }

    private static string MapInputType(WebFormInputType inputType) => inputType switch
    {
        WebFormInputType.Email => "email",
        WebFormInputType.Password => "password",
        WebFormInputType.Number => "number",
        WebFormInputType.Date => "date",
        _ => "text"
    };

    private static string BuildAcceptValue(WebFormFieldDefinition field)
    {
        if (field.File is null)
            return string.Empty;

        var values = field.File.AllowedExtensions
            .Concat(field.File.AllowedContentTypes)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(",", values);
    }

    private static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes >= 1_000_000)
            return $"{sizeBytes / 1_000_000d:0.#} MB";
        if (sizeBytes >= 1_000)
            return $"{sizeBytes / 1_000d:0.#} KB";
        return $"{sizeBytes} bytes";
    }
}
