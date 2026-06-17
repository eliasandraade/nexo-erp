using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcProfessionalRepository : ISvcProfessionalRepository
{
    private readonly NexoDbContext _context;

    public SvcProfessionalRepository(NexoDbContext context) => _context = context;

    public async Task<SvcProfessional?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcProfessionals.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcProfessional>> GetAllAsync(
        bool onlyActive = false, CancellationToken ct = default)
    {
        var query = _context.SvcProfessionals.AsQueryable();
        if (onlyActive)
            query = query.Where(x => x.IsActive);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SvcProfessional entity, CancellationToken ct = default)
        => await _context.SvcProfessionals.AddAsync(entity, ct);

    public void Update(SvcProfessional entity)
        => _context.SvcProfessionals.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
