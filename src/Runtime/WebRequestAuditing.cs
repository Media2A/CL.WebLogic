using CL.WebLogic.Configuration;
using CodeLogic.Framework.Libraries;

namespace CL.WebLogic.Runtime;

public interface IWebRequestAuditStore
{
    Task InitializeAsync(LibraryContext context, WebLogicConfig config);
    Task RecordAsync(WebRequestContext request, int statusCode, long durationMs, string source);
}

public sealed class NullWebRequestAuditStore : IWebRequestAuditStore
{
    public static NullWebRequestAuditStore Instance { get; } = new();

    private NullWebRequestAuditStore()
    {
    }

    public Task InitializeAsync(LibraryContext context, WebLogicConfig config) => Task.CompletedTask;

    public Task RecordAsync(WebRequestContext request, int statusCode, long durationMs, string source) =>
        Task.CompletedTask;
}
