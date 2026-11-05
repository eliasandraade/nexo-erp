using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Platform-level repository — not tenant-scoped (ModuleSubscription is a platform entity).
/// Queries must filter by TenantId explicitly.
/// </summary>
public interface IModuleSubscriptionRepository
{
    Task<IReadOnlyList<ModuleSubscription>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>Reads the row and locks it until the current transaction ends.</summary>
    Task<ModuleSubscription?> GetByStripeSubscriptionIdForUpdateAsync(string stripeSubscriptionId, CancellationToken ct = default);
    Task AddAsync(ModuleSubscription subscription, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
