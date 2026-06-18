using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcPackageRepository : ISvcPackageRepository
{
    private readonly NexoDbContext _context;
    public SvcPackageRepository(NexoDbContext context) => _context = context;

    public async Task<SvcPackage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcPackages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<SvcPackage?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcPackages.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcPackage>> GetAllAsync(bool? active, CancellationToken ct = default)
    {
        var q = _context.SvcPackages.AsQueryable();
        if (active is { } a) q = q.Where(x => x.IsActive == a);
        return await q.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync(ct);
    }

    public async Task AddAsync(SvcPackage entity, CancellationToken ct = default)
        => await _context.SvcPackages.AddAsync(entity, ct);

    public void Update(SvcPackage entity) => _context.SvcPackages.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
