using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules;

/// <summary>
/// Which companies may use a module now, for the code with no signed-in company behind it: public portals and system jobs.
/// Always the database, never the tenant or module caches, so an expiry counts at once. The rule is the single one
/// (<see cref="ModuleSubscription.HasAccessAt"/>), and a company that is not Active has access to nothing.
/// See docs/engineering/tenant-access-enforcement.md.
/// </summary>
public static class ModuleAccessQueries
{
    /// <summary>The companies with access to this module now. An <see cref="IQueryable{T}"/>, to compose inside other queries.</summary>
    public static IQueryable<Guid> TenantsWithAccess(NexoDbContext db, string moduleKey, DateTime nowUtc)
    {
        var key = moduleKey.ToLowerInvariant();
        return LiveTenantIds(db, db.ModuleSubscriptions.Where(s => s.ModuleKey == key), nowUtc);
    }

    /// <summary>The companies with access to at least one module now: the others are read-only as a whole.</summary>
    public static IQueryable<Guid> TenantsWithAnyAccess(NexoDbContext db, DateTime nowUtc)
        => LiveTenantIds(db, db.ModuleSubscriptions, nowUtc);

    private static IQueryable<Guid> LiveTenantIds(NexoDbContext db, IQueryable<ModuleSubscription> rows, DateTime nowUtc)
    {
        var withAccess = rows.Where(ModuleSubscription.HasAccessAt(nowUtc)).Select(s => s.TenantId);
        return db.Tenants.Where(t => t.Status == TenantStatus.Active && withAccess.Contains(t.Id)).Select(t => t.Id);
    }
}
