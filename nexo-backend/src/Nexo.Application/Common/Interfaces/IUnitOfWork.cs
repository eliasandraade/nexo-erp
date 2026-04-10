namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Abstracts database transaction management.
/// Allows services to wrap multiple repository operations in a single atomic unit.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Begins a new database transaction and returns a scope to commit or roll back.</summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct = default);
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
