using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class ReprocessLogRepository : IReprocessLogRepository
{
    private readonly NexoDbContext _context;

    public ReprocessLogRepository(NexoDbContext context) => _context = context;

    public async Task<ReprocessLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.IntReprocessLogs.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ReprocessLog>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntReprocessLogs
            .Where(x => x.MovementId == movementId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ReprocessLog log, CancellationToken ct = default)
        => await _context.IntReprocessLogs.AddAsync(log, ct);

    public void Update(ReprocessLog log)
        => _context.IntReprocessLogs.Update(log);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
