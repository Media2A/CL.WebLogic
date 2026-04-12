using CL.MySQL2.Models;

namespace MiniBlog.Shared.Entities;

[Table(Name = "miniblog_posts", Collation = "utf8mb4_unicode_ci")]
[CompositeIndex("ix_miniblog_posts_status_published", "status", "published_utc")]
public sealed class MiniBlogPostEntity
{
    [Column(Name = "id", DataType = DataType.VarChar, Size = 64, Primary = true, NotNull = true)]
    public string Id { get; set; } = string.Empty;

    [Column(Name = "slug", DataType = DataType.VarChar, Size = 180, NotNull = true, Unique = true)]
    public string Slug { get; set; } = string.Empty;

    [Column(Name = "title", DataType = DataType.VarChar, Size = 180, NotNull = true)]
    public string Title { get; set; } = string.Empty;

    [Column(Name = "summary", DataType = DataType.VarChar, Size = 400, NotNull = true)]
    public string Summary { get; set; } = string.Empty;

    [Column(Name = "body_html", DataType = DataType.LongText, NotNull = true)]
    public string BodyHtml { get; set; } = string.Empty;

    [Column(Name = "status", DataType = DataType.VarChar, Size = 32, NotNull = true, Index = true)]
    public string Status { get; set; } = string.Empty;

    [Column(Name = "author_user_id", DataType = DataType.VarChar, Size = 64, NotNull = true, Index = true)]
    public string AuthorUserId { get; set; } = string.Empty;

    [Column(Name = "author_display_name", DataType = DataType.VarChar, Size = 256, NotNull = true)]
    public string AuthorDisplayName { get; set; } = string.Empty;

    [Column(Name = "meta_title", DataType = DataType.VarChar, Size = 220, NotNull = true)]
    public string MetaTitle { get; set; } = string.Empty;

    [Column(Name = "meta_description", DataType = DataType.VarChar, Size = 320, NotNull = true)]
    public string MetaDescription { get; set; } = string.Empty;

    [Column(Name = "published_utc", DataType = DataType.DateTime)]
    public DateTime? PublishedUtc { get; set; }

    [Column(Name = "updated_utc", DataType = DataType.DateTime, NotNull = true, Index = true)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
