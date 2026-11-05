using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Tenant repository — operates on the platform-level tenants table (no tenant isolation filter).
/// Used by: TenantResolutionMiddleware, AuthService, Platform admin services.
/// </summary>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Records the tenant's customer in Stripe. A write of its own: <see cref="GetByIdAsync"/> reads without tracking, so a
    /// change made on what it returns is never saved, and the customer portal would answer 400 to whoever already paid.
    /// </summary>
    Task SetStripeCustomerIdAsync(Guid tenantId, string stripeCustomerId, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveModuleKeysAsync(Guid tenantId, CancellationToken ct = default);
    Task<DateTime?> GetTrialEndsAtAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
