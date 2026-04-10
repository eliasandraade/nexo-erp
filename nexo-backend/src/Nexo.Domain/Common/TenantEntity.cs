namespace Nexo.Domain.Common;

/// <summary>
/// Base class for all tenant-scoped entities.
/// Inherits BaseEntity and adds TenantId — the foundation of multi-tenant isolation.
///
/// Rules:
///   - TenantId is set once at creation and never changed (private set).
///   - EF Core Global Query Filters use TenantId to scope all queries automatically.
///   - TenantSaveChangesInterceptor validates TenantId on every INSERT.
///   - IgnoreQueryFilters() is FORBIDDEN on any repository of TenantEntity subclasses.
/// </summary>
public abstract class TenantEntity : BaseEntity
{
    public Guid TenantId { get; private set; }

    protected TenantEntity() { } // EF Core constructor

    protected TenantEntity(Guid tenantId) : base()
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));

        TenantId = tenantId;
    }

    /// <summary>
    /// Called exclusively by TenantSaveChangesInterceptor when TenantId was not
    /// set by the constructor (e.g. entity created via parameterless EF constructor
    /// and then TenantId injected by the interceptor).
    /// </summary>
    internal void SetTenantId(Guid tenantId)
    {
        if (TenantId != Guid.Empty)
            throw new InvalidOperationException(
                $"TenantId is already set on {GetType().Name}. It cannot be changed.");

        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));

        TenantId = tenantId;
    }
}
