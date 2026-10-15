using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcTimeBlockRepository : ISvcTimeBlockRepository
{
    private readonly NexoDbContext _context;
    public SvcTimeBlockRepository(NexoDbContext context) => _context = context;

    /// <summary>Blocks overlapping the window; for a professional, theirs plus the store-wide ones.</summary>
    public async Task<IReadOnlyList<SvcTimeBlock>> GetRangeAsync(
        DateTime fromUtc, DateTime toUtc, Guid? professionalId, CancellationToken ct = default)
    {
        var query = _context.SvcTimeBlocks.Where(b => b.StartsAt < toUtc && b.EndsAt > fromUtc);
        if (professionalId is { } id) query = query.Where(b => b.ProfessionalId == null || b.ProfessionalId == id);
        return await query.OrderBy(b => b.StartsAt).ToListAsync(ct);
    }

    public async Task<SvcTimeBlock?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcTimeBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task AddAsync(SvcTimeBlock block, CancellationToken ct = default)
        => await _context.SvcTimeBlocks.AddAsync(block, ct);

    public void Remove(SvcTimeBlock block) => _context.SvcTimeBlocks.Remove(block);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
