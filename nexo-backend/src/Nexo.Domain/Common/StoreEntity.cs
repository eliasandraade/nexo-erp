namespace Nexo.Domain.Common;

/// <summary>
/// Base class for all store-scoped entities.
/// Extends TenantEntity (adds StoreId on top of TenantId) — the foundation of sub-tenant isolation.
///
/// Rules:
///   - StoreId is set once at creation (by TenantSaveChangesInterceptor) and never changed.
///   - EF Core Global Query Filters filter on BOTH TenantId AND StoreId.
///   - TenantSaveChangesInterceptor auto-injects StoreId from ICurrentStore on every INSERT.
///   - Entities that belong to a specific store (stock, sales, cash) extend StoreEntity.
///   - Entities shared across stores within a tenant (customers, categories) remain TenantEntity.
/// </summary>
public abstract class StoreEntity : TenantEntity
{
    public Guid StoreId { get; private set; }

    protected StoreEntity() { } // EF Core constructor

    protected StoreEntity(Guid tenantId) : base(tenantId) { }

    /// <summary>
    /// Called exclusively by TenantSaveChangesInterceptor when StoreId was not
    /// set by the constructor (injected automatically from ICurrentStore.Id).
    /// </summary>
    internal void SetStoreId(Guid storeId)
    {
        if (StoreId != Guid.Empty)
            throw new InvalidOperationException(
                $"StoreId is already set on {GetType().Name}. It cannot be changed.");

        if (storeId == Guid.Empty)
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));

        StoreId = storeId;
    }
}
