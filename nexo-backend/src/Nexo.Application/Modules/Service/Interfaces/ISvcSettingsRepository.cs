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
    Task AddAsync(SvcSettings entity, CancellationToken ct = default);
    void Update(SvcSettings entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
