using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// The moment a session ends is the commit: of a new SecurityStamp, of a new status of the person, of a new status of the company.
/// So this is where open hub connections hear about it, whichever code made the change: role, status and password changes, "sign
/// out of every device", two-step verification, the platform acts, and whatever comes next, with nothing to remember in each of them.
///
/// Never before the commit. A connection closed earlier could reconnect at once with the old token, still valid, and stay. Inside a
/// transaction the signal waits for its commit, and a rollback drops it. It is a hint: whoever receives it asks the database, so
/// one sent for nothing closes nothing. What escapes here (a change made by raw SQL) is the sweep's job.
/// Singleton like the other interceptors: what a unit of work has pending lives with its DbContext.
/// </summary>
public sealed class HubSessionChangeInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private sealed class Pending
    {
        /// <summary>Being saved now: may still fail.</summary>
        public HashSet<Guid> SavingUsers { get; } = [];
        public HashSet<Guid> SavingTenants { get; } = [];
        /// <summary>Saved: waits for the commit when there is a transaction open.</summary>
        public HashSet<Guid> SavedUsers { get; } = [];
        public HashSet<Guid> SavedTenants { get; } = [];
    }

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();
    private readonly HubSessionSignals _signals;
    private readonly ILogger<HubSessionChangeInterceptor> _logger;

    public HubSessionChangeInterceptor(HubSessionSignals signals, ILogger<HubSessionChangeInterceptor> logger)
    {
        _signals = signals;
        _logger = logger;
    }

    // ── SaveChanges ───────────────────────────────────────────────────────────

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (MoveToSaved(eventData.Context) is { } change) PublishWithoutWaiting(change);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (MoveToSaved(eventData.Context) is { } change) await PublishAsync(change);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => DropSaving(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        DropSaving(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => DropSaving(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        DropSaving(eventData.Context);
        return Task.CompletedTask;
    }

    // ── Transaction ───────────────────────────────────────────────────────────

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (TakeSaved(eventData.Context) is { } change) PublishWithoutWaiting(change);
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (TakeSaved(eventData.Context) is { } change) await PublishAsync(change);
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => TakeSaved(eventData.Context);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TakeSaved(eventData.Context);
        return Task.CompletedTask;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case User user when entry.State == EntityState.Deleted
                                    || (entry.State == EntityState.Modified && (entry.Property(nameof(User.SecurityStamp)).IsModified || entry.Property(nameof(User.Status)).IsModified)):
                    _pending.GetOrCreateValue(context).SavingUsers.Add(user.Id);
                    break;
                case Tenant tenant when entry.State == EntityState.Deleted
                                        || (entry.State == EntityState.Modified && entry.Property(nameof(Tenant.Status)).IsModified):
                    _pending.GetOrCreateValue(context).SavingTenants.Add(tenant.Id);
                    break;
            }
        }
    }

    /// <summary>The change to tell now, when no transaction is open: then the save itself was the commit.</summary>
    private HubSessionChange? MoveToSaved(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return null;
        pending.SavedUsers.UnionWith(pending.SavingUsers);
        pending.SavedTenants.UnionWith(pending.SavingTenants);
        pending.SavingUsers.Clear();
        pending.SavingTenants.Clear();
        return context.Database.CurrentTransaction is null ? TakeSaved(context) : null;
    }

    private void DropSaving(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        pending.SavingUsers.Clear();
        pending.SavingTenants.Clear();
    }

    private HubSessionChange? TakeSaved(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return null;
        if (pending.SavedUsers.Count == 0 && pending.SavedTenants.Count == 0) return null;
        var change = new HubSessionChange(pending.SavedUsers.ToArray(), pending.SavedTenants.ToArray());
        pending.SavedUsers.Clear();
        pending.SavedTenants.Clear();
        return change;
    }

    /// <summary>Not the caller's token: the commit is done, and the connections close even if the caller has already left.</summary>
    private Task PublishAsync(HubSessionChange change) => _signals.PublishAsync(change, CancellationToken.None);

    private void PublishWithoutWaiting(HubSessionChange change)
        => _ = Task.Run(async () =>
        {
            try
            {
                await PublishAsync(change);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not signal a session change; the sweep covers it");
            }
        });
}
