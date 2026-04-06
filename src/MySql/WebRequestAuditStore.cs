using CL.MySQL2;
using CL.MySQL2.Models;
using CL.WebLogic.Configuration;
using CL.WebLogic.Runtime;
using CodeLogic;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic.MySql;

public interface IWebRequestAuditStore
{
    Task InitializeAsync(LibraryContext context, WebLogicConfig config);
    Task RecordAsync(WebRequestContext request, int statusCode, long durationMs, string source);
}

public sealed class WebRequestAuditStore : IWebRequestAuditStore
{
    private LibraryContext? _context;
    private WebLogicConfig? _config;
    private MySQL2Library? _mysql;

    public WebRequestAuditStore(LibraryContext context, WebLogicConfig config)
    {
        _context = context;
        _config = config;
    }

    public async Task InitializeAsync(LibraryContext context, WebLogicConfig config)
    {
        _context = context;
        _config = config;
        _mysql = Libraries.Get<MySQL2Library>();

        if (_mysql is null || !config.MySql.EnableRequestLogging || !config.MySql.SyncTablesOnStart)
            return;

        try
        {
            await _mysql.SyncTableAsync<WebRequestLogRecord>(connectionId: config.MySql.ConnectionId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Logger.Warning($"Unable to sync WebRequestLogRecord table: {ex.Message}");
        }
    }

    public async Task RecordAsync(WebRequestContext request, int statusCode, long durationMs, string source)
    {
        if (_context is null || _config is null || _mysql is null || !_config.MySql.EnableRequestLogging)
            return;

        try
        {
            var repository = _mysql.GetRepository<WebRequestLogRecord>(_config.MySql.ConnectionId);
            await repository.InsertAsync(new WebRequestLogRecord
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
            _context.Logger.Warning($"Failed to persist request log: {ex.Message}");
        }
    }
}

[Table(Name = "web_request_log")]
public sealed class WebRequestLogRecord
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
