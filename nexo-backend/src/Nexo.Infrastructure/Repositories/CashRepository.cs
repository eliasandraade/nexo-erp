using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class CashRepository : ICashRepository
{
    private readonly NexoDbContext _context;

    public CashRepository(NexoDbContext context) => _context = context;

    public async Task<CashSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.CashSessions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<CashSession?> GetOpenSessionByUserAsync(Guid userId, CancellationToken ct = default)
        => await _context.CashSessions.FirstOrDefaultAsync(
            x => x.Status == CashSessionStatus.Open && x.OpenedByUserId == userId, ct);

    public async Task<CashSession?> GetOpenSessionAsync(CancellationToken ct = default)
        => await _context.CashSessions.FirstOrDefaultAsync(x => x.Status == CashSessionStatus.Open, ct);

    public async Task<IReadOnlyList<CashSession>> GetAllAsync(CancellationToken ct = default)
        => await _context.CashSessions
            .OrderByDescending(x => x.OpenedAt)
            .ToListAsync(ct);

    public async Task AddSessionAsync(CashSession session, CancellationToken ct = default)
        => await _context.CashSessions.AddAsync(session, ct);

    public async Task AddMovementAsync(CashMovement movement, CancellationToken ct = default)
        => await _context.CashMovements.AddAsync(movement, ct);

    public async Task<IReadOnlyList<CashMovement>> GetMovementsBySessionAsync(Guid sessionId, CancellationToken ct = default)
        => await _context.CashMovements
            .Where(x => x.CashSessionId == sessionId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
