using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcCatalogItemRepository : ISvcCatalogItemRepository
{
    private readonly NexoDbContext _context;

    public SvcCatalogItemRepository(NexoDbContext context) => _context = context;

    public async Task<SvcCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcCatalogItems.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcCatalogItem>> GetAllAsync(
        bool onlyActive = false, CancellationToken ct = default)
    {
        var query = _context.SvcCatalogItems.AsQueryable();
        if (onlyActive)
            query = query.Where(x => x.IsActive);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SvcCatalogItem entity, CancellationToken ct = default)
        => await _context.SvcCatalogItems.AddAsync(entity, ct);

    public void Update(SvcCatalogItem entity)
        => _context.SvcCatalogItems.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
