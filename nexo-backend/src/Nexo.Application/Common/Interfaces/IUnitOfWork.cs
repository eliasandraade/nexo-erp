namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Abstracts database transaction management.
/// Allows services to wrap multiple repository operations in a single atomic unit.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Begins a new database transaction and returns a scope to commit or roll back.</summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside a transaction, wrapped in the EF execution
    /// strategy so that transient failures are retried correctly even when
    /// EnableRetryOnFailure is configured on the DbContext.
    ///
    /// Use this instead of BeginTransactionAsync when the call site itself begins
    /// the transaction (i.e. is not nested inside an outer transaction).
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);

    /// <inheritdoc cref="ExecuteInTransactionAsync(Func{CancellationToken,Task},CancellationToken)"/>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}

/// <summary>
/// Represents an active database transaction.
/// Dispose (via await using) triggers automatic rollback if CommitAsync was not called.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
