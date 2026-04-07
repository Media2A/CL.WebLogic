using System.Reflection;
using System.Text.Json.Serialization;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Forms;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WebFormAttribute : Attribute
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class WebFormFieldAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? HelpText { get; set; }
    public string? Placeholder { get; set; }
    public string? Section { get; set; }
    public string? SelectPrompt { get; set; }
    public string? OptionsProvider { get; set; }
    public string? SearchProvider { get; set; }
    public string? DependsOn { get; set; }
    public string? SearchPlaceholder { get; set; }
    public WebFormInputType InputType { get; set; } = WebFormInputType.Auto;
    public bool Required { get; set; }
    public bool Hidden { get; set; }
    public bool ReadOnly { get; set; }
    public int ColumnSpan { get; set; } = 6;
    public int MinSearchLength { get; set; }
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public string? Pattern { get; set; }
    public string? AllowedValues { get; set; }
    public double MinValue { get; set; } = double.NaN;
    public double MaxValue { get; set; } = double.NaN;
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class WebFileFieldAttribute : WebFormFieldAttribute
{
    public long MaxFileSizeBytes { get; set; }
    public string? AllowedContentTypes { get; set; }
    public string? AllowedExtensions { get; set; }
    public int MaxImageWidth { get; set; }
    public int MaxImageHeight { get; set; }
    public int MinImageWidth { get; set; }
    public int MinImageHeight { get; set; }
    public bool RequireImage { get; set; }
}

public enum WebFormInputType
{
    Auto,
    Text,
    TextArea,
    Email,
    Password,
    Number,
    Date,
    Checkbox,
    Select,
    Autocomplete,
    File
}

public sealed class WebFormDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required Type ModelType { get; init; }
    public required IReadOnlyList<WebFormFieldDefinition> Fields { get; init; }

    public WebClientFormSchema ToClientSchema() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Fields = Fields.Select(static field => field.ToClientField()).ToArray()
    };
}

public sealed class WebFormFieldDefinition
{
    [JsonIgnore]
    public required PropertyInfo Property { get; init; }

    public required string Name { get; init; }
    public required string Label { get; init; }
    public string Description { get; init; } = string.Empty;
    public string HelpText { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string SelectPrompt { get; init; } = string.Empty;
    public string OptionsProvider { get; init; } = string.Empty;
    public string SearchProvider { get; init; } = string.Empty;
    public string DependsOn { get; init; } = string.Empty;
    public string SearchPlaceholder { get; init; } = string.Empty;
    public required Type PropertyType { get; init; }
    public required WebFormInputType InputType { get; init; }
    public bool Required { get; init; }
    public bool Hidden { get; init; }
    public bool ReadOnly { get; init; }
    public int ColumnSpan { get; init; } = 6;
    public int MinSearchLength { get; init; }
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
    public string Pattern { get; init; } = string.Empty;
    public IReadOnlyList<WebFormSelectOption> Options { get; init; } = [];
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public WebFileValidationOptions? File { get; init; }

    public WebClientFormFieldSchema ToClientField() => new()
    {
        Name = Name,
        Label = Label,
        Description = Description,
        HelpText = HelpText,
        Placeholder = Placeholder,
        Section = Section,
        SelectPrompt = SelectPrompt,
        OptionsProvider = OptionsProvider,
        SearchProvider = SearchProvider,
        DependsOn = DependsOn,
        SearchPlaceholder = SearchPlaceholder,
        InputType = InputType.ToString().ToLowerInvariant(),
        Required = Required,
        Hidden = Hidden,
        ReadOnly = ReadOnly,
        ColumnSpan = ColumnSpan,
        MinSearchLength = MinSearchLength > 0 ? MinSearchLength : null,
        MinLength = MinLength > 0 ? MinLength : null,
        MaxLength = MaxLength > 0 ? MaxLength : null,
        Pattern = string.IsNullOrWhiteSpace(Pattern) ? null : Pattern,
        AllowedValues = Options.Count == 0 ? null : Options.Select(static option => option.Value).ToArray(),
        Options = Options.Count == 0 ? null : Options.Select(static option => option.ToClientOption()).ToArray(),
        MinValue = MinValue,
        MaxValue = MaxValue,
        File = File is null ? null : new WebClientFileValidationSchema
        {
            MaxFileSizeBytes = File.MaxFileSizeBytes > 0 ? File.MaxFileSizeBytes : null,
            AllowedContentTypes = File.AllowedContentTypes.Count == 0 ? null : File.AllowedContentTypes.ToArray(),
            AllowedExtensions = File.AllowedExtensions.Count == 0 ? null : File.AllowedExtensions.ToArray(),
            MaxImageWidth = File.MaxImageWidth > 0 ? File.MaxImageWidth : null,
            MaxImageHeight = File.MaxImageHeight > 0 ? File.MaxImageHeight : null,
            MinImageWidth = File.MinImageWidth > 0 ? File.MinImageWidth : null,
            MinImageHeight = File.MinImageHeight > 0 ? File.MinImageHeight : null,
            RequireImage = File.RequireImage
        }
    };
}

public sealed class WebFormSelectOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    public WebClientFormSelectOption ToClientOption() => new()
    {
        Value = Value,
        Label = Label
    };
}

public sealed class WebFileValidationOptions
{
    public long MaxFileSizeBytes { get; init; }
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = [];
    public IReadOnlyList<string> AllowedExtensions { get; init; } = [];
    public int MaxImageWidth { get; init; }
    public int MaxImageHeight { get; init; }
    public int MinImageWidth { get; init; }
    public int MinImageHeight { get; init; }
    public bool RequireImage { get; init; }
}

public sealed class WebFormBindingResult<TModel> where TModel : class, new()
{
    public required WebFormDefinition Definition { get; init; }
    public required TModel Model { get; init; }
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
    public required IReadOnlyDictionary<string, WebUploadedFile?> Files { get; init; }
    public required IReadOnlyList<WebFieldValidationError> Errors { get; init; }
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<WebFieldValidationError> GetFieldErrors(string fieldName) =>
        Errors.Where(error => string.Equals(error.FieldName, fieldName, StringComparison.OrdinalIgnoreCase)).ToArray();

    public string? GetFirstError(string fieldName) =>
        Errors.FirstOrDefault(error => string.Equals(error.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))?.Message;

    public TTarget MapTo<TTarget>() where TTarget : class, new() =>
        WebFormMapper.Map<TModel, TTarget>(Model);
}

public sealed class WebFieldValidationError
{
    public required string FieldName { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
}

public sealed class WebClientFormSchema
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required IReadOnlyList<WebClientFormFieldSchema> Fields { get; init; }
}

public sealed class WebClientFormFieldSchema
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string Description { get; init; } = string.Empty;
    public string HelpText { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string SelectPrompt { get; init; } = string.Empty;
    public string OptionsProvider { get; init; } = string.Empty;
    public string SearchProvider { get; init; } = string.Empty;
    public string DependsOn { get; init; } = string.Empty;
    public string SearchPlaceholder { get; init; } = string.Empty;
    public required string InputType { get; init; }
    public bool Required { get; init; }
    public bool Hidden { get; init; }
    public bool ReadOnly { get; init; }
    public int ColumnSpan { get; init; } = 6;
    public int? MinSearchLength { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }
    public string[]? AllowedValues { get; init; }
    public WebClientFormSelectOption[]? Options { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public WebClientFileValidationSchema? File { get; init; }
}

public sealed class WebClientFormSelectOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}

public sealed class WebClientFileValidationSchema
{
    public long? MaxFileSizeBytes { get; init; }
    public string[]? AllowedContentTypes { get; init; }
    public string[]? AllowedExtensions { get; init; }
    public int? MaxImageWidth { get; init; }
    public int? MaxImageHeight { get; init; }
    public int? MinImageWidth { get; init; }
    public int? MinImageHeight { get; init; }
    public bool RequireImage { get; init; }
}

public sealed class WebFormSchemaOptions
{
    public IReadOnlyDictionary<string, WebFormFieldOverride> FieldOverrides { get; init; } = new Dictionary<string, WebFormFieldOverride>(StringComparer.OrdinalIgnoreCase);
}

public sealed class WebFormFieldOverride
{
    public string? Section { get; init; }
    public string? HelpText { get; init; }
    public string? Placeholder { get; init; }
    public string? SelectPrompt { get; init; }
    public string? OptionsProvider { get; init; }
    public string? SearchProvider { get; init; }
    public string? DependsOn { get; init; }
    public string? SearchPlaceholder { get; init; }
    public bool? Hidden { get; init; }
    public bool? ReadOnly { get; init; }
    public int? ColumnSpan { get; init; }
    public int? MinSearchLength { get; init; }
    public IReadOnlyList<WebFormSelectOption>? Options { get; init; }
}
