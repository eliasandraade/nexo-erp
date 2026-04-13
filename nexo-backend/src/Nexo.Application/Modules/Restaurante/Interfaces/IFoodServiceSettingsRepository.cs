using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IFoodServiceSettingsRepository
{
    Task<FoodServiceSettings?> GetCurrentStoreAsync(CancellationToken ct = default);
    Task AddAsync(FoodServiceSettings settings, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
