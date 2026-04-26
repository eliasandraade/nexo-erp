using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IFoodServiceSettingsRepository
{
    Task<FoodServiceSettings?> GetCurrentStoreAsync(CancellationToken ct = default);

    /// <summary>Bypasses query filters — used by public portal (no auth context).</summary>
    Task<FoodServiceSettings?> GetByStoreIdAsync(Guid storeId, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(FoodServiceSettings settings, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
