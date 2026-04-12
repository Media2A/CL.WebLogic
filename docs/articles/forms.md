# Forms & Validation

CL.WebLogic provides a C# model-driven form system with validation that runs on both client and server from a single source of truth.

## Define a Form Model

```csharp
[WebForm(Id = "contact", Name = "Contact Form")]
public class ContactForm
{
    [WebFormField(Label = "Name", Required = true, MinLength = 2, MaxLength = 100)]
    public string Name { get; set; } = "";

    [WebFormField(Label = "Email", InputType = WebFormInputType.Email, Required = true,
        Pattern = @"^[^@]+@[^@]+\.[^@]+$")]
    public string Email { get; set; } = "";

    [WebFormField(Label = "Message", InputType = WebFormInputType.TextArea,
        Required = true, MinLength = 10, MaxLength = 2000)]
    public string Message { get; set; } = "";
}
```

## Field Attributes

| Property | Type | Description |
|----------|------|-------------|
| `Name` | string | Form field name (defaults to property name) |
| `Label` | string | Display label |
| `InputType` | enum | Auto, Text, TextArea, Email, Password, Number, Date, Checkbox, Select, File |
| `Required` | bool | Field is required |
| `MinLength` | int | Minimum string length |
| `MaxLength` | int | Maximum string length |
| `Pattern` | string | Regex validation pattern |
| `MinValue` | double | Minimum numeric value |
| `MaxValue` | double | Maximum numeric value |
| `AllowedValues` | string | Comma-separated allowed values (for selects) |
| `Placeholder` | string | Input placeholder text |
| `Hidden` | bool | Field is hidden |
| `ReadOnly` | bool | Field is read-only |

## File Upload Fields

```csharp
[WebFileField(Label = "Avatar", Required = true,
    MaxFileSizeBytes = 2 * 1024 * 1024,
    AllowedContentTypes = "image/jpeg,image/png,image/webp",
    AllowedExtensions = ".jpg,.png,.webp",
    MaxImageWidth = 1024, MaxImageHeight = 1024)]
public WebUploadedFile? Avatar { get; set; }
```

## Server-Side Validation

```csharp
private async Task<WebResult> HandleSubmitAsync(WebRequestContext request)
{
    var result = await request.Forms.BindAsync<ContactForm>();

    if (!result.IsValid)
    {
        // Return errors — client auto-displays them per field
        return WebResult.Json(new
        {
            success = false,
            errors = result.Errors.Select(e => new
            {
                fieldName = e.FieldName,
                code = e.Code,
                message = e.Message
            })
        });
    }

    var model = result.Model;
    // model.Name, model.Email, model.Message are validated and bound
    // ...

    return WebResult.Commands(
        WebResult.ToastCommand("Message sent!", "success"));
}
```

## Client-Side Validation

Generate the schema JSON from your C# model:

```csharp
var schema = JsonSerializer.Serialize(
    request.Forms.GetClientSchema<ContactForm>());
```

Embed it in the template:

```html
<form method="post" action="/api/contact"
      data-weblogic-form="ajax"
      data-weblogic-form-schema="schema-contact">
    <div data-form-summary class="alert alert-danger d-none"></div>

    <input type="text" name="Name">
    <div data-form-error-for="Name" class="invalid-feedback d-none"></div>

    <input type="email" name="Email">
    <div data-form-error-for="Email" class="invalid-feedback d-none"></div>

    <textarea name="Message"></textarea>
    <div data-form-error-for="Message" class="invalid-feedback d-none"></div>

    <button type="submit">Send</button>
</form>

<script type="application/json" id="schema-contact">
    {raw:model:form_schema_json}
</script>
```

The client validates before submit:
- Required fields show "X is required"
- Length checks show min/max messages
- Pattern mismatches show format errors
- Invalid fields get `.is-invalid` class (Bootstrap styling)
- Summary div shows all errors as a list

## Error Display Elements

| Element | Purpose |
|---------|---------|
| `data-form-summary` | Shows all errors as a `<ul>` list |
| `data-form-error-for="fieldName"` | Per-field error message |
| `.is-invalid` | Auto-added to invalid inputs (Bootstrap) |
