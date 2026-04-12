using CL.MySQL2;
using CL.MySQL2.Models;
using CodeLogic.Framework.Libraries;
using CodeLogic;
using MiniBlog.Shared.Entities;
using MiniBlog.Shared.Models;

namespace MiniBlog.Shared.Services;

public sealed class MiniBlogDataService
{
    private readonly string _connectionId;

    public MiniBlogDataService(string connectionId = "Default")
    {
        _connectionId = string.IsNullOrWhiteSpace(connectionId) ? "Default" : connectionId;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var mysql = GetMySql();
        _ = cancellationToken;
        await mysql.SyncTableAsync<MiniBlogPostEntity>(createBackup: false, connectionId: _connectionId).ConfigureAwait(false);
    }

    public async Task SeedPostsAsync(IEnumerable<MiniBlogSeedPost> posts, CancellationToken cancellationToken = default)
    {
        var repository = GetMySql().GetRepository<MiniBlogPostEntity>(_connectionId);

        foreach (var seed in posts)
        {
            var existingResult = await repository.GetByIdAsync(seed.Id, cancellationToken).ConfigureAwait(false);
            var entity = existingResult.IsSuccess && existingResult.Value is not null
                ? existingResult.Value
                : new MiniBlogPostEntity
                {
                    Id = seed.Id
                };

            entity.Slug = seed.Slug;
            entity.Title = seed.Title;
            entity.Summary = seed.Summary;
            entity.BodyHtml = seed.BodyHtml;
            entity.Status = seed.Status;
            entity.AuthorUserId = seed.AuthorUserId;
            entity.AuthorDisplayName = seed.AuthorDisplayName;
            entity.MetaTitle = seed.MetaTitle;
            entity.MetaDescription = seed.MetaDescription;
            entity.PublishedUtc = seed.PublishedUtc?.UtcDateTime;
            entity.UpdatedUtc = DateTime.UtcNow;

            if (existingResult.IsSuccess && existingResult.Value is not null)
                await repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
            else
                await repository.InsertAsync(entity, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<MiniBlogPostSummary>> GetPublishedPostsAsync(int take = 12, CancellationToken cancellationToken = default)
    {
        var result = await GetMySql()
            .Query<MiniBlogPostEntity>(_connectionId)
            .Where(post => post.Status == "published")
            .OrderByDescending(post => post.PublishedUtc)
            .Limit(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess && result.Value is not null
            ? result.Value.Select(MapSummary).ToArray()
            : [];
    }

    public async Task<PagedResult<MiniBlogPostSummary>> GetPublishedPostsPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 12 : pageSize;

        var result = await GetMySql()
            .Query<MiniBlogPostEntity>(_connectionId)
            .Where(post => post.Status == "published")
            .OrderByDescending(post => post.PublishedUtc)
            .ToPagedListAsync(safePage, safePageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Value is not null)
        {
            return new PagedResult<MiniBlogPostSummary>
            {
                Items = result.Value.Items.Select(MapSummary).ToList(),
                PageNumber = result.Value.PageNumber,
                PageSize = result.Value.PageSize,
                TotalItems = result.Value.TotalItems
            };
        }

        return new PagedResult<MiniBlogPostSummary>
        {
            Items = [],
            PageNumber = safePage,
            PageSize = safePageSize,
            TotalItems = 0
        };
    }

    public async Task<IReadOnlyList<MiniBlogPostSummary>> GetAllPostsAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetMySql()
            .Query<MiniBlogPostEntity>(_connectionId)
            .OrderByDescending(post => post.UpdatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess && result.Value is not null
            ? result.Value.Select(MapSummary).ToArray()
            : [];
    }

    public async Task<MiniBlogPostDetail?> GetPublishedPostBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await GetPostBySlugAsync(slug, publishedOnly: true, cancellationToken).ConfigureAwait(false);

    public async Task<MiniBlogPostDetail?> GetPostByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await GetMySql().GetRepository<MiniBlogPostEntity>(_connectionId).GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null ? MapDetail(result.Value) : null;
    }

    public async Task SavePostAsync(MiniBlogPostUpsertCommand command, string authorUserId, string authorDisplayName, CancellationToken cancellationToken = default)
    {
        var repository = GetMySql().GetRepository<MiniBlogPostEntity>(_connectionId);
        var postId = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString() : command.Id;
        var existingResult = await repository.GetByIdAsync(postId, cancellationToken).ConfigureAwait(false);
        var existing = existingResult.IsSuccess ? existingResult.Value : null;

        var entity = existing ?? new MiniBlogPostEntity
        {
            Id = postId
        };

        entity.Slug = command.Slug;
        entity.Title = command.Title;
        entity.Summary = command.Summary;
        entity.BodyHtml = command.BodyHtml;
        entity.Status = command.Status;
        entity.AuthorUserId = authorUserId;
        entity.AuthorDisplayName = authorDisplayName;
        entity.MetaTitle = command.MetaTitle;
        entity.MetaDescription = command.MetaDescription;
        entity.PublishedUtc = ResolvePublishedUtc(command.Status, existing?.PublishedUtc);
        entity.UpdatedUtc = DateTime.UtcNow;

        if (existing is null)
            await repository.InsertAsync(entity, cancellationToken).ConfigureAwait(false);
        else
            await repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MiniBlogPostDetail?> GetPostBySlugAsync(string slug, bool publishedOnly, CancellationToken cancellationToken)
    {
        var query = GetMySql()
            .Query<MiniBlogPostEntity>(_connectionId)
            .Where(post => post.Slug == slug);

        if (publishedOnly)
            query = query.Where(post => post.Status == "published");

        var result = await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is not null ? MapDetail(result.Value) : null;
    }

    private static DateTime? ResolvePublishedUtc(string status, DateTime? currentPublishedUtc)
    {
        if (string.Equals(status, "published", StringComparison.OrdinalIgnoreCase))
            return currentPublishedUtc ?? DateTime.UtcNow;

        return null;
    }

    private static MiniBlogPostSummary MapSummary(MiniBlogPostEntity entity) => new()
    {
        Id = entity.Id,
        Slug = entity.Slug,
        Title = entity.Title,
        Summary = entity.Summary,
        Status = entity.Status,
        AuthorUserId = entity.AuthorUserId,
        AuthorDisplayName = entity.AuthorDisplayName,
        MetaTitle = entity.MetaTitle,
        MetaDescription = entity.MetaDescription,
        PublishedUtc = entity.PublishedUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(entity.PublishedUtc.Value, DateTimeKind.Utc)),
        UpdatedUtc = new DateTimeOffset(DateTime.SpecifyKind(entity.UpdatedUtc, DateTimeKind.Utc))
    };

    private static MiniBlogPostDetail MapDetail(MiniBlogPostEntity entity) => new()
    {
        Id = entity.Id,
        Slug = entity.Slug,
        Title = entity.Title,
        Summary = entity.Summary,
        BodyHtml = entity.BodyHtml,
        Status = entity.Status,
        AuthorUserId = entity.AuthorUserId,
        AuthorDisplayName = entity.AuthorDisplayName,
        MetaTitle = entity.MetaTitle,
        MetaDescription = entity.MetaDescription,
        PublishedUtc = entity.PublishedUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(entity.PublishedUtc.Value, DateTimeKind.Utc)),
        UpdatedUtc = new DateTimeOffset(DateTime.SpecifyKind(entity.UpdatedUtc, DateTimeKind.Utc))
    };

    private static MySQL2Library GetMySql() =>
        Libraries.Get<MySQL2Library>()
        ?? throw new InvalidOperationException("CL.MySQL2 must be loaded for MiniBlog.");
}
