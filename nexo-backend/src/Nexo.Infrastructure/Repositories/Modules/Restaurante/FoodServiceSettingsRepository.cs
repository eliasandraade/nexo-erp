using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class FoodServiceSettingsRepository : IFoodServiceSettingsRepository
{
    private readonly NexoDbContext _context;
    public FoodServiceSettingsRepository(NexoDbContext context) => _context = context;

    // Global query filter on FoodServiceSettings (StoreEntity) auto-scopes to current store
    public async Task<FoodServiceSettings?> GetCurrentStoreAsync(CancellationToken ct = default)
        => await _context.FoodServiceSettings.FirstOrDefaultAsync(ct);

    public async Task AddAsync(FoodServiceSettings settings, CancellationToken ct = default)
        => await _context.FoodServiceSettings.AddAsync(settings, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
