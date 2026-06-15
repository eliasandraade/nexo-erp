using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Tenant repository — operates on the platform-level tenants table (no tenant isolation filter).
/// Used by: TenantResolutionMiddleware, AuthService, Platform admin services.
/// </summary>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveModuleKeysAsync(Guid tenantId, CancellationToken ct = default);
    Task<DateTime?> GetTrialEndsAtAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
