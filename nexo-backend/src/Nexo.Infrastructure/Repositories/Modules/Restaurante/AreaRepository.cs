using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class AreaRepository : IAreaRepository
{
    private readonly NexoDbContext _context;

    public AreaRepository(NexoDbContext context) => _context = context;

    public async Task<RestArea?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.RestAreas
            .Include(x => x.Tables)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<RestArea>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _context.RestAreas.AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task AddAsync(RestArea area, CancellationToken ct = default)
        => await _context.RestAreas.AddAsync(area, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
