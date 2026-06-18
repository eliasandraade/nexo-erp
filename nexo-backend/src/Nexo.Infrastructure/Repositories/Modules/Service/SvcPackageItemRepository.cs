using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcPackageItemRepository : ISvcPackageItemRepository
{
    private readonly NexoDbContext _context;
    public SvcPackageItemRepository(NexoDbContext context) => _context = context;

    public async Task<SvcPackageItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcPackageItems.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcPackageItem>> GetByPackageAsync(Guid packageId, CancellationToken ct = default)
        => await _context.SvcPackageItems.Where(x => x.PackageId == packageId)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task<bool> ExistsForCatalogAsync(Guid packageId, Guid catalogItemId, CancellationToken ct = default)
        => await _context.SvcPackageItems.AnyAsync(x => x.PackageId == packageId && x.CatalogItemId == catalogItemId, ct);

    public async Task AddAsync(SvcPackageItem entity, CancellationToken ct = default)
        => await _context.SvcPackageItems.AddAsync(entity, ct);

    public void Update(SvcPackageItem entity) => _context.SvcPackageItems.Update(entity);
    public void Remove(SvcPackageItem entity) => _context.SvcPackageItems.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
