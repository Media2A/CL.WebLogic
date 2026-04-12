using CL.WebLogic.Forms;

namespace MiniBlog.Shared.Models;

public class MiniBlogPostSummary
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public DateTimeOffset? PublishedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MiniBlogPostDetail : MiniBlogPostSummary
{
    public string BodyHtml { get; set; } = string.Empty;
}

[WebForm(Id = "miniblog.editor.post", Name = "MiniBlog Post Editor", Description = "Editor form for public MiniBlog posts.")]
public sealed class MiniBlogPostEditorForm
{
    [WebFormField(Hidden = true)]
    public string PostId { get; set; } = string.Empty;

    [WebFormField(Label = "Title", Required = true, MinLength = 5, MaxLength = 140, Section = "Content", Placeholder = "Write a magnetic headline")]
    public string Title { get; set; } = string.Empty;

    [WebFormField(Label = "Slug", Required = true, MinLength = 5, MaxLength = 160, Placeholder = "headline-in-url-form")]
    public string Slug { get; set; } = string.Empty;

    [WebFormField(Label = "Summary", Required = true, InputType = WebFormInputType.TextArea, MinLength = 20, MaxLength = 280, Placeholder = "Short summary used in cards and meta tags")]
    public string Summary { get; set; } = string.Empty;

    [WebFormField(Label = "Body HTML", Required = true, InputType = WebFormInputType.TextArea, Section = "Body", MinLength = 40, Placeholder = "<p>Tell the story...</p>")]
    public string BodyHtml { get; set; } = string.Empty;

    [WebFormField(Label = "Status", Required = true, InputType = WebFormInputType.Select, SelectPrompt = "Choose publish state", Section = "Publishing", AllowedValues = "draft,published")]
    public string Status { get; set; } = "draft";

    [WebFormField(Label = "Meta title", Required = true, MinLength = 5, MaxLength = 160, Section = "Metadata", Placeholder = "Search and social title")]
    public string MetaTitle { get; set; } = string.Empty;

    [WebFormField(Label = "Meta description", Required = true, InputType = WebFormInputType.TextArea, MinLength = 20, MaxLength = 220, Placeholder = "Short description for search and sharing")]
    public string MetaDescription { get; set; } = string.Empty;
}

public sealed class MiniBlogPostUpsertCommand
{
    [WebFormMapFrom(nameof(MiniBlogPostEditorForm.PostId))]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
}
