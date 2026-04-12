using CL.MySQL2;
using CL.MySQL2.Models;
using CL.WebLogic.Configuration;
using CL.WebLogic.Runtime;
using CodeLogic;
using CodeLogic.Framework.Libraries;

namespace MiniBlog.Shared.Infrastructure;

public sealed class MiniBlogMySqlRequestAuditStore : IWebRequestAuditStore
{
    private readonly string _connectionId;
    private readonly bool _enabled;
    private readonly bool _syncTablesOnStart;
    private LibraryContext? _context;
    private MySQL2Library? _mysql;

    public MiniBlogMySqlRequestAuditStore(string connectionId, bool enabled, bool syncTablesOnStart = true)
    {
        _connectionId = connectionId;
        _enabled = enabled;
        _syncTablesOnStart = syncTablesOnStart;
    }

    public async Task InitializeAsync(LibraryContext context, WebLogicConfig config)
    {
        _context = context;
        _mysql = Libraries.Get<MySQL2Library>();

        if (_mysql is null || !_enabled || !_syncTablesOnStart)
            return;

        try
        {
            await _mysql.SyncTableAsync<MiniBlogRequestLogRecord>(connectionId: _connectionId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Logger.Warning($"Unable to sync MiniBlogRequestLogRecord table: {ex.Message}");
        }
    }

    public async Task RecordAsync(WebRequestContext request, int statusCode, long durationMs, string source)
    {
        if (_context is null || _mysql is null || !_enabled)
            return;

        try
        {
            var repository = _mysql.GetRepository<MiniBlogRequestLogRecord>(_connectionId);
            await repository.InsertAsync(new MiniBlogRequestLogRecord
            {
                Method = request.Method,
                Path = request.Path,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                StatusCode = statusCode,
                DurationMs = durationMs,
                Source = source,
                CreatedUtc = DateTime.UtcNow
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Logger.Warning($"Failed to persist MiniBlog request log: {ex.Message}");
        }
    }
}

[Table(Name = "miniblog_request_log")]
public sealed class MiniBlogRequestLogRecord
{
    [Column(DataType = DataType.BigInt, Primary = true, AutoIncrement = true, NotNull = true)]
    public long Id { get; set; }

    [Column(DataType = DataType.VarChar, Size = 16, NotNull = true)]
    public string Method { get; set; } = string.Empty;

    [Column(DataType = DataType.VarChar, Size = 512, NotNull = true, Index = true)]
    public string Path { get; set; } = string.Empty;

    [Column(DataType = DataType.VarChar, Size = 64, NotNull = true, Index = true)]
    public string ClientIp { get; set; } = string.Empty;

    [Column(DataType = DataType.VarChar, Size = 256)]
    public string UserAgent { get; set; } = string.Empty;

    [Column(DataType = DataType.Int, NotNull = true)]
    public int StatusCode { get; set; }

    [Column(DataType = DataType.BigInt, NotNull = true)]
    public long DurationMs { get; set; }

    [Column(DataType = DataType.VarChar, Size = 32, NotNull = true)]
    public string Source { get; set; } = string.Empty;

    [Column(DataType = DataType.DateTime, NotNull = true)]
    public DateTime CreatedUtc { get; set; }
}
