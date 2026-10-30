using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Counts the SELECTs that touch the tax tables, so a test can tell whether a list grows its query count with the number of rows
/// (N+1). Only tax tables are counted: other modules' background work on the same host never disturbs the figure.
/// </summary>
public sealed class TaxQueryCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    private void Seen(DbCommand command)
    {
        var sql = command.CommandText;
        if (sql.Contains("nexo.tax_", StringComparison.Ordinal) && sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _count);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Seen(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Seen(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Seen(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Seen(command);
        return ValueTask.FromResult(result);
    }
}
