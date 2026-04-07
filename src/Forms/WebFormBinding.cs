using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using CL.WebLogic;
using CL.WebLogic.Runtime;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace CL.WebLogic.Forms;

public sealed class WebFormContext
{
    private readonly WebRequestContext _request;

    internal WebFormContext(WebRequestContext request)
    {
        _request = request;
    }

    public WebFormDefinition GetDefinition<TModel>() where TModel : class, new() =>
        WebFormBinder.GetDefinition<TModel>();

    public async Task<WebClientFormSchema> GetClientSchemaAsync<TModel>(WebFormSchemaOptions? options = null) where TModel : class, new() =>
        (await WebFormBinder.ResolveDefinitionAsync(_request, WebFormBinder.GetDefinition<TModel>(), options).ConfigureAwait(false)).ToClientSchema();

    public WebClientFormSchema GetClientSchema<TModel>(WebFormSchemaOptions? options = null) where TModel : class, new() =>
        GetClientSchemaAsync<TModel>(options).GetAwaiter().GetResult();

    public Task<WebFormBindingResult<TModel>> BindAsync<TModel>(WebFormSchemaOptions? options = null) where TModel : class, new() =>
        WebFormBinder.BindAsync<TModel>(_request, options);

    public Task<WebFormBindingResult<TModel>> ValidateAsync<TModel>(WebFormSchemaOptions? options = null) where TModel : class, new() =>
        WebFormBinder.BindAsync<TModel>(_request, options);

    public async Task<string> RenderFormAsync<TModel>(WebFormRenderOptions? options = null) where TModel : class, new() =>
        WebFormRenderer.RenderForm(await WebFormBinder.ResolveDefinitionAsync(_request, GetDefinition<TModel>(), options?.SchemaOptions).ConfigureAwait(false), options);

    public string RenderForm<TModel>(WebFormRenderOptions? options = null) where TModel : class, new() =>
        RenderFormAsync<TModel>(options).GetAwaiter().GetResult();

    public async Task<string> RenderFormAsync<TModel>(WebFormRenderState state, WebFormRenderOptions? options = null) where TModel : class, new() =>
        WebFormRenderer.RenderForm(await WebFormBinder.ResolveDefinitionAsync(_request, GetDefinition<TModel>(), options?.SchemaOptions).ConfigureAwait(false), options, state);

    public string RenderForm<TModel>(WebFormRenderState state, WebFormRenderOptions? options = null) where TModel : class, new() =>
        RenderFormAsync<TModel>(state, options).GetAwaiter().GetResult();

    public async Task<string> RenderFormAsync<TModel>(WebFormBindingResult<TModel> result, WebFormRenderOptions? options = null) where TModel : class, new() =>
        WebFormRenderer.RenderForm(await WebFormBinder.ResolveDefinitionAsync(_request, result.Definition, options?.SchemaOptions).ConfigureAwait(false), options, WebFormRenderer.FromBindingResult(result));

    public string RenderForm<TModel>(WebFormBindingResult<TModel> result, WebFormRenderOptions? options = null) where TModel : class, new() =>
        RenderFormAsync(result, options).GetAwaiter().GetResult();
}

public static class WebFormBinder
{
    private static readonly ConcurrentDictionary<Type, WebFormDefinition> Definitions = new();

    public static WebFormDefinition GetDefinition<TModel>() where TModel : class, new() =>
        Definitions.GetOrAdd(typeof(TModel), BuildDefinition);

    public static WebFormDefinition? GetDefinitionById(string formId) =>
        Definitions.Values.FirstOrDefault(definition => string.Equals(definition.Id, formId, StringComparison.OrdinalIgnoreCase));

    public static WebFormDefinition ResolveDefinition(WebFormDefinition definition, WebFormSchemaOptions? options)
    {
        if (options is null || options.FieldOverrides.Count == 0)
            return definition;

        var fields = definition.Fields.Select(field =>
        {
            if (!options.FieldOverrides.TryGetValue(field.Name, out var fieldOverride) || fieldOverride is null)
                return field;

            return new WebFormFieldDefinition
            {
                Property = field.Property,
                Name = field.Name,
                Label = field.Label,
                Description = field.Description,
                HelpText = fieldOverride.HelpText ?? field.HelpText,
                Placeholder = fieldOverride.Placeholder ?? field.Placeholder,
                Section = fieldOverride.Section ?? field.Section,
                SelectPrompt = fieldOverride.SelectPrompt ?? field.SelectPrompt,
                OptionsProvider = fieldOverride.OptionsProvider ?? field.OptionsProvider,
                SearchProvider = fieldOverride.SearchProvider ?? field.SearchProvider,
                DependsOn = fieldOverride.DependsOn ?? field.DependsOn,
                SearchPlaceholder = fieldOverride.SearchPlaceholder ?? field.SearchPlaceholder,
                PropertyType = field.PropertyType,
                InputType = field.InputType,
                Required = field.Required,
                Hidden = fieldOverride.Hidden ?? field.Hidden,
                ReadOnly = fieldOverride.ReadOnly ?? field.ReadOnly,
                ColumnSpan = fieldOverride.ColumnSpan ?? field.ColumnSpan,
                MinSearchLength = fieldOverride.MinSearchLength ?? field.MinSearchLength,
                MinLength = field.MinLength,
                MaxLength = field.MaxLength,
                Pattern = field.Pattern,
                Options = fieldOverride.Options ?? field.Options,
                MinValue = field.MinValue,
                MaxValue = field.MaxValue,
                File = field.File
            };
        }).ToArray();

        return new WebFormDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            ModelType = definition.ModelType,
            Fields = fields
        };
    }

    public static async Task<WebFormDefinition> ResolveDefinitionAsync(
        WebRequestContext request,
        WebFormDefinition definition,
        WebFormSchemaOptions? options,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveDefinition(definition, options);
        var registry = WebLogicLibrary.GetRequired().FormOptionsProviders;
        var values = await BuildIncomingValuesAsync(request).ConfigureAwait(false);

        var fields = new List<WebFormFieldDefinition>(resolved.Fields.Count);
        foreach (var field in resolved.Fields)
        {
            if (registry is null || string.IsNullOrWhiteSpace(field.OptionsProvider))
            {
                fields.Add(field);
                continue;
            }

            var optionsList = await registry.ResolveAsync(field.OptionsProvider, request, resolved, field, values, cancellationToken).ConfigureAwait(false);
            fields.Add(new WebFormFieldDefinition
            {
                Property = field.Property,
                Name = field.Name,
                Label = field.Label,
                Description = field.Description,
                HelpText = field.HelpText,
                Placeholder = field.Placeholder,
                Section = field.Section,
                SelectPrompt = field.SelectPrompt,
                OptionsProvider = field.OptionsProvider,
                SearchProvider = field.SearchProvider,
                DependsOn = field.DependsOn,
                SearchPlaceholder = field.SearchPlaceholder,
                PropertyType = field.PropertyType,
                InputType = field.InputType,
                Required = field.Required,
                Hidden = field.Hidden,
                ReadOnly = field.ReadOnly,
                ColumnSpan = field.ColumnSpan,
                MinSearchLength = field.MinSearchLength,
                MinLength = field.MinLength,
                MaxLength = field.MaxLength,
                Pattern = field.Pattern,
                Options = optionsList,
                MinValue = field.MinValue,
                MaxValue = field.MaxValue,
                File = field.File
            });
        }

        return new WebFormDefinition
        {
            Id = resolved.Id,
            Name = resolved.Name,
            Description = resolved.Description,
            ModelType = resolved.ModelType,
            Fields = fields
        };
    }

    public static async Task<WebFormBindingResult<TModel>> BindAsync<TModel>(WebRequestContext request, WebFormSchemaOptions? options = null)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await ResolveDefinitionAsync(request, GetDefinition<TModel>(), options).ConfigureAwait(false);
        var model = new TModel();
        var formCollection = await request.ReadFormCollectionAsync().ConfigureAwait(false);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, WebUploadedFile?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<WebFieldValidationError>();

        foreach (var field in definition.Fields)
        {
            var rawValue = GetIncomingValue(request, formCollection, field.Name);
            var uploadedFile = GetIncomingFile(formCollection, field.Name);

            if (field.File is not null)
            {
                files[field.Name] = uploadedFile;
                values[field.Name] = uploadedFile?.FileName;

                if (uploadedFile is null)
                {
                    if (field.Required)
                    {
                        errors.Add(CreateError(field.Name, "required", $"{field.Label} is required."));
                    }

                    continue;
                }

                SetPropertyValue(model, field.Property, field.PropertyType == typeof(IFormFile) ? uploadedFile.InnerFile : uploadedFile);
                ValidateFileField(field, uploadedFile, errors);
                continue;
            }

            values[field.Name] = rawValue;
            var conversion = ConvertValue(field.PropertyType, rawValue);
            if (!conversion.Success)
            {
                errors.Add(CreateError(field.Name, "invalid", conversion.ErrorMessage ?? $"{field.Label} is invalid."));
                continue;
            }

            SetPropertyValue(model, field.Property, conversion.Value);
            ValidateScalarField(field, rawValue, conversion.Value, errors);
            await ValidateProviderSelectionAsync(request, definition, field, values, rawValue, errors).ConfigureAwait(false);
        }

        return new WebFormBindingResult<TModel>
        {
            Definition = definition,
            Model = model,
            Values = values,
            Files = files,
            Errors = errors
        };
    }

    private static WebFormDefinition BuildDefinition(Type modelType)
    {
        var formAttribute = modelType.GetCustomAttribute<WebFormAttribute>();
        var fields = modelType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanWrite)
            .Select(BuildFieldDefinition)
            .Where(static field => field is not null)
            .Cast<WebFormFieldDefinition>()
            .ToArray();

        return new WebFormDefinition
        {
            Id = formAttribute?.Id ?? modelType.Name,
            Name = formAttribute?.Name ?? modelType.Name,
            Description = formAttribute?.Description ?? string.Empty,
            ModelType = modelType,
            Fields = fields
        };
    }

    private static WebFormFieldDefinition? BuildFieldDefinition(PropertyInfo property)
    {
        var fileAttribute = property.GetCustomAttribute<WebFileFieldAttribute>();
        var fieldAttribute = (WebFormFieldAttribute?)fileAttribute ?? property.GetCustomAttribute<WebFormFieldAttribute>();
        if (fieldAttribute is null)
            return null;

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var inputType = fieldAttribute.InputType == WebFormInputType.Auto
            ? InferInputType(propertyType, fileAttribute is not null)
            : fieldAttribute.InputType;

        return new WebFormFieldDefinition
        {
            Property = property,
            Name = fieldAttribute.Name ?? property.Name,
            Label = fieldAttribute.Label ?? SplitPascalCase(property.Name),
            Description = fieldAttribute.Description ?? string.Empty,
            HelpText = fieldAttribute.HelpText ?? string.Empty,
            Placeholder = fieldAttribute.Placeholder ?? string.Empty,
            Section = fieldAttribute.Section ?? string.Empty,
            SelectPrompt = fieldAttribute.SelectPrompt ?? string.Empty,
            OptionsProvider = fieldAttribute.OptionsProvider ?? string.Empty,
            SearchProvider = fieldAttribute.SearchProvider ?? string.Empty,
            DependsOn = fieldAttribute.DependsOn ?? string.Empty,
            SearchPlaceholder = fieldAttribute.SearchPlaceholder ?? string.Empty,
            PropertyType = property.PropertyType,
            InputType = inputType,
            Required = fieldAttribute.Required,
            Hidden = fieldAttribute.Hidden,
            ReadOnly = fieldAttribute.ReadOnly,
            ColumnSpan = fieldAttribute.ColumnSpan <= 0 ? 6 : Math.Clamp(fieldAttribute.ColumnSpan, 1, 12),
            MinSearchLength = fieldAttribute.MinSearchLength,
            MinLength = fieldAttribute.MinLength,
            MaxLength = fieldAttribute.MaxLength,
            Pattern = fieldAttribute.Pattern ?? string.Empty,
            Options = ParseDelimitedList(fieldAttribute.AllowedValues)
                .Select(static option => new WebFormSelectOption
                {
                    Value = option,
                    Label = SplitPascalCase(option.Replace("-", " ").Replace("_", " "))
                })
                .ToArray(),
            MinValue = double.IsNaN(fieldAttribute.MinValue) ? null : (decimal)fieldAttribute.MinValue,
            MaxValue = double.IsNaN(fieldAttribute.MaxValue) ? null : (decimal)fieldAttribute.MaxValue,
            File = fileAttribute is null
                ? null
                : new WebFileValidationOptions
                {
                    MaxFileSizeBytes = fileAttribute.MaxFileSizeBytes,
                    AllowedContentTypes = ParseDelimitedList(fileAttribute.AllowedContentTypes),
                    AllowedExtensions = ParseDelimitedList(fileAttribute.AllowedExtensions)
                        .Select(static item => item.StartsWith('.') ? item : $".{item}")
                        .ToArray(),
                    MaxImageWidth = fileAttribute.MaxImageWidth,
                    MaxImageHeight = fileAttribute.MaxImageHeight,
                    MinImageWidth = fileAttribute.MinImageWidth,
                    MinImageHeight = fileAttribute.MinImageHeight,
                    RequireImage = fileAttribute.RequireImage
                }
        };
    }

    private static WebFormInputType InferInputType(Type propertyType, bool isFileField)
    {
        if (isFileField || propertyType == typeof(IFormFile) || propertyType == typeof(WebUploadedFile))
            return WebFormInputType.File;

        if (propertyType == typeof(bool))
            return WebFormInputType.Checkbox;

        if (propertyType == typeof(int) ||
            propertyType == typeof(long) ||
            propertyType == typeof(float) ||
            propertyType == typeof(double) ||
            propertyType == typeof(decimal))
            return WebFormInputType.Number;

        if (propertyType == typeof(DateTime) || propertyType == typeof(DateOnly))
            return WebFormInputType.Date;

        return WebFormInputType.Text;
    }

    private static IReadOnlyList<string> ParseDelimitedList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? GetIncomingValue(WebRequestContext request, IFormCollection formCollection, string fieldName)
    {
        if (formCollection.TryGetValue(fieldName, out var formValue) && !string.IsNullOrWhiteSpace(formValue.ToString()))
            return formValue.ToString();

        return request.GetQuery(fieldName);
    }

    private static async Task<IReadOnlyDictionary<string, string?>> BuildIncomingValuesAsync(WebRequestContext request)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Query)
            values[pair.Key] = pair.Value;

        if (!request.HttpContext.Request.HasFormContentType)
            return values;

        var formCollection = await request.ReadFormCollectionAsync().ConfigureAwait(false);
        foreach (var pair in formCollection)
            values[pair.Key] = pair.Value.ToString();

        return values;
    }

    private static WebUploadedFile? GetIncomingFile(IFormCollection formCollection, string fieldName)
    {
        var file = formCollection.Files.GetFile(fieldName);
        return file is null
            ? null
            : new WebUploadedFile(
                file.Name,
                file.FileName,
                file.ContentType,
                file.Length,
                file);
    }

    private static void ValidateScalarField(
        WebFormFieldDefinition field,
        string? rawValue,
        object? parsedValue,
        List<WebFieldValidationError> errors)
    {
        if (field.Required && string.IsNullOrWhiteSpace(rawValue))
        {
            errors.Add(CreateError(field.Name, "required", $"{field.Label} is required."));
            return;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        if (field.MinLength > 0 && rawValue.Length < field.MinLength)
            errors.Add(CreateError(field.Name, "min_length", $"{field.Label} must be at least {field.MinLength} characters long."));

        if (field.MaxLength > 0 && rawValue.Length > field.MaxLength)
            errors.Add(CreateError(field.Name, "max_length", $"{field.Label} must be no more than {field.MaxLength} characters long."));

        if (!string.IsNullOrWhiteSpace(field.Pattern) && !Regex.IsMatch(rawValue, field.Pattern))
            errors.Add(CreateError(field.Name, "pattern", $"{field.Label} is not in the expected format."));

        if (field.Options.Count > 0 && !field.Options.Any(option => string.Equals(option.Value, rawValue, StringComparison.OrdinalIgnoreCase)))
            errors.Add(CreateError(field.Name, "allowed_values", $"{field.Label} must be one of: {string.Join(", ", field.Options.Select(static option => option.Label))}."));

        if (parsedValue is not null && TryConvertToDecimal(parsedValue, out var numericValue))
        {
            if (field.MinValue.HasValue && numericValue < field.MinValue.Value)
                errors.Add(CreateError(field.Name, "min_value", $"{field.Label} must be at least {field.MinValue.Value}."));

            if (field.MaxValue.HasValue && numericValue > field.MaxValue.Value)
                errors.Add(CreateError(field.Name, "max_value", $"{field.Label} must be no more than {field.MaxValue.Value}."));
        }
    }

    private static async Task ValidateProviderSelectionAsync(
        WebRequestContext request,
        WebFormDefinition definition,
        WebFormFieldDefinition field,
        IReadOnlyDictionary<string, string?> values,
        string? rawValue,
        List<WebFieldValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || string.IsNullOrWhiteSpace(field.SearchProvider))
            return;

        var registry = WebLogicLibrary.GetRequired().FormOptionsProviders;
        var isValid = await registry.IsValidSearchSelectionAsync(
            field.SearchProvider,
            request,
            definition,
            field,
            values,
            rawValue).ConfigureAwait(false);

        if (!isValid)
        {
            errors.Add(CreateError(field.Name, "search_selection", $"{field.Label} must be selected from the search results."));
        }
    }

    private static void ValidateFileField(
        WebFormFieldDefinition field,
        WebUploadedFile file,
        List<WebFieldValidationError> errors)
    {
        var options = field.File;
        if (options is null)
            return;

        if (options.MaxFileSizeBytes > 0 && file.Length > options.MaxFileSizeBytes)
            errors.Add(CreateError(field.Name, "max_file_size", $"{field.Label} exceeds the maximum file size."));

        var extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (options.AllowedExtensions.Count > 0 &&
            !string.IsNullOrWhiteSpace(extension) &&
            !options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(CreateError(field.Name, "extension", $"{field.Label} has an unsupported file extension."));
        }

        if (options.AllowedContentTypes.Count > 0 &&
            !string.IsNullOrWhiteSpace(file.ContentType) &&
            !options.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(CreateError(field.Name, "content_type", $"{field.Label} has an unsupported content type."));
        }

        var requiresImageChecks = options.RequireImage ||
                                  options.MaxImageWidth > 0 ||
                                  options.MaxImageHeight > 0 ||
                                  options.MinImageWidth > 0 ||
                                  options.MinImageHeight > 0;

        if (!requiresImageChecks)
            return;

        try
        {
            using var stream = file.OpenReadStream();
            using var managed = new SKManagedStream(stream, disposeManagedStream: false);
            using var codec = SKCodec.Create(managed);
            if (codec is null)
            {
                errors.Add(CreateError(field.Name, "image", $"{field.Label} must be a valid image file."));
                return;
            }

            var width = codec.Info.Width;
            var height = codec.Info.Height;

            if (options.MaxImageWidth > 0 && width > options.MaxImageWidth)
                errors.Add(CreateError(field.Name, "max_image_width", $"{field.Label} is wider than the allowed image width."));

            if (options.MaxImageHeight > 0 && height > options.MaxImageHeight)
                errors.Add(CreateError(field.Name, "max_image_height", $"{field.Label} is taller than the allowed image height."));

            if (options.MinImageWidth > 0 && width < options.MinImageWidth)
                errors.Add(CreateError(field.Name, "min_image_width", $"{field.Label} is narrower than the required image width."));

            if (options.MinImageHeight > 0 && height < options.MinImageHeight)
                errors.Add(CreateError(field.Name, "min_image_height", $"{field.Label} is shorter than the required image height."));
        }
        catch
        {
            errors.Add(CreateError(field.Name, "image", $"{field.Label} could not be inspected as an image."));
        }
    }

    private static (bool Success, object? Value, string? ErrorMessage) ConvertValue(Type destinationType, string? rawValue)
    {
        var targetType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        if (targetType == typeof(string))
            return (true, rawValue ?? string.Empty, null);

        if (string.IsNullOrWhiteSpace(rawValue))
            return (true, destinationType.IsValueType && Nullable.GetUnderlyingType(destinationType) is null ? Activator.CreateInstance(targetType) : null, null);

        try
        {
            if (targetType == typeof(bool))
            {
                var value = rawValue.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                            rawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                            rawValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                            rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
                return (true, value, null);
            }

            if (targetType == typeof(int))
                return (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value), value, null);

            if (targetType == typeof(long))
                return (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value), value, null);

            if (targetType == typeof(decimal))
                return (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value), value, null);

            if (targetType == typeof(double))
                return (double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value), value, null);

            if (targetType == typeof(DateTime))
                return (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value), value, null);

            if (targetType == typeof(DateOnly))
            {
                var success = DateOnly.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly);
                return (success, dateOnly, null);
            }

            if (targetType.IsEnum)
            {
                var success = Enum.TryParse(targetType, rawValue, true, out var enumValue);
                return (success, enumValue, null);
            }

            return (true, Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture), null);
        }
        catch
        {
            return (false, null, $"{SplitPascalCase(targetType.Name)} is invalid.");
        }
    }

    private static bool TryConvertToDecimal(object value, out decimal decimalValue)
    {
        switch (value)
        {
            case decimal exact:
                decimalValue = exact;
                return true;
            case byte or short or int or long or float or double:
                decimalValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            default:
                decimalValue = default;
                return false;
        }
    }

    private static void SetPropertyValue(object model, PropertyInfo property, object? value)
    {
        if (value is null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            return;

        property.SetValue(model, value);
    }

    private static WebFieldValidationError CreateError(string fieldName, string code, string message) => new()
    {
        FieldName = fieldName,
        Code = code,
        Message = message
    };

    private static string SplitPascalCase(string value) =>
        Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
}
