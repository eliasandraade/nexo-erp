using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>
/// Repository for the per-store SvcSettings singleton. Tenant + store isolation is enforced by
/// the EF global query filter, so <see cref="GetForCurrentStoreAsync"/> returns the row for the
/// active store (or null when the store has not been configured yet).
/// </summary>
public interface ISvcSettingsRepository
{
    Task<SvcSettings?> GetForCurrentStoreAsync(CancellationToken ct = default);

    /// <summary>
    /// Public-path read for a store resolved by its slug (no auth context). Bypasses the global
    /// query filter and scopes explicitly by tenant + store. Used by the public booking portal.
    /// </summary>
    Task<SvcSettings?> GetByStorePublicAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task AddAsync(SvcSettings entity, CancellationToken ct = default);
    void Update(SvcSettings entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
