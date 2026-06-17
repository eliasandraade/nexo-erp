using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcSubjectRepository : ISvcSubjectRepository
{
    private readonly NexoDbContext _context;

    public SvcSubjectRepository(NexoDbContext context) => _context = context;

    public async Task<SvcSubject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcSubjects.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcSubject>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default)
    {
        var query = _context.SvcSubjects.AsQueryable();
        if (customerId is { } cid) query = query.Where(x => x.CustomerId == cid);
        if (kind is { } k)         query = query.Where(x => x.Kind == k);
        if (active is { } a)       query = query.Where(x => x.IsActive == a);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SvcSubject entity, CancellationToken ct = default)
        => await _context.SvcSubjects.AddAsync(entity, ct);

    public void Update(SvcSubject entity) => _context.SvcSubjects.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
