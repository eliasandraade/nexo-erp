using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class RecipeCardRepository : IRecipeCardRepository
{
    private readonly NexoDbContext _context;

    public RecipeCardRepository(NexoDbContext context) => _context = context;

    public async Task<RestRecipeCard?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestRecipeCards.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RestRecipeCard?> GetByIdWithIngredientsAsync(Guid id, CancellationToken ct = default)
        => await _context.RestRecipeCards
            .Include(x => x.Ingredients)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<RestRecipeCard?> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
        => await _context.RestRecipeCards
            .FirstOrDefaultAsync(x => x.ProductId == productId, ct);

    public async Task<RestRecipeCard?> GetByProductIdWithIngredientsAsync(Guid productId, CancellationToken ct = default)
        => await _context.RestRecipeCards
            .Include(x => x.Ingredients)
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.IsActive, ct);

    public async Task<IReadOnlyList<RestRecipeCard>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _context.RestRecipeCards.Include(x => x.Ingredients).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(RestRecipeCard card, CancellationToken ct = default)
        => await _context.RestRecipeCards.AddAsync(card, ct);

    public void TrackIngredient(RestRecipeIngredient ingredient)
        => _context.Entry(ingredient).State = Microsoft.EntityFrameworkCore.EntityState.Added;

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
