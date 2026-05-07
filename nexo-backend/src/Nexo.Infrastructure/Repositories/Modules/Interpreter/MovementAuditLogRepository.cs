using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Interpreter;

public class MovementAuditLogRepository : IMovementAuditLogRepository
{
    private readonly NexoDbContext _context;

    public MovementAuditLogRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<MovementAuditLog>> GetByMovementIdAsync(Guid movementId, CancellationToken ct = default)
        => await _context.IntAuditLogs
            .Where(x => x.MovementId == movementId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(MovementAuditLog log, CancellationToken ct = default)
        => await _context.IntAuditLogs.AddAsync(log, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
