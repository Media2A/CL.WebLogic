using CL.WebLogic.Forms;
using Microsoft.AspNetCore.Http;

namespace StarterWebsite.Application.Forms;

[WebForm(
    Id = "starter.profile-intake",
    Name = "Profile Intake",
    Description = "Demo form showing server-generated validation schema, client-side validation, and file/image checks.")]
public sealed class ProfileIntakeForm
{
    [WebFormField(
        Section = "Identity",
        Label = "Display name",
        HelpText = "This is the public-facing display name for the profile preview.",
        Placeholder = "Ada Lovelace",
        Required = true,
        MinLength = 3,
        MaxLength = 80,
        ColumnSpan = 6)]
    public string DisplayName { get; set; } = string.Empty;

    [WebFormField(
        Section = "Identity",
        Label = "Email address",
        Placeholder = "ada@example.com",
        Required = true,
        Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        ColumnSpan = 6)]
    public string EmailAddress { get; set; } = string.Empty;

    [WebFormField(
        Section = "Profile settings",
        Label = "Country",
        InputType = WebFormInputType.Select,
        Required = true,
        SelectPrompt = "Pick a country",
        OptionsProvider = "starter.countries",
        ColumnSpan = 6)]
    public string Country { get; set; } = string.Empty;

    [WebFormField(
        Section = "Profile settings",
        Label = "Local office",
        InputType = WebFormInputType.Select,
        Required = true,
        SelectPrompt = "Pick an office",
        OptionsProvider = "starter.offices",
        DependsOn = nameof(Country),
        ColumnSpan = 6)]
    public string LocalOffice { get; set; } = string.Empty;

    [WebFormField(
        Section = "Profile settings",
        Label = "Preferred mentor",
        InputType = WebFormInputType.Autocomplete,
        Required = true,
        Placeholder = "Search mentors by name or specialty",
        SearchPlaceholder = "Type at least 2 letters to search mentors",
        SearchProvider = "starter.mentors",
        DependsOn = nameof(Country),
        MinSearchLength = 2,
        ColumnSpan = 12)]
    public string PreferredMentorId { get; set; } = string.Empty;

    [WebFormField(
        Section = "Profile settings",
        Label = "Age",
        InputType = WebFormInputType.Number,
        MinValue = 18,
        MaxValue = 120,
        ColumnSpan = 4)]
    public int? Age { get; set; }

    [WebFormField(
        Section = "Profile settings",
        Label = "Favorite color",
        InputType = WebFormInputType.Select,
        Required = true,
        SelectPrompt = "Pick a palette",
        AllowedValues = "amber,teal,coral",
        ColumnSpan = 8)]
    public string FavoriteColor { get; set; } = string.Empty;

    [WebFormField(
        Section = "Content",
        Label = "Short bio",
        InputType = WebFormInputType.TextArea,
        HelpText = "Keep it concise. This is a good place to show text area rendering and max length checks.",
        Placeholder = "A short intro that stays within the limit.",
        MaxLength = 240,
        ColumnSpan = 12)]
    public string Bio { get; set; } = string.Empty;

    [WebFormField(
        Section = "Context",
        Label = "Audience segment",
        ReadOnly = true,
        HelpText = "This value is rendered by the server as read-only metadata that still binds back with the form.",
        ColumnSpan = 6)]
    public string AudienceSegment { get; set; } = string.Empty;

    [WebFormField(
        Hidden = true)]
    public string FormIntent { get; set; } = string.Empty;

    [WebFileField(
        Section = "Upload",
        Label = "Profile image",
        MaxFileSizeBytes = 2_000_000,
        AllowedContentTypes = "image/jpeg,image/png,image/webp",
        AllowedExtensions = ".jpg,.jpeg,.png,.webp",
        MaxImageWidth = 1600,
        MaxImageHeight = 1600,
        MinImageWidth = 120,
        MinImageHeight = 120,
        RequireImage = true,
        ColumnSpan = 12)]
    public IFormFile? ProfileImage { get; set; }
}
