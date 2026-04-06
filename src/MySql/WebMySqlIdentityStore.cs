using CL.MySQL2;
using CL.MySQL2.Models;
using CL.WebLogic.Configuration;
using CL.WebLogic.Security;
using CodeLogic;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic.MySql;

public sealed class WebMySqlIdentityStore : IWebIdentityStore
{
    private readonly LibraryContext _context;
    private readonly WebLogicConfig _config;
    private MySQL2Library? _mysql;

    public WebMySqlIdentityStore(LibraryContext context, WebLogicConfig config)
    {
        _context = context;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        _mysql = Libraries.Get<MySQL2Library>();
        if (_mysql is null || !_config.Auth.MySql.Enabled)
            return;

        if (_config.Auth.MySql.SyncTablesOnStart)
        {
            await _mysql.SyncTableAsync<WebAuthUserRecord>(connectionId: _config.Auth.MySql.ConnectionId).ConfigureAwait(false);
            await _mysql.SyncTableAsync<WebAuthAccessGroupRecord>(connectionId: _config.Auth.MySql.ConnectionId).ConfigureAwait(false);
            await _mysql.SyncTableAsync<WebAuthUserAccessGroupRecord>(connectionId: _config.Auth.MySql.ConnectionId).ConfigureAwait(false);
        }

        if (_config.Auth.MySql.SeedDemoRecords)
            await SeedDemoRecordsAsync().ConfigureAwait(false);
    }

    public async Task<WebIdentityProfile?> GetIdentityAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_mysql is null || !_config.Auth.MySql.Enabled || string.IsNullOrWhiteSpace(userId))
            return null;

        var users = _mysql.GetRepository<WebAuthUserRecord>(_config.Auth.MySql.ConnectionId);
        var userResult = await users.GetByColumnAsync("user_id", userId, cancellationToken).ConfigureAwait(false);
        if (userResult.IsFailure || userResult.Value is null)
            return null;

        var user = userResult.Value.FirstOrDefault(static item => item.IsActive);
        if (user is null)
            return null;

        var memberships = _mysql.GetRepository<WebAuthUserAccessGroupRecord>(_config.Auth.MySql.ConnectionId);
        var membershipResult = await memberships.GetByColumnAsync("user_id", user.Id, cancellationToken).ConfigureAwait(false);
        var groupIds = membershipResult.IsSuccess && membershipResult.Value is not null
            ? membershipResult.Value.Select(static item => item.AccessGroupId).Distinct().ToArray()
            : [];

        var groups = new List<string>();
        if (groupIds.Length > 0)
        {
            var groupRepo = _mysql.GetRepository<WebAuthAccessGroupRecord>(_config.Auth.MySql.ConnectionId);
            foreach (var groupId in groupIds)
            {
                var groupResult = await groupRepo.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);
                if (groupResult.IsSuccess && groupResult.Value is not null && groupResult.Value.IsActive)
                    groups.Add(groupResult.Value.GroupKey);
            }
        }

        return new WebIdentityProfile
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            AccessGroups = groups
        };
    }

    private async Task SeedDemoRecordsAsync()
    {
        if (_mysql is null)
            return;

        var groupRepo = _mysql.GetRepository<WebAuthAccessGroupRecord>(_config.Auth.MySql.ConnectionId);
        var adminGroupResult = await groupRepo.GetByColumnAsync("group_key", "admin").ConfigureAwait(false);
        var adminGroup = adminGroupResult.IsSuccess && adminGroupResult.Value is not null
            ? adminGroupResult.Value.FirstOrDefault()
            : null;

        if (adminGroup is null)
        {
            var inserted = await groupRepo.InsertAsync(new WebAuthAccessGroupRecord
            {
                GroupKey = "admin",
                DisplayName = "Administrators",
                IsActive = true
            }).ConfigureAwait(false);

            adminGroup = inserted.IsSuccess ? inserted.Value : null;
        }

        if (adminGroup is null)
            return;

        var userRepo = _mysql.GetRepository<WebAuthUserRecord>(_config.Auth.MySql.ConnectionId);
        var adminUserResult = await userRepo.GetByColumnAsync("user_id", _config.Auth.MySql.DemoAdminUserId).ConfigureAwait(false);
        var adminUser = adminUserResult.IsSuccess && adminUserResult.Value is not null
            ? adminUserResult.Value.FirstOrDefault()
            : null;

        if (adminUser is null)
        {
            var inserted = await userRepo.InsertAsync(new WebAuthUserRecord
            {
                UserId = _config.Auth.MySql.DemoAdminUserId,
                DisplayName = "Demo Admin",
                Email = "demo-admin@example.local",
                IsActive = true
            }).ConfigureAwait(false);

            adminUser = inserted.IsSuccess ? inserted.Value : null;
        }

        if (adminUser is null)
            return;

        var membershipRepo = _mysql.GetRepository<WebAuthUserAccessGroupRecord>(_config.Auth.MySql.ConnectionId);
        var existingMemberships = await membershipRepo.GetByColumnAsync("user_id", adminUser.Id).ConfigureAwait(false);
        var alreadyLinked = existingMemberships.IsSuccess
            && existingMemberships.Value is not null
            && existingMemberships.Value.Any(item => item.AccessGroupId == adminGroup.Id);

        if (!alreadyLinked)
        {
            await membershipRepo.InsertAsync(new WebAuthUserAccessGroupRecord
            {
                UserId = adminUser.Id,
                AccessGroupId = adminGroup.Id
            }).ConfigureAwait(false);
        }
    }
}

[Table(Name = "weblogic_auth_users")]
public sealed class WebAuthUserRecord
{
    [Column(DataType = DataType.BigInt, Primary = true, AutoIncrement = true, NotNull = true)]
    public long Id { get; set; }

    [Column(Name = "user_id", DataType = DataType.VarChar, Size = 128, NotNull = true, Unique = true, Index = true)]
    public string UserId { get; set; } = string.Empty;

    [Column(Name = "display_name", DataType = DataType.VarChar, Size = 256)]
    public string DisplayName { get; set; } = string.Empty;

    [Column(Name = "email", DataType = DataType.VarChar, Size = 256)]
    public string Email { get; set; } = string.Empty;

    [Column(Name = "is_active", DataType = DataType.TinyInt, NotNull = true)]
    public bool IsActive { get; set; } = true;
}

[Table(Name = "weblogic_auth_access_groups")]
public sealed class WebAuthAccessGroupRecord
{
    [Column(DataType = DataType.BigInt, Primary = true, AutoIncrement = true, NotNull = true)]
    public long Id { get; set; }

    [Column(Name = "group_key", DataType = DataType.VarChar, Size = 128, NotNull = true, Unique = true, Index = true)]
    public string GroupKey { get; set; } = string.Empty;

    [Column(Name = "display_name", DataType = DataType.VarChar, Size = 256)]
    public string DisplayName { get; set; } = string.Empty;

    [Column(Name = "is_active", DataType = DataType.TinyInt, NotNull = true)]
    public bool IsActive { get; set; } = true;
}

[Table(Name = "weblogic_auth_user_access_groups")]
public sealed class WebAuthUserAccessGroupRecord
{
    [Column(DataType = DataType.BigInt, Primary = true, AutoIncrement = true, NotNull = true)]
    public long Id { get; set; }

    [Column(Name = "user_id", DataType = DataType.BigInt, NotNull = true, Index = true)]
    public long UserId { get; set; }

    [Column(Name = "access_group_id", DataType = DataType.BigInt, NotNull = true, Index = true)]
    public long AccessGroupId { get; set; }
}
