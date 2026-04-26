using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class ModifierGroupRepository : IModifierGroupRepository
{
    private readonly NexoDbContext _context;
    public ModifierGroupRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
        => await _context.ProductModifierGroups
            .Include(g => g.Modifiers)
            .Where(g => g.ProductId == productId && g.IsActive)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, Guid tenantId, CancellationToken ct = default)
        => await _context.ProductModifierGroups
            .IgnoreQueryFilters()
            .Include(g => g.Modifiers)
            .Where(g => g.ProductId == productId && g.TenantId == tenantId && g.IsActive)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

    public async Task<ProductModifierGroup?> GetByIdWithModifiersAsync(Guid id, CancellationToken ct = default)
        => await _context.ProductModifierGroups
            .Include(g => g.Modifiers)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<ProductModifier?> GetModifierByIdAsync(Guid modifierId, CancellationToken ct = default)
        => await _context.ProductModifiers.FirstOrDefaultAsync(m => m.Id == modifierId, ct);

    public async Task<ProductModifier?> GetActiveModifierAsync(Guid modifierId, Guid tenantId, CancellationToken ct = default)
        => await _context.ProductModifiers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == modifierId
                                   && m.TenantId == tenantId
                                   && m.IsActive, ct);

    public async Task AddGroupAsync(ProductModifierGroup group, CancellationToken ct = default)
        => await _context.ProductModifierGroups.AddAsync(group, ct);

    public void TrackModifier(ProductModifier modifier)
        => _context.Entry(modifier).State = EntityState.Added;

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
