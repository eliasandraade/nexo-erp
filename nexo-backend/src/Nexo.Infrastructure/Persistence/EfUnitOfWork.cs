using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly NexoDbContext _context;

    public EfUnitOfWork(NexoDbContext context) => _context = context;

    /// <summary>
    /// Begins a transaction, or returns a no-op scope if one is already active on the connection.
    ///
    /// Ambient transaction pattern: the outermost caller owns the real transaction.
    /// Inner callers (e.g. SaleService.ConfirmAsync called from OrderService.PayAsync)
    /// receive a no-op scope — their CommitAsync / RollbackAsync are ignored.
    /// The outer caller's CommitAsync or RollbackAsync (triggered by DisposeAsync if
    /// CommitAsync was never called) controls the final outcome.
    ///
    /// This prevents InvalidOperationException from Npgsql when a second
    /// BeginTransactionAsync is called on a connection that already has an active transaction.
    /// </summary>
    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_context.Database.CurrentTransaction is not null)
            return new NoOpTransactionScope();

        var tx = await _context.Database.BeginTransactionAsync(ct);
        return new EfTransactionScope(tx);
    }

    /// <inheritdoc/>
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteInTransactionAsync(
            operation: async () => await operation(ct),
            verifySucceeded: () => Task.FromResult(false));
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        T result = default!;
        await strategy.ExecuteInTransactionAsync(
            operation: async () => { result = await operation(ct); },
            verifySucceeded: () => Task.FromResult(false));
        return result;
    }
}

internal sealed class EfTransactionScope : ITransactionScope
{
    private readonly IDbContextTransaction _tx;
    private bool _disposed;

    public EfTransactionScope(IDbContextTransaction tx) => _tx = tx;

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _tx.CommitAsync(ct);
        _disposed = true;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _tx.RollbackAsync(ct);
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            // Automatic rollback if CommitAsync was never called
            await _tx.RollbackAsync();
            _disposed = true;
        }
        await _tx.DisposeAsync();
    }
}

/// <summary>
/// No-op transaction scope returned when a real transaction is already active.
/// Used by inner service methods that begin a transaction but are called from
/// within an outer transaction (e.g. SaleService.ConfirmAsync inside OrderService.PayAsync).
/// </summary>
internal sealed class NoOpTransactionScope : ITransactionScope
{
    public Task CommitAsync(CancellationToken ct = default)  => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
