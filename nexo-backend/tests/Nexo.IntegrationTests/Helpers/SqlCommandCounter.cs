using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Counts the SQL commands EF Core runs for the HTTP requests that carry the <see cref="Header"/> header (value = a marker
/// chosen by the test) — so an N+1 test sees only its own request, never background jobs or other tests.
/// </summary>
public sealed class SqlCommandCounter : DbCommandInterceptor
{
    public const string Header = "X-Test-Sql-Count";

    private readonly IHttpContextAccessor _http;
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public SqlCommandCounter(IHttpContextAccessor http) => _http = http;

    public int CountFor(string marker) => _counts.GetValueOrDefault(marker);

    private void Hit()
    {
        var marker = _http.HttpContext?.Request.Headers[Header].ToString();
        if (!string.IsNullOrEmpty(marker)) _counts.AddOrUpdate(marker, 1, (_, c) => c + 1);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Hit();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Hit();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Hit();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Hit();
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Hit();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Hit();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
