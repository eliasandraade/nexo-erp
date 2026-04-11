using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.MultiTenancy;

/// <summary>
/// EF Core SaveChanges interceptor — enforces tenant and store isolation on every INSERT/UPDATE.
///
/// Registered as a SINGLETON (EF Core requirement for interceptors).
/// Does NOT inject ICurrentTenant/ICurrentStore in its constructor — that would create a captive
/// dependency (singleton capturing a scoped service). Instead, the current tenant/store is
/// read from the NexoDbContext instance passed in each SaveChanges event.
///
/// On every SaveChanges:
///   1. For Added TenantEntity: auto-sets TenantId if still Guid.Empty.
///   2. For Added StoreEntity: additionally auto-sets StoreId if still Guid.Empty.
///   3. Validates no TenantEntity has a TenantId that differs from the current tenant.
///   4. Validates no existing entity attempts to change its TenantId.
///   5. Throws TenantIsolationViolationException on any violation.
///
/// Skips all validation when IsResolved = false (DataSeeder, migrations, unauthenticated).
/// </summary>
public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<TenantSaveChangesInterceptor> _logger;

    // ICurrentTenant/ICurrentStore are intentionally NOT injected here.
    // They are obtained per-call from the NexoDbContext passed in the event data.
    public TenantSaveChangesInterceptor(ILogger<TenantSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateTenantEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        ValidateTenantEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void ValidateTenantEntities(DbContext? context)
    {
        // Access the scoped ICurrentTenant/ICurrentStore from the DbContext itself —
        // this is the correct per-request instance, not a captured singleton.
        if (context is not NexoDbContext nexoCtx) return;

        var currentTenant = nexoCtx.CurrentTenant;
        var currentStore  = nexoCtx.CurrentStore;

        // Skip validation when there's no resolved tenant (DataSeeder, migrations, anon requests)
        if (!currentTenant.IsResolved) return;

        // Enforce immutability of audit-sensitive records
        EnforceImmutableEntities(nexoCtx);

        foreach (var entry in nexoCtx.ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                // Auto-inject TenantId if the entity was created without one
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.SetTenantId(currentTenant.Id);
                }

                // Block cross-tenant INSERTs
                if (entry.Entity.TenantId != currentTenant.Id)
                {
                    var entityType = entry.Entity.GetType().Name;
                    var entityId   = entry.Entity.Id;

                    _logger.LogCritical(
                        "TENANT ISOLATION VIOLATION: attempted INSERT of {EntityType} (Id: {EntityId}) " +
                        "with TenantId {AttemptedTenantId} while current tenant is {CurrentTenantId}.",
                        entityType, entityId,
                        entry.Entity.TenantId, currentTenant.Id);

                    throw new TenantIsolationViolationException(
                        entityType,
                        entry.Entity.TenantId,
                        currentTenant.Id);
                }

                // Auto-inject StoreId for StoreEntity subclasses
                if (entry.Entity is StoreEntity storeEntity && currentStore.IsResolved)
                {
                    if (storeEntity.StoreId == Guid.Empty)
                        storeEntity.SetStoreId(currentStore.Id);
                }
            }

            if (entry.State == EntityState.Modified)
            {
                // Block attempts to move an entity to a different tenant.
                // Only throw when the value actually changes — EF Core relationship fixup
                // can mark TenantId as IsModified=true even when orig == current (a no-op),
                // so we guard on the actual value difference to avoid false positives.
                var tenantIdEntry = entry.Property(nameof(TenantEntity.TenantId));
                if (tenantIdEntry.IsModified &&
                    (Guid?)tenantIdEntry.CurrentValue != (Guid?)tenantIdEntry.OriginalValue)
                {
                    _logger.LogCritical(
                        "TENANT ISOLATION VIOLATION: attempted UPDATE of TenantId on {EntityType} (Id: {EntityId}). " +
                        "OriginalTenantId: {OriginalTenantId}, AttemptedTenantId: {AttemptedTenantId}.",
                        entry.Entity.GetType().Name, entry.Entity.Id,
                        tenantIdEntry.OriginalValue, tenantIdEntry.CurrentValue);

                    throw new TenantIsolationViolationException(
                        entry.Entity.GetType().Name,
                        (Guid)tenantIdEntry.CurrentValue!,
                        currentTenant.Id);
                }
            }
        }
    }

    /// <summary>
    /// Guarantees StockMovement and CashMovement are append-only.
    /// Any attempt to UPDATE or DELETE these records throws immediately,
    /// regardless of whether the caller holds a valid tenant context.
    /// Corrections must always be made via compensating records.
    /// </summary>
    private static void EnforceImmutableEntities(NexoDbContext nexoCtx)
    {
        foreach (var entry in nexoCtx.ChangeTracker.Entries())
        {
            var isImmutable = entry.Entity is StockMovement || entry.Entity is CashMovement;
            if (!isImmutable) continue;

            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                var entityType = entry.Entity.GetType().Name;
                var entityId   = (entry.Entity as TenantEntity)?.Id;
                throw new InvalidOperationException(
                    $"{entityType} (Id: {entityId}) is immutable. " +
                    $"Corrections must be made via compensating records, never by editing or deleting.");
            }
        }
    }
}
