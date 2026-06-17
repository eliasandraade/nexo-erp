using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcRecordEntryRepository : ISvcRecordEntryRepository
{
    private readonly NexoDbContext _context;

    public SvcRecordEntryRepository(NexoDbContext context) => _context = context;

    public async Task<SvcRecordEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcRecordEntries.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcRecordEntry>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default)
        => await _context.SvcRecordEntries
            .Where(x => x.ContextType == contextType && x.ContextId == contextId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SvcRecordEntry entity, CancellationToken ct = default)
        => await _context.SvcRecordEntries.AddAsync(entity, ct);

    public void Remove(SvcRecordEntry entity) => _context.SvcRecordEntries.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
