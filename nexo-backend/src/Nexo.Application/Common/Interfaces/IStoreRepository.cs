using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Store repository — operates on the stores table.
/// Bypasses Global Query Filters (like ITenantRepository) so it can be used in
/// auth flows (login, switch-store) before the tenant context is resolved.
/// </summary>
public interface IStoreRepository
{
    Task<Store?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Store>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns stores by their IDs (from JWT store[] claims), including ModuleSubscription for moduleKey.
    /// Only returns active stores that belong to the specified tenant (security guard).
    /// </summary>
    Task<IReadOnlyList<Store>> GetByIdsAsync(Guid tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default);

    Task AddAsync(Store store, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
