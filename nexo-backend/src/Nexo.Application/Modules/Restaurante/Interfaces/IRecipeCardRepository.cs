using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IRecipeCardRepository
{
    Task<RestRecipeCard?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RestRecipeCard?> GetByIdWithIngredientsAsync(Guid id, CancellationToken ct = default);
    Task<RestRecipeCard?> GetByProductIdAsync(Guid productId, CancellationToken ct = default);
    Task<RestRecipeCard?> GetByProductIdWithIngredientsAsync(Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<RestRecipeCard>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task AddAsync(RestRecipeCard card, CancellationToken ct = default);
    /// <summary>Explicitly tracks a new ingredient as Added. Required because EF Core assigns
    /// Modified (not Added) to entities with non-sentinel Guids found in readonly backing fields.</summary>
    void TrackIngredient(RestRecipeIngredient ingredient);
    Task SaveChangesAsync(CancellationToken ct = default);
}
